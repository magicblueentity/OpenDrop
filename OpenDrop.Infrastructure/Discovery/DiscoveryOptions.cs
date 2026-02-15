namespace OpenDrop.Infrastructure.Discovery;

public sealed record DiscoveryOptions(
    string DeviceId,
    string DisplayName,
    string? AvatarPngBase64,
    string[] Capabilities,
    int ServicePort);
