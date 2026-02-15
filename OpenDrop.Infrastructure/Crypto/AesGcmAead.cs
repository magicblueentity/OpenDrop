using System.Security.Cryptography;
using OpenDrop.Core.Crypto;

namespace OpenDrop.Infrastructure.Crypto;

public sealed class AesGcmAead : IAead
{
    private const int TagSize = 16;

    public byte[] Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> aad)
    {
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        // Wire format: ciphertext || tag
        var outBuf = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, outBuf, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, outBuf, ciphertext.Length, tag.Length);
        return outBuf;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> aad)
    {
        if (ciphertext.Length < TagSize) throw new CryptographicException("Ciphertext too short.");

        var ctLen = ciphertext.Length - TagSize;
        var ct = ciphertext.Slice(0, ctLen).ToArray();
        var tag = ciphertext.Slice(ctLen, TagSize).ToArray();
        var pt = new byte[ctLen];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ct, tag, pt, aad);
        return pt;
    }
}
