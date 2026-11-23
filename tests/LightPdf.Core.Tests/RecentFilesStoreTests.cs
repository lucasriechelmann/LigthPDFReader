namespace LightPdf.Core.Tests;

public class RecentFilesStoreTests
{
    private static string TempJson() =>
        Path.Combine(Path.GetTempPath(), "lightpdf-tests", Guid.NewGuid().ToString("N"), "recent.json");

    [Fact]
    public void Touch_MovesToFront()
    {
        var store = new RecentFilesStore(null);
        store.Touch(@"C:\a.pdf");
        store.Touch(@"C:\b.pdf");
        store.Touch(@"C:\a.pdf");

        Assert.Equal([@"C:\a.pdf", @"C:\b.pdf"], store.Items.Select(r => r.Path));
    }

    [Fact]
    public void Touch_IsCaseAndPathFormInsensitive()
    {
        var store = new RecentFilesStore(null);
        store.Touch(@"C:\Docs\File.pdf");
        store.Touch(@"c:\docs\..\docs\FILE.PDF");

        Assert.Single(store.Items);
    }

    [Fact]
    public void Touch_KeepsAtMostMaxEntries()
    {
        var store = new RecentFilesStore(null);
        for (int i = 0; i < RecentFilesStore.MaxEntries + 5; i++)
            store.Touch($@"C:\f{i}.pdf");

        Assert.Equal(RecentFilesStore.MaxEntries, store.Items.Count);
        Assert.Equal($@"C:\f{RecentFilesStore.MaxEntries + 4}.pdf", store.Items[0].Path); // newest kept
    }

    [Fact]
    public void Touch_WithoutView_KeepsStoredView()
    {
        var store = new RecentFilesStore(null);
        store.Touch(@"C:\a.pdf", new ViewState(41, 1.5, ZoomMode.Custom));
        store.Touch(@"C:\a.pdf");

        Assert.Equal(new ViewState(41, 1.5, ZoomMode.Custom), store.Find(@"C:\a.pdf")!.View);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        string json = TempJson();
        var store = new RecentFilesStore(json);
        store.Touch(@"C:\a.pdf", new ViewState(3, 2.0, ZoomMode.Custom));
        store.Touch(@"C:\b.pdf", new ViewState(7, 1.0, ZoomMode.FitPage));
        store.Save();

        var reloaded = new RecentFilesStore(json);
        reloaded.Load();

        Assert.Equal([@"C:\b.pdf", @"C:\a.pdf"], reloaded.Items.Select(r => r.Path));
        Assert.Equal(new ViewState(3, 2.0, ZoomMode.Custom), reloaded.Find(@"C:\a.pdf")!.View);
        Assert.Contains("\"FitPage\"", File.ReadAllText(json)); // enums stored readably
    }

    [Fact]
    public void Load_CorruptFile_GivesEmptyList()
    {
        string json = TempJson();
        Directory.CreateDirectory(Path.GetDirectoryName(json)!);
        File.WriteAllText(json, "{ not json");

        var store = new RecentFilesStore(json);
        store.Load();

        Assert.Empty(store.Items);
    }

    [Fact]
    public void Load_MissingFile_GivesEmptyList()
    {
        var store = new RecentFilesStore(TempJson());
        store.Load();
        Assert.Empty(store.Items);
    }

    [Fact]
    public void RemoveAndClear()
    {
        var store = new RecentFilesStore(null);
        store.Touch(@"C:\a.pdf");
        store.Touch(@"C:\b.pdf");

        store.Remove(@"C:\A.PDF");
        Assert.Equal([@"C:\b.pdf"], store.Items.Select(r => r.Path));

        store.Clear();
        Assert.Empty(store.Items);
    }
}
