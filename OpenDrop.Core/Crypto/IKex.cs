namespace OpenDrop.Core.Crypto;

public interface IKex
{
    KexKeyPair CreateIdentityKeyPair();
    byte[] DeriveSharedSecret(ReadOnlySpan<byte> myPrivateKey, ReadOnlySpan<byte> peerPublicKey);
}
