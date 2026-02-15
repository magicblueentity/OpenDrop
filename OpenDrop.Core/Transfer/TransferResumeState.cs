namespace OpenDrop.Core.Transfer;

public sealed record TransferResumeState(string SessionId, long BytesReceived, bool Finalized);
