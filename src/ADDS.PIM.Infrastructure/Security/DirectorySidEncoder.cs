using System.Buffers.Binary;
using System.Globalization;

namespace ADDS.PIM.Infrastructure.Security;

internal static class DirectorySidEncoder
{
    internal static byte[] ToBinarySid(string sid)
    {
        var parts = sid.Split('-', StringSplitOptions.None);
        if (parts.Length is < 3 or > 18 || !string.Equals(parts[0], "S", StringComparison.OrdinalIgnoreCase)
            || !byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision != 1
            || !ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var authority) || authority > 0xFFFFFFFFFFFF)
            throw new ArgumentException("The SID is invalid.", nameof(sid));

        var subAuthorityCount = parts.Length - 3;
        var bytes = new byte[8 + (subAuthorityCount * 4)];
        bytes[0] = revision;
        bytes[1] = (byte)subAuthorityCount;
        for (var index = 0; index < 6; index++) bytes[7 - index] = (byte)(authority >> (index * 8));
        for (var index = 0; index < subAuthorityCount; index++)
        {
            if (!uint.TryParse(parts[index + 3], NumberStyles.None, CultureInfo.InvariantCulture, out var subAuthority))
                throw new ArgumentException("The SID is invalid.", nameof(sid));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8 + (index * 4), 4), subAuthority);
        }
        return bytes;
    }
}
