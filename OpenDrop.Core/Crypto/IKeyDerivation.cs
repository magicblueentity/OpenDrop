namespace OpenDrop.Core.Crypto;

public interface IKeyDerivation
{
    byte[] HkdfSha256(ReadOnlySpan<byte> ikm, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> info, int length);
}
