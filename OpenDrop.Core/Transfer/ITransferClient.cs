namespace OpenDrop.Core.Transfer;

public interface ITransferClient
{
    Task<CreateSessionResult> CreateSessionAsync(Uri baseUri, TransferOffer offer, byte[] senderEphemeralPublicKey, CancellationToken cancellationToken);
    Task<TransferResumeState> GetResumeStateAsync(Uri baseUri, string sessionId, CancellationToken cancellationToken);
    Task UploadChunkAsync(Uri baseUri, string sessionId, long offset, long index, byte[] encryptedChunk, string chunkSha256Hex, CancellationToken cancellationToken);
    Task FinalizeAsync(Uri baseUri, string sessionId, CancellationToken cancellationToken);
}
