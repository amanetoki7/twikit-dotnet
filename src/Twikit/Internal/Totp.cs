using System.Security.Cryptography;
using System.Text;

namespace Twikit.Internal;

/// <summary>RFC 6238 の TOTP（Python 版の pyotp 相当）。</summary>
internal static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] Base32Decode(string input)
    {
        var cleaned = new StringBuilder();
        foreach (var c in input.ToUpperInvariant())
        {
            if (c == '=' || c == ' ' || c == '-') continue;
            cleaned.Append(c);
        }
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in cleaned.ToString())
        {
            var value = Base32Alphabet.IndexOf(c);
            if (value < 0) throw new FormatException($"Invalid base32 character: '{c}'");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return output.ToArray();
    }

    /// <summary>現在時刻の 6 桁コード（30 秒ステップ、HMAC-SHA1）。</summary>
    public static string Now(string secret, int digits = 6, int period = 30, DateTimeOffset? at = null)
    {
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / period;
        var key = Base32Decode(secret);
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);
        var otp = binary % (int)Math.Pow(10, digits);
        return otp.ToString().PadLeft(digits, '0');
    }
}
