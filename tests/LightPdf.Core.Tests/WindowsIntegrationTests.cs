using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LightPdf.Core.Tests;

public class SingleInstanceTests
{
    private static string UniqueId() => "LightPdf-Test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task SecondAcquire_Fails_UntilFirstIsDisposed()
    {
        string id = UniqueId();
        var first = SingleInstance.TryAcquire(id);
        Assert.NotNull(first);

        // A different thread, like a different process, must not get it.
        Assert.Null(await Task.Run(() => SingleInstance.TryAcquire(id)));

        first.Dispose();
        using var again = await Task.Run(() => SingleInstance.TryAcquire(id));
        Assert.NotNull(again);
    }

    [Fact]
    public async Task Send_DeliversPathsToPrimary()
    {
        string id = UniqueId();
        using var primary = SingleInstance.TryAcquire(id)!;
        var received = new TaskCompletionSource<IReadOnlyList<string>>();
        primary.Listen(paths => received.TrySetResult(paths));

        string[] paths = [@"C:\docs\a.pdf", @"C:\ünïcødé\日本語 b.pdf"];
        Assert.True(SingleInstance.TrySend(id, paths, TimeSpan.FromSeconds(5)));

        Assert.Equal(paths, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Send_EmptyMessage_MeansJustActivate()
    {
        string id = UniqueId();
        using var primary = SingleInstance.TryAcquire(id)!;
        var received = new TaskCompletionSource<IReadOnlyList<string>>();
        primary.Listen(paths => received.TrySetResult(paths));

        Assert.True(SingleInstance.TrySend(id, [], TimeSpan.FromSeconds(5)));
        Assert.Empty(await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Listener_HandlesSeveralMessagesInARow()
    {
        string id = UniqueId();
        using var primary = SingleInstance.TryAcquire(id)!;
        var all = new List<string>();
        var done = new TaskCompletionSource();
        primary.Listen(paths => { lock (all) { all.AddRange(paths); if (all.Count == 3) done.TrySetResult(); } });

        for (int i = 0; i < 3; i++)
            Assert.True(SingleInstance.TrySend(id, [$@"C:\{i}.pdf"], TimeSpan.FromSeconds(5)));

        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void Send_WithNoPrimary_FailsQuickly()
        => Assert.False(SingleInstance.TrySend(UniqueId(), [@"C:\a.pdf"], TimeSpan.FromMilliseconds(200)));
}

/// <summary>Runs against a throwaway key under HKCU\Software\LightPdf.Tests, never the real file associations.</summary>
[SupportedOSPlatform("windows")]
public sealed class FileAssociationTests : IDisposable
{
    private const string Exe = @"C:\Apps\LightPdf\LightPdf.exe";
    private const string Icon = @"C:\Apps\LightPdf\pdf-document.ico";
    private readonly string _rootPath = @"Software\LightPdf.Tests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;
    private readonly FileAssociation _assoc;

    public FileAssociationTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_rootPath);
        _assoc = new FileAssociation(_root);
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\LightPdf.Tests", writable: true);
        if (parent is { SubKeyCount: 0 }) Registry.CurrentUser.DeleteSubKey(@"Software\LightPdf.Tests", false);
    }

    private string? Value(string path, string name = "")
    {
        using var key = _root.OpenSubKey(path);
        return key?.GetValue(name) as string;
    }

    [Fact]
    public void Register_WritesProgIdOpenCommandAndIcon()
    {
        _assoc.Register(Exe, Icon);

        Assert.Equal($"\"{Exe}\" \"%1\"", Value(@"Software\Classes\LightPdf.Document\shell\open\command"));
        Assert.Equal(Icon, Value(@"Software\Classes\LightPdf.Document\DefaultIcon"));
        using var openWith = _root.OpenSubKey(@"Software\Classes\.pdf\OpenWithProgids");
        Assert.Contains(FileAssociation.ProgId, openWith!.GetValueNames());
        Assert.True(_assoc.IsRegistered(Exe));
    }

    [Fact]
    public void Register_ListsAppInDefaultApps()
    {
        _assoc.Register(Exe, Icon);

        Assert.Equal(FileAssociation.CapabilitiesPath, Value(@"Software\RegisteredApplications", FileAssociation.AppName));
        Assert.Equal(FileAssociation.ProgId, Value(FileAssociation.CapabilitiesPath + @"\FileAssociations", ".pdf"));
    }

    [Fact]
    public void Register_DoesNotTouchTheDefaultPdfHandler()
    {
        using (var pdf = _root.CreateSubKey(@"Software\Classes\.pdf")) pdf.SetValue("", "SomeOtherApp.Document");

        _assoc.Register(Exe, Icon);

        Assert.Equal("SomeOtherApp.Document", Value(@"Software\Classes\.pdf"));
    }

    [Fact]
    public void Unregister_RemovesOnlyOurEntries()
    {
        using (var other = _root.CreateSubKey(@"Software\Classes\.pdf\OpenWithProgids")) other.SetValue("Other.Pdf", "");
        _assoc.Register(Exe, Icon);

        _assoc.Unregister(Exe);

        Assert.False(_assoc.IsRegistered(Exe));
        Assert.Null(_root.OpenSubKey(@"Software\Classes\LightPdf.Document"));
        Assert.Null(_root.OpenSubKey(FileAssociation.CapabilitiesPath));
        Assert.Null(Value(@"Software\RegisteredApplications", FileAssociation.AppName));
        using var openWith = _root.OpenSubKey(@"Software\Classes\.pdf\OpenWithProgids");
        Assert.Equal(["Other.Pdf"], openWith!.GetValueNames());
    }

    [Fact]
    public void IsRegistered_FalseForADifferentExe()
    {
        _assoc.Register(Exe, Icon);
        Assert.False(_assoc.IsRegistered(@"D:\Elsewhere\LightPdf.exe"));
    }
}
