using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using OpenDrop.Core.Crypto;
using OpenDrop.Core.Security;
using OpenDrop.Core.Transfer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenDrop.Infrastructure.Transfer;

public sealed class TransferServer : IAsyncDisposable
{
    private readonly ILogger<TransferServer> _log;
    private readonly TransferServerOptions _options;
    private readonly IKex _kex;
    private readonly IKeyDerivation _kdf;
    private readonly IAead _aead;

    private readonly ConcurrentDictionary<string, TransferSession> _sessions = new(StringComparer.Ordinal);
    private WebApplication? _app;
    private Task? _runTask;

    // Basic abuse protection: per-remote-IP request tokens.
    private readonly TokenBucketLimiter _limiter = new(capacity: 60, refillPerSecond: 30); // burst 60, ~30 req/s sustained

    public event EventHandler<TransferOffer>? OfferReceived;

    public TransferServer(
        TransferServerOptions options,
        IKex kex,
        IKeyDerivation kdf,
        IAead aead,
        ILogger<TransferServer> log)
    {
        _options = options;
        _kex = kex;
        _kdf = kdf;
        _aead = aead;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_app is not null) return;

        Directory.CreateDirectory(_options.StorageDirectory);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(TransferServer).Assembly.FullName,
            Args = Array.Empty<string>(),
            EnvironmentName = Environments.Production
        });

        builder.Logging.ClearProviders(); // app should configure logging; keep minimal here

        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Listen(IPAddress.Parse(_options.BindAddress), _options.Port);
        });

        var app = builder.Build();

        app.Use(async (ctx, next) =>
        {
            var key = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!_limiter.TryConsume(key, 1))
            {
                ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }
            await next();
        });

        app.MapPost("/api/v1/sessions", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<CreateSessionRequest>(cancellationToken: ctx.RequestAborted);
            if (req is null) return Results.BadRequest();

            if (req.Offer.FileSize <= 0 || req.Offer.FileSize > _options.MaxFileSizeBytes)
                return Results.BadRequest("Invalid file size.");

            var ext = Path.GetExtension(req.Offer.FileName);
            if (_options.AllowedExtensions.Length > 0 &&
                !string.IsNullOrWhiteSpace(ext) &&
                !_options.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                return Results.BadRequest("File type not allowed.");
            }

            // Generate receiver ephemeral key for this session.
            var recvKp = _kex.CreateIdentityKeyPair();
            var salt = RandomNumberGenerator.GetBytes(16);
            var (key, baseNonce) = TransferCrypto.DeriveAeadKeyAndNonce(
                _kex, _kdf,
                myPriv: recvKp.PrivateKey,
                peerPub: req.SenderEphemeralPublicKey,
                salt: salt,
                infoLabel: "airdroplike/v1/session");

            var sessionId = Guid.NewGuid().ToString("n");
            var safeName = PathSafety.SanitizeFileName(req.Offer.FileName);
            var tempPath = Path.Combine(_options.StorageDirectory, $"{sessionId}.{safeName}.part");

            var offer = req.Offer with
            {
                SessionId = sessionId,
                CreatedUtc = DateTimeOffset.UtcNow,
                FileName = safeName
            };

            var session = new TransferSession
            {
                Offer = offer,
                SessionId = sessionId,
                SenderEphemeralPublicKey = req.SenderEphemeralPublicKey,
                ReceiverEphemeralPrivateKey = recvKp.PrivateKey,
                ReceiverEphemeralPublicKey = recvKp.PublicKey,
                Salt = salt,
                AeadKey = key,
                BaseNonce = baseNonce,
                TempPath = tempPath,
                BytesReceived = 0,
                Finalized = false
            };

            _sessions[sessionId] = session;

            OfferReceived?.Invoke(this, offer);
            _log.LogInformation("Offer received: {Name} ({Bytes} bytes) session {SessionId}.", offer.FileName, offer.FileSize, sessionId);

            return Results.Ok(new CreateSessionResponse(sessionId, recvKp.PublicKey, salt));
        });

        app.MapGet("/api/v1/sessions/{sessionId}/status", (string sessionId) =>
        {
            if (!_sessions.TryGetValue(sessionId, out var s)) return Results.NotFound();
            return Results.Ok(new TransferResumeState(sessionId, s.BytesReceived, s.Finalized));
        });

        app.MapPut("/api/v1/sessions/{sessionId}/chunks", async (HttpContext ctx, string sessionId) =>
        {
            if (!_sessions.TryGetValue(sessionId, out var s)) return Results.NotFound();
            if (s.Finalized) return Results.BadRequest("Already finalized.");

            if (!long.TryParse(ctx.Request.Query["offset"], out var offset) || offset < 0)
                return Results.BadRequest("Missing/invalid offset.");
            if (!long.TryParse(ctx.Request.Query["index"], out var index) || index < 0)
                return Results.BadRequest("Missing/invalid index.");

            var declaredSha = ctx.Request.Query["sha256"].ToString();
            if (string.IsNullOrWhiteSpace(declaredSha)) return Results.BadRequest("Missing sha256.");

            if (offset != s.BytesReceived)
                return Results.Conflict("Offset mismatch.");

            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted);
            var enc = ms.ToArray();

            if (!string.Equals(TransferCrypto.Sha256Hex(enc), declaredSha, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("Chunk hash mismatch.");

            var nonce = TransferCrypto.NonceForChunk(s.BaseNonce, index);
            byte[] pt;
            try
            {
                // AAD binds the chunk location to the ciphertext.
                var aad = System.Text.Encoding.UTF8.GetBytes($"{sessionId}:{offset}:{index}");
                pt = _aead.Decrypt(s.AeadKey, nonce, enc, aad);
            }
            catch (CryptographicException)
            {
                return Results.Unauthorized();
            }

            if (s.BytesReceived + pt.Length > s.Offer.FileSize)
                return Results.BadRequest("Too much data.");

            Directory.CreateDirectory(Path.GetDirectoryName(s.TempPath)!);
            await using (var fs = new FileStream(s.TempPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                await fs.WriteAsync(pt, ctx.RequestAborted);
                await fs.FlushAsync(ctx.RequestAborted);
            }

            s.BytesReceived += pt.Length;
            return Results.NoContent();
        });

        app.MapPost("/api/v1/sessions/{sessionId}/finalize", async (string sessionId) =>
        {
            if (!_sessions.TryGetValue(sessionId, out var s)) return Results.NotFound();
            if (s.Finalized) return Results.NoContent();

            if (s.BytesReceived != s.Offer.FileSize)
                return Results.Conflict("Incomplete.");

            // Verify final hash.
            await using var fs = new FileStream(s.TempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = await SHA256.HashDataAsync(fs, cancellationToken);
            var hex = Convert.ToHexString(hash).ToLowerInvariant();
            if (!string.Equals(hex, s.Offer.Sha256Hex, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("File hash mismatch.");

            var finalPath = Path.Combine(_options.StorageDirectory, s.Offer.FileName);
            finalPath = EnsureUnique(finalPath);
            File.Move(s.TempPath, finalPath);
            s.Finalized = true;

            _log.LogInformation("Transfer finalized: {Path}", finalPath);
            return Results.Ok(new { path = finalPath });
        });

        _app = app;
        _runTask = app.RunAsync(cancellationToken);
        await Task.CompletedTask;
    }

    private static string EnsureUnique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("Unable to pick unique filename.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is null) return;
        try
        {
            await _app.StopAsync();
        }
        catch { /* ignore */ }
        await _app.DisposeAsync();
        _app = null;
        if (_runTask is not null) await _runTask;
    }

    private sealed record CreateSessionRequest(TransferOffer Offer, byte[] SenderEphemeralPublicKey);
    private sealed record CreateSessionResponse(string SessionId, byte[] ReceiverEphemeralPublicKey, byte[] Salt);
}
