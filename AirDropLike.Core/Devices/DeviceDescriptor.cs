namespace AirDropLike.Core.Devices;

public sealed record DeviceDescriptor(
    string DeviceId,
    string DisplayName,
    string? AvatarPngBase64,
    string[] Capabilities,
    string ServiceHost,
    int ServicePort,
    DateTimeOffset LastSeenUtc);
