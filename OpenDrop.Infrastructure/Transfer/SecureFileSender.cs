using System.Security.Cryptography;
using OpenDrop.Core.Crypto;
using OpenDrop.Core.Transfer;
using Microsoft.Extensions.Logging;

namespace OpenDrop.Infrastructure.Transfer;

public sealed class SecureFileSender
{
    private readonly ITransferClient _client;
    private readonly IKex _kex;
    private readonly IKeyDerivation _kdf;
    private readonly IAead _aead;
    private readonly ILogger<SecureFileSender> _log;

    public SecureFileSender(ITransferClient client, IKex kex, IKeyDerivation kdf, IAead aead, ILogger<SecureFileSender> log)
    {
        _client = client;
        _kex = kex;
        _kdf = kdf;
        _aead = aead;
        _log = log;
    }

    public async Task SendFileAsync(Uri baseUri, string senderDeviceId, string filePath, CancellationToken cancellationToken)
    {
        var fi = new FileInfo(filePath);
        if (!fi.Exists) throw new FileNotFoundException("File not found.", filePath);

        var shaHex = await ComputeSha256HexAsync(filePath, cancellationToken);
        var offer = new TransferOffer(
            SessionId: "",
            SenderDeviceId: senderDeviceId,
            FileName: fi.Name,
            FileSize: fi.Length,
            ContentType: "application/octet-stream",
            Sha256Hex: shaHex,
            CreatedUtc: DateTimeOffset.UtcNow);

        var eph = _kex.CreateIdentityKeyPair();
        var session = await _client.CreateSessionAsync(baseUri, offer, eph.PublicKey, cancellationToken);

        var (key, baseNonce) = TransferCrypto.DeriveAeadKeyAndNonce(
            _kex, _kdf,
            myPriv: eph.PrivateKey,
            peerPub: session.ReceiverEphemeralPublicKey,
            salt: session.Salt,
            infoLabel: "airdroplike/v1/session");

        var sessionId = session.SessionId;
        var state = await _client.GetResumeStateAsync(baseUri, sessionId, cancellationToken);

        const int chunkSize = 1024 * 1024; // 1 MiB
        long offset = state.BytesReceived;
        long chunkIndex = offset / chunkSize;

        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(offset, SeekOrigin.Begin);

        var buf = new byte[chunkSize];
        while (offset < fi.Length)
        {
            var read = await fs.ReadAsync(buf, cancellationToken);
            if (read == 0) break;

            // Avoid Span<T> locals in async methods (ref-like locals cannot be in scope across awaits).
            var pt = new byte[read];
            Buffer.BlockCopy(buf, 0, pt, 0, read);
            var nonce = TransferCrypto.NonceForChunk(baseNonce, chunkIndex);
            var aad = System.Text.Encoding.UTF8.GetBytes($"{sessionId}:{offset}:{chunkIndex}");
            var enc = _aead.Encrypt(key, nonce, pt, aad);
            var encSha = TransferCrypto.Sha256Hex(enc);

            await _client.UploadChunkAsync(baseUri, sessionId, offset, chunkIndex, enc, encSha, cancellationToken);

            offset += read;
            chunkIndex++;
        }

        await _client.FinalizeAsync(baseUri, sessionId, cancellationToken);
        _log.LogInformation("Sent {Path} to {BaseUri} session {SessionId}.", filePath, baseUri, sessionId);

        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(baseNonce);
        CryptographicOperations.ZeroMemory(eph.PrivateKey);
    }

    private static async Task<string> ComputeSha256HexAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(fs, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
