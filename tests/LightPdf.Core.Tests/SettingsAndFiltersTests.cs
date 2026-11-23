using System.Runtime.InteropServices;

namespace LightPdf.Core.Tests;

public class SettingsStoreTests
{
    private static string TempJson() =>
        Path.Combine(Path.GetTempPath(), "lightpdf-tests", Guid.NewGuid().ToString("N"), "settings.json");

    [Fact]
    public void Defaults_FollowSystemTheme_NoInversion()
    {
        var store = new SettingsStore(TempJson());
        store.Load();
        Assert.Equal(ThemePreference.System, store.Current.Theme);
        Assert.False(store.Current.InvertPageColors);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        string path = TempJson();
        var store = new SettingsStore(path);
        store.Current.Theme = ThemePreference.Light;
        store.Current.InvertPageColors = true;
        store.Save();

        var reloaded = new SettingsStore(path);
        reloaded.Load();
        Assert.Equal(ThemePreference.Light, reloaded.Current.Theme);
        Assert.True(reloaded.Current.InvertPageColors);
        Assert.Contains("\"Light\"", File.ReadAllText(path));
    }

    [Fact]
    public void CorruptFile_GivesDefaults()
    {
        string path = TempJson();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ nope");
        var store = new SettingsStore(path);
        store.Load();
        Assert.Equal(ThemePreference.System, store.Current.Theme);
    }
}

public class ColorFiltersTests
{
    /// <summary>Runs the filter on one BGRA pixel and returns (r, g, b).</summary>
    private static (int R, int G, int B) Invert(byte r, byte g, byte b)
    {
        IntPtr p = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteByte(p, 0, b);
            Marshal.WriteByte(p, 1, g);
            Marshal.WriteByte(p, 2, r);
            Marshal.WriteByte(p, 3, 255);
            ColorFilters.InvertLightness(p, 1, 1, 4);
            Assert.Equal(255, Marshal.ReadByte(p, 3)); // alpha untouched
            return (Marshal.ReadByte(p, 2), Marshal.ReadByte(p, 1), Marshal.ReadByte(p, 0));
        }
        finally
        {
            Marshal.FreeHGlobal(p);
        }
    }

    [Fact]
    public void WhitePaper_BecomesBlack() => Assert.Equal((0, 0, 0), Invert(255, 255, 255));

    [Fact]
    public void BlackText_BecomesWhite() => Assert.Equal((255, 255, 255), Invert(0, 0, 0));

    [Fact]
    public void MidGray_StaysAboutTheSame()
    {
        var (r, g, b) = Invert(128, 128, 128);
        Assert.InRange(r, 125, 131);
        Assert.Equal(r, g);
        Assert.Equal(g, b);
    }

    [Fact]
    public void BlueLink_BecomesLightBlue_NotOrange()
    {
        var (r, g, b) = Invert(0, 0, 255);
        Assert.True(b > r && b > g, $"hue should stay blue, got ({r},{g},{b})");
        Assert.True(r > 150, "and get much lighter");
    }

    [Fact]
    public void RespectsStride()
    {
        // 1x2 image with 8 padding bytes per row: padding must not be touched.
        const int stride = 12;
        IntPtr p = Marshal.AllocHGlobal(stride * 2);
        try
        {
            for (int i = 0; i < stride * 2; i++) Marshal.WriteByte(p, i, 0xAB);
            Marshal.WriteInt32(p, 0, unchecked((int)0xFFFFFFFF));          // row 0: white
            Marshal.WriteInt32(p, stride, unchecked((int)0xFF000000));     // row 1: black
            ColorFilters.InvertLightness(p, 1, 2, stride);

            Assert.Equal(unchecked((int)0xFF000000), Marshal.ReadInt32(p, 0));
            Assert.Equal(unchecked((int)0xFFFFFFFF), Marshal.ReadInt32(p, stride));
            Assert.Equal(0xAB, Marshal.ReadByte(p, 4));                    // padding intact
        }
        finally
        {
            Marshal.FreeHGlobal(p);
        }
    }
}
