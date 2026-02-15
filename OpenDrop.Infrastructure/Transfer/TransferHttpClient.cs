using System.Net.Http.Json;
using OpenDrop.Core.Transfer;

namespace OpenDrop.Infrastructure.Transfer;

public sealed class TransferHttpClient : ITransferClient
{
    private readonly HttpClient _http;

    public TransferHttpClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<CreateSessionResult> CreateSessionAsync(Uri baseUri, TransferOffer offer, byte[] senderEphemeralPublicKey, CancellationToken cancellationToken)
    {
        var req = new CreateSessionRequest(offer, senderEphemeralPublicKey);
        var resp = await _http.PostAsJsonAsync(new Uri(baseUri, "/api/v1/sessions"), req, cancellationToken);
        resp.EnsureSuccessStatusCode();
        var data = await resp.Content.ReadFromJsonAsync<CreateSessionResponse>(cancellationToken: cancellationToken);
        if (data is null) throw new InvalidOperationException("Bad response.");
        return new CreateSessionResult(data.SessionId, data.ReceiverEphemeralPublicKey, data.Salt);
    }

    public async Task<TransferResumeState> GetResumeStateAsync(Uri baseUri, string sessionId, CancellationToken cancellationToken)
    {
        var data = await _http.GetFromJsonAsync<TransferResumeState>(new Uri(baseUri, $"/api/v1/sessions/{sessionId}/status"), cancellationToken);
        return data ?? throw new InvalidOperationException("Bad response.");
    }

    public async Task UploadChunkAsync(Uri baseUri, string sessionId, long offset, long index, byte[] encryptedChunk, string chunkSha256Hex, CancellationToken cancellationToken)
    {
        var uri = new Uri(baseUri, $"/api/v1/sessions/{sessionId}/chunks?offset={offset}&index={index}&sha256={chunkSha256Hex}");
        using var content = new ByteArrayContent(encryptedChunk);
        var resp = await _http.PutAsync(uri, content, cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public async Task FinalizeAsync(Uri baseUri, string sessionId, CancellationToken cancellationToken)
    {
        var resp = await _http.PostAsync(new Uri(baseUri, $"/api/v1/sessions/{sessionId}/finalize"), content: null, cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record CreateSessionRequest(TransferOffer Offer, byte[] SenderEphemeralPublicKey);
    private sealed record CreateSessionResponse(string SessionId, byte[] ReceiverEphemeralPublicKey, byte[] Salt);
}
