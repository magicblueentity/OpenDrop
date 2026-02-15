using OpenDrop.Infrastructure.Crypto;
using OpenDrop.Infrastructure.Transfer;
using Xunit;

namespace OpenDrop.Tests;

public sealed class CryptoRoundTripTests
{
    [Fact]
    public void AesGcm_RoundTrip_WithChunkNonce()
    {
        var kex = new EcdhP256Kex();
        var kdf = new HkdfSha256Deriver();
        var aead = new AesGcmAead();

        var a = kex.CreateIdentityKeyPair();
        var b = kex.CreateIdentityKeyPair();
        var salt = new byte[16];
        Random.Shared.NextBytes(salt);

        var (ka, na) = TransferCrypto.DeriveAeadKeyAndNonce(kex, kdf, a.PrivateKey, b.PublicKey, salt, "airdroplike/v1/session");
        var (kb, nb) = TransferCrypto.DeriveAeadKeyAndNonce(kex, kdf, b.PrivateKey, a.PublicKey, salt, "airdroplike/v1/session");

        Assert.Equal(ka, kb);
        Assert.Equal(na, nb);

        var nonce = TransferCrypto.NonceForChunk(na, 42);
        var aad = System.Text.Encoding.UTF8.GetBytes("sess:0:42");
        var pt = System.Text.Encoding.UTF8.GetBytes("hello");
        var ct = aead.Encrypt(ka, nonce, pt, aad);
        var back = aead.Decrypt(kb, nonce, ct, aad);

        Assert.Equal(pt, back);
    }
}
