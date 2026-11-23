namespace LightPdf.Core;

public static class ColorFilters
{
    /// <summary>
    /// Night-reading filter for opaque BGRA pixels: inverts each pixel's lightness while keeping its hue,
    /// by adding (255 − 2·L) to every channel. White paper becomes black, black text becomes white,
    /// a blue link becomes light blue, and photos stay recognizable (unlike a plain negative).
    /// </summary>
    public static unsafe void InvertLightness(IntPtr bgra, int width, int height, int stride)
    {
        for (int y = 0; y < height; y++)
        {
            byte* p = (byte*)bgra + (long)y * stride;
            for (int x = 0; x < width; x++, p += 4)
            {
                int b = p[0], g = p[1], r = p[2];
                int lightness = (r * 77 + g * 150 + b * 29) >> 8; // Rec. 601 luma, 0..255
                int shift = 255 - 2 * lightness;
                p[0] = Clamp(b + shift);
                p[1] = Clamp(g + shift);
                p[2] = Clamp(r + shift);
                // p[3] (alpha) unchanged: pages are opaque.
            }
        }
    }

    private static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
}
