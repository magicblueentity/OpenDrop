namespace OpenDrop.Core.Transfer;

public sealed record TransferOffer(
    string SessionId,
    string SenderDeviceId,
    string FileName,
    long FileSize,
    string ContentType,
    string Sha256Hex,
    DateTimeOffset CreatedUtc);

public sealed record TransferProgress(
    string SessionId,
    long BytesSent,
    long BytesTotal,
    double Fraction,
    string? Status);
