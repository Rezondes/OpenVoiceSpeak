using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OVS.Client.Localization;
using OVS.Shared.Protocol;

namespace OVS.Client.Services;

/// <summary>
/// Package 168: turns a chosen image into the profile picture to upload. PNG, JPG, GIF or WebP up to 2 MB; an animated
/// file gives its first frame. The picture is scaled so its shorter side is 128 px, cut to the middle square and written
/// by <see cref="AvatarFormat.Encode"/>: a PNG of pixels only, without any of the file's metadata.
/// </summary>
internal static class AvatarImport
{
    /// <summary>The largest image file accepted.</summary>
    public const long MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>Reads an image and makes the PNG to upload, or says why not.</summary>
    public static IconImportResult Prepare(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) return new IconImportResult(null, Strings.Icon_Missing);
        if (file.Length > MaxFileBytes) return new IconImportResult(null, Strings.Avatar_TooBig);
        var head = new byte[12];
        using (var stream = file.OpenRead()) stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        if (!IsKnownFormat(head)) return new IconImportResult(null, Strings.Avatar_Format);

        Bitmap image;
        try
        {
            using var stream = file.OpenRead();
            #pragma warning disable CA2000 // disposed by the using block below
            image = new Bitmap(stream); // the first frame of an animated file
            #pragma warning restore CA2000
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new IconImportResult(null, Strings.Icon_Unreadable);
        }
        using (image)
        {
            var (width, height) = (image.PixelSize.Width, image.PixelSize.Height);
            double scale = (double)AvatarFormat.Size / Math.Min(width, height);
            var scaledSize = new PixelSize(Math.Max(AvatarFormat.Size, (int)Math.Round(width * scale)), Math.Max(AvatarFormat.Size, (int)Math.Round(height * scale)));
            using var scaled = image.CreateScaledBitmap(scaledSize, BitmapInterpolationMode.HighQuality);
            var square = new PixelRect((scaledSize.Width - AvatarFormat.Size) / 2, (scaledSize.Height - AvatarFormat.Size) / 2, AvatarFormat.Size, AvatarFormat.Size);
            return new IconImportResult(AvatarFormat.Encode(Rgba(scaled, square)), null);
        }
    }

    /// <summary>PNG, JPG, GIF or WebP by their first bytes.</summary>
    static bool IsKnownFormat(ReadOnlySpan<byte> head) =>
        head.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
        || head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])
        || head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8)
        || (head.StartsWith("RIFF"u8) && head[8..].StartsWith("WEBP"u8));

    /// <summary>The pixels of <paramref name="area"/> as RGBA, not premultiplied, whatever the bitmap holds them as.</summary>
    static byte[] Rgba(Bitmap bitmap, PixelRect area)
    {
        int stride = area.Width * 4;
        var pixels = new byte[stride * area.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(area, handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }
        bool bgra = bitmap.Format is not { } format || format == PixelFormat.Bgra8888;
        bool premultiplied = bitmap.AlphaFormat is null or AlphaFormat.Premul;
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (bgra) (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);
            byte alpha = pixels[index + 3];
            if (!premultiplied || alpha is 0 or 255) continue;
            for (int channel = 0; channel < 3; channel++) pixels[index + channel] = (byte)Math.Min(255, pixels[index + channel] * 255 / alpha);
        }
        return pixels;
    }
}
