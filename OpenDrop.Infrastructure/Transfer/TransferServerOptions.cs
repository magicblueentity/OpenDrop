namespace OpenDrop.Infrastructure.Transfer;

public sealed record TransferServerOptions(
    string BindAddress,
    int Port,
    string StorageDirectory,
    long MaxFileSizeBytes,
    string[] AllowedExtensions);
