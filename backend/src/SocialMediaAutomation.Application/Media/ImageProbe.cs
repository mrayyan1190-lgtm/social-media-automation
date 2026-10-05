using System.Buffers.Binary;
using SocialMediaAutomation.Domain.Common;

namespace SocialMediaAutomation.Application.Media;

/// <summary>
/// Identifies JPEG/PNG from the file's own bytes (never the extension or client Content-Type) and reads its pixel size
/// from the header. Returns null for anything else, including truncated or malformed files.
/// </summary>
public static class ImageProbe
{
    public sealed record ImageInfo(string ContentType, int Width, int Height, string Extension);

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static ImageInfo? Inspect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual(PngSignature) && data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            var width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4));
            return width > 0 && height > 0 ? new ImageInfo(PlatformRules.Png, width, height, ".png") : null;
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return InspectJpeg(data);

        return null;
    }

    // Walks JPEG marker segments until a start-of-frame marker, which carries height and width.
    private static ImageInfo? InspectJpeg(ReadOnlySpan<byte> data)
    {
        var position = 2;
        while (position + 4 <= data.Length)
        {
            if (data[position] != 0xFF)
                return null;
            var marker = data[position + 1];
            if (marker == 0xFF)
            {
                position++; // fill byte
                continue;
            }
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                position += 2; // markers without a length field
                continue;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 2, 2));
            if (segmentLength < 2)
                return null;

            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isStartOfFrame)
            {
                if (position + 9 > data.Length)
                    return null;
                var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 5, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 7, 2));
                return width > 0 && height > 0 ? new ImageInfo(PlatformRules.Jpeg, width, height, ".jpg") : null;
            }

            position += 2 + segmentLength;
        }
        return null;
    }
}
