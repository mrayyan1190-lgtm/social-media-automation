using System.Buffers.Binary;
using System.Text;
using SocialMediaAutomation.Application.Media;

namespace SocialMediaAutomation.Application.Tests;

public class ImageProbeTests
{
    /// <summary>A PNG signature + IHDR header with the given size (enough for header inspection).</summary>
    public static byte[] PngHeader(int width, int height, int padding = 0)
    {
        var bytes = new byte[33 + padding];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 }.CopyTo(bytes, 0);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    /// <summary>SOI, an APP0 segment, then a baseline SOF0 frame header with the given size.</summary>
    public static byte[] JpegHeader(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        bytes.AddRange("JFIF\0"u8.ToArray());
        bytes.AddRange(new byte[9]);
        bytes.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03 });
        bytes.AddRange(new byte[9]);
        bytes.AddRange(new byte[] { 0xFF, 0xD9 });
        return [.. bytes];
    }

    [Fact]
    public void Png_type_and_size_come_from_the_header() =>
        Assert.Equal(new ImageProbe.ImageInfo("image/png", 1200, 628, ".png"), ImageProbe.Inspect(PngHeader(1200, 628)));

    [Fact]
    public void Jpeg_size_is_read_from_the_start_of_frame_segment() =>
        Assert.Equal(new ImageProbe.ImageInfo("image/jpeg", 1080, 1350, ".jpg"), ImageProbe.Inspect(JpegHeader(1080, 1350)));

    [Theory]
    [InlineData("GIF89a....")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'/>")]
    [InlineData("just some text pretending to be an image.png")]
    public void Other_content_is_not_an_image(string content) =>
        Assert.Null(ImageProbe.Inspect(Encoding.UTF8.GetBytes(content)));

    [Fact]
    public void Truncated_files_are_rejected()
    {
        Assert.Null(ImageProbe.Inspect(PngHeader(10, 10)[..20]));
        Assert.Null(ImageProbe.Inspect(JpegHeader(10, 10)[..12]));
    }
}
