using System.Security.Cryptography;
using OpenDrop.Core.Crypto;

namespace OpenDrop.Infrastructure.Crypto;

public sealed class HkdfSha256Deriver : IKeyDerivation
{
    public byte[] HkdfSha256(ReadOnlySpan<byte> ikm, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> info, int length)
    {
        // HKDF-Extract
        using var hmac = new HMACSHA256(salt.ToArray());
        var prk = hmac.ComputeHash(ikm.ToArray());

        // HKDF-Expand
        var okm = new byte[length];
        var t = Array.Empty<byte>();
        var offset = 0;
        byte counter = 1;

        using var hmac2 = new HMACSHA256(prk);
        while (offset < length)
        {
            var input = new byte[t.Length + info.Length + 1];
            Buffer.BlockCopy(t, 0, input, 0, t.Length);
            info.CopyTo(input.AsSpan(t.Length));
            input[^1] = counter++;

            t = hmac2.ComputeHash(input);
            var toCopy = Math.Min(t.Length, length - offset);
            Buffer.BlockCopy(t, 0, okm, offset, toCopy);
            offset += toCopy;
        }

        CryptographicOperations.ZeroMemory(prk);
        return okm;
    }
}
