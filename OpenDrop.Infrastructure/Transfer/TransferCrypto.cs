using System.Security.Cryptography;
using OpenDrop.Core.Crypto;

namespace OpenDrop.Infrastructure.Transfer;

public static class TransferCrypto
{
    public static (byte[] key, byte[] baseNonce) DeriveAeadKeyAndNonce(
        IKex kex,
        IKeyDerivation kdf,
        ReadOnlySpan<byte> myPriv,
        ReadOnlySpan<byte> peerPub,
        ReadOnlySpan<byte> salt,
        string infoLabel)
    {
        var shared = kex.DeriveSharedSecret(myPriv, peerPub);
        try
        {
            var okm = kdf.HkdfSha256(shared, salt, System.Text.Encoding.UTF8.GetBytes(infoLabel), 32 + 12);
            var key = okm.AsSpan(0, 32).ToArray();
            var nonce = okm.AsSpan(32, 12).ToArray();
            return (key, nonce);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
        }
    }

    public static byte[] NonceForChunk(ReadOnlySpan<byte> baseNonce12, long chunkIndex)
    {
        if (baseNonce12.Length != 12) throw new ArgumentException("baseNonce must be 12 bytes.");
        var nonce = baseNonce12.ToArray();

        // XOR chunkIndex into last 8 bytes (big-endian) to get a unique nonce per chunk.
        Span<byte> idx = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(idx, chunkIndex);
        for (int i = 0; i < 8; i++)
            nonce[4 + i] ^= idx[i];

        return nonce;
    }

    public static string Sha256Hex(ReadOnlySpan<byte> data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
