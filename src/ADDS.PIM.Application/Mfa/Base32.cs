using System.Text;

namespace ADDS.PIM.Application.Mfa;

/// <summary>RFC 4648 base32 (unpadded), the encoding TOTP provisioning URIs conventionally use for the shared secret.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return string.Empty;

        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                output.Append(Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }

        if (bitsLeft > 0)
        {
            output.Append(Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }

        return output.ToString();
    }
}
