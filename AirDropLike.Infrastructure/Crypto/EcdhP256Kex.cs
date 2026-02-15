using System.Security.Cryptography;
using AirDropLike.Core.Crypto;

namespace AirDropLike.Infrastructure.Crypto;

public sealed class EcdhP256Kex : IKex
{
    public KexKeyPair CreateIdentityKeyPair()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var pub = ecdh.PublicKey.ExportSubjectPublicKeyInfo();
        var priv = ecdh.ExportPkcs8PrivateKey();
        return new KexKeyPair(pub, priv);
    }

    public byte[] DeriveSharedSecret(ReadOnlySpan<byte> myPrivateKey, ReadOnlySpan<byte> peerPublicKey)
    {
        using var mine = ECDiffieHellman.Create();
        mine.ImportPkcs8PrivateKey(myPrivateKey, out _);

        using var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(peerPublicKey, out _);

        // Produces a shared secret suitable as IKM for HKDF.
        return mine.DeriveKeyMaterial(peer.PublicKey);
    }
}
