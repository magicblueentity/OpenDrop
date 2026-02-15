namespace AirDropLike.Infrastructure.Discovery;

public sealed record DiscoveryOptions(
    string DeviceId,
    string DisplayName,
    string? AvatarPngBase64,
    string[] Capabilities,
    int ServicePort);
