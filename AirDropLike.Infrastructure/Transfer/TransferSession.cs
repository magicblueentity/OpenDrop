using AirDropLike.Core.Transfer;

namespace AirDropLike.Infrastructure.Transfer;

internal sealed class TransferSession
{
    public required TransferOffer Offer { get; init; }
    public required string SessionId { get; init; }

    public required byte[] SenderEphemeralPublicKey { get; init; }
    public required byte[] ReceiverEphemeralPrivateKey { get; init; }
    public required byte[] ReceiverEphemeralPublicKey { get; init; }

    public required byte[] Salt { get; init; }
    public required byte[] AeadKey { get; init; } // 32 bytes
    public required byte[] BaseNonce { get; init; } // 12 bytes

    public required string TempPath { get; init; }
    public long BytesReceived { get; set; }
    public bool Finalized { get; set; }
}
