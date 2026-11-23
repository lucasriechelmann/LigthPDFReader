using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using LightPdf.Core;
using LightPdf.Pdfium;

namespace LightPdf.App;

public partial class MainWindow : Window
{
    private readonly StartupOptions _options;
    private readonly PdfWorker _worker = new();   // one PDFium thread shared by all tabs
    private readonly RecentFilesStore _recent;
    private bool _benchReported;

    public MainWindow() : this(new StartupOptions([], false)) { } // designer

    public MainWindow(StartupOptions options)
    {
        _options = options;
        _recent = options.Automated ? new RecentFilesStore(null) : RecentFilesStore.CreateDefault();
        _recent.Load();

        InitializeComponent();
        if (options.StartMaximized) WindowState = WindowState.Maximized;
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel); // Ctrl shortcuts win over focused controls
        PageBox.KeyDown += OnPageBoxKeyDown;
        PageBox.LostFocus += (_, _) => UpdateToolbar();
        InitSearchBar();
        SyncAppearanceMenu();
        UpdateUi();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_options.FilePaths.Count > 0)
            await OpenFilesAsync(_options.FilePaths);
        else if (_options.Automated)
            Close();

        if (_options.PrintToPdfPath is { } output && ActiveTab is not null)
            await PrintToPdfAndExitAsync(output);
    }

    // ---------------------------------------------------------------- tabs

    private DocumentTab? ActiveTab => (Tabs.SelectedItem as TabItem)?.Content as DocumentTab;
    private IEnumerable<TabItem> TabItems => Tabs.Items.OfType<TabItem>();

    private TabItem? FindTab(string path)
    {
        string full = Path.GetFullPath(path);
        return TabItems.FirstOrDefault(t =>
            string.Equals(((DocumentTab)t.Content!).FilePath, full, StringComparison.OrdinalIgnoreCase));
    }

    private void AddTab(DocumentTab tab)
    {
        var title = new TextBlock
        {
            Text = tab.FileName,
            MaxWidth = 220,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var close = new Button { Content = "✕", Classes = { "tabClose" } };
        ToolTip.SetTip(close, "Close (Ctrl+W)");
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { title, close } };

        var item = new TabItem { Header = header, Content = tab };
        ToolTip.SetTip(item, tab.FilePath);
        close.Click += (_, _) => CloseTab(item);
        item.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Middle)
            {
                CloseTab(item);
                e.Handled = true;
            }
        };

        tab.Pages.ViewChanged += (s, _) => { if (ReferenceEquals(s, ActiveTab?.Pages)) UpdateToolbar(); };
        tab.Pages.PageRendered += OnPageRendered;
        tab.Pages.SearchChanged += (s, _) => { if (ReferenceEquals(s, ActiveTab?.Pages)) UpdateSearchStatus(); };

        Tabs.Items.Add(item);
        Tabs.SelectedItem = item;
        UpdateUi();
    }

    private void CloseTab(TabItem item)
    {
        if (item.Content is not DocumentTab tab) return;
        if (!_options.Automated)
        {
            _recent.Touch(tab.FilePath, tab.Pages.GetViewState());
            _recent.Save();
        }

        int index = Tabs.Items.IndexOf(item);
        Tabs.Items.Remove(item);
        tab.Close(_worker);
        if (Tabs.Items.Count > 0)
            Tabs.SelectedIndex = Math.Min(index, Tabs.Items.Count - 1);
        UpdateUi();
    }

    private void CycleTab(int delta)
    {
        int count = Tabs.Items.Count;
        if (count < 2) return;
        Tabs.SelectedIndex = ((Tabs.SelectedIndex + delta) % count + count) % count;
    }

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Ignore SelectionChanged bubbling up from controls inside a tab.
        if (!ReferenceEquals(e.Source, Tabs)) return;
        UpdateUi();
        SyncSearchBarToTab();
        if (!SearchBox.IsFocused) ActiveTab?.Scroller.Focus();
    }

    // ---------------------------------------------------------------- opening

    private async void OnOpenClick(object? sender, RoutedEventArgs e) => await ShowOpenDialogAsync();

    private async Task ShowOpenDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open PDF",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("PDF documents") { Patterns = ["*.pdf"] }],
        });
        await OpenFilesAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles() ?? [];
        await OpenFilesAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    /// <summary>Opens files as tabs; failures are collected and reported together at the end.</summary>
    private async Task OpenFilesAsync(IEnumerable<string> paths)
    {
        var errors = new List<string>();
        foreach (string path in paths.ToList())
            if (await TryOpenFileAsync(path) is { } error)
                errors.Add(error);

        if (errors.Count == 1)
            ShowError(errors[0]);
        else if (errors.Count > 1)
            ShowError($"Couldn't open {errors.Count} files. " + string.Join(" ", errors));
        else if (!IsPrinting && MessageBar.IsVisible && _messageAction is null)
            ClearMessage(); // e.g. an earlier "cannot open" error
    }

    private Task OpenFileAsync(string path) => OpenFilesAsync([path]);

    /// <summary>
    /// Opens a file in a new tab, or switches to it if it's already open.
    /// Returns an error message, or null on success (or when the user cancelled a password prompt).
    /// </summary>
    private async Task<string?> TryOpenFileAsync(string path)
    {
        if (FindTab(path) is { } existing)
        {
            Tabs.SelectedItem = existing;
            return null;
        }

        string? password = null;
        while (true)
        {
            try
            {
                var (document, sizes) = await _worker.Run(() =>
                {
                    var doc = PdfDocument.Open(path, password);
                    try { return (doc, doc.GetPageSizes()); }
                    catch { doc.Dispose(); throw; }
                });

                ViewState? lastView = _recent.Find(path)?.View;
                AddTab(new DocumentTab(_worker, document, sizes, lastView, App.Settings.Current.InvertPageColors));
                if (!_options.Automated)
                {
                    _recent.Touch(path);
                    _recent.Save();
                    RefreshRecent();
                }
                return null;
            }
            catch (PdfException ex) when (ex.Error == PdfiumError.Password && !_options.Automated)
            {
                password = await PasswordDialog.AskAsync(this, Path.GetFileName(path), retry: password is not null);
                if (password is null) return null; // cancelled
            }
            catch (PdfException ex)
            {
                if (ex.Error == PdfiumError.File && !_options.Automated)
                {
                    _recent.Remove(path); // gone or unreadable: don't keep offering it
                    _recent.Save();
                    RefreshRecent();
                }
                if (_options.Automated) { Console.Error.WriteLine(ex.Message); Close(); }
                return ex.Message;
            }
        }
    }

    // ---------------------------------------------------------------- recent files

    // Flyouts aren't in the window's name scope, so reach it through its button.
    private MenuFlyout RecentMenu => (MenuFlyout)RecentButton.Flyout!;

    /// <summary>Rebuilds the Recent menu and the empty-state list from the store.</summary>
    private void RefreshRecent()
    {
        UpdateRecentMenu();
        UpdateRecentList();
    }

    // Built ahead of time, not in Opening: an empty MenuFlyout never opens, so Opening never fires.
    private void UpdateRecentMenu()
    {
        RecentMenu.Items.Clear();
        if (_recent.Items.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "No recent files", IsEnabled = false });
            return;
        }
        foreach (var entry in _recent.Items)
        {
            var item = new MenuItem { Header = Path.GetFileName(entry.Path) };
            ToolTip.SetTip(item, entry.Path);
            string path = entry.Path;
            item.Click += async (_, _) =>
            {
                // Close first: opening refreshes (rebuilds) this menu, which would orphan the open flyout.
                RecentMenu.Hide();
                await OpenFileAsync(path);
            };
            RecentMenu.Items.Add(item);
        }
        RecentMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear list" };
        clear.Click += (_, _) =>
        {
            RecentMenu.Hide();
            _recent.Clear();
            _recent.Save();
            RefreshRecent();
        };
        RecentMenu.Items.Add(clear);
    }

    /// <summary>Recent files on the empty-state screen.</summary>
    private void UpdateRecentList()
    {
        RecentList.Children.Clear();
        foreach (var entry in _recent.Items.Take(8))
        {
            var button = new Button
            {
                Classes = { "recentLink" },
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = Path.GetFileName(entry.Path), Classes = { "name" } },
                        new TextBlock { Text = Path.GetDirectoryName(entry.Path), Classes = { "folder" } },
                    },
                },
            };
            string path = entry.Path;
            button.Click += async (_, _) => await OpenFileAsync(path);
            RecentList.Children.Add(button);
        }
        RecentHeading.IsVisible = RecentList.Children.Count > 0;
    }

    // ---------------------------------------------------------------- toolbar

    private void UpdateUi()
    {
        bool hasTabs = Tabs.Items.Count > 0;
        Tabs.IsVisible = hasTabs;
        EmptyState.IsVisible = !hasTabs;
        RefreshRecent(); // cheap (≤ 20 entries); keeps the menu current after every open/close
        Title = ActiveTab is { } tab ? $"{tab.FileName} - LightPdf" : "LightPdf";
        UpdateToolbar();
    }

    private void UpdateToolbar()
    {
        var pages = ActiveTab?.Pages;
        bool hasDoc = pages is { PageCount: > 0 };
        PageBox.IsEnabled = hasDoc;
        if (!PageBox.IsFocused)
            PageBox.Text = hasDoc ? (pages!.CurrentPage + 1).ToString() : "";
        PageCountText.Text = $"/ {pages?.PageCount ?? 0}";
        PrevButton.IsEnabled = hasDoc && pages!.CurrentPage > 0;
        NextButton.IsEnabled = hasDoc && pages!.CurrentPage < pages.PageCount - 1;
        ZoomOutButton.IsEnabled = ZoomInButton.IsEnabled = FitWidthButton.IsEnabled = FitPageButton.IsEnabled = hasDoc;
        ZoomText.Text = hasDoc ? $"{pages!.Zoom * 100:0}%" : "";
        FitWidthButton.IsChecked = hasDoc && pages!.ZoomMode == ZoomMode.FitWidth;
        FitPageButton.IsChecked = hasDoc && pages!.ZoomMode == ZoomMode.FitPage;
        PrintButton.IsEnabled = hasDoc && !IsPrinting;
    }

    private void OnPrevClick(object? sender, RoutedEventArgs e) => ActiveTab?.Pages.GoToPage(ActiveTab.Pages.CurrentPage - 1);
    private void OnNextClick(object? sender, RoutedEventArgs e) => ActiveTab?.Pages.GoToPage(ActiveTab.Pages.CurrentPage + 1);
    private void OnZoomInClick(object? sender, RoutedEventArgs e) => ActiveTab?.Pages.ZoomIn();
    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => ActiveTab?.Pages.ZoomOut();
    private void OnFitWidthClick(object? sender, RoutedEventArgs e) => ActiveTab?.Pages.SetZoomMode(ZoomMode.FitWidth);
    private void OnFitPageClick(object? sender, RoutedEventArgs e) => ActiveTab?.Pages.SetZoomMode(ZoomMode.FitPage);

    private void OnPageBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (int.TryParse(PageBox.Text, out int page))
                ActiveTab?.Pages.GoToPage(page - 1);
            ActiveTab?.Scroller.Focus(); // leaving the box refreshes it with the actual page
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ActiveTab?.Scroller.Focus();
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- keyboard

    /// <summary>Ctrl shortcuts, handled on the way down so focused controls (tabs, text boxes) can't swallow them.</summary>
    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var tab = ActiveTab;
        bool inTextBox = e.Source is TextBox;

        if (e.Key == Key.F3)
        {
            NextMatch(shift ? -1 : 1);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && !inTextBox && tab is { Pages.HasSelection: true })
        {
            tab.Pages.ClearSelection();
            e.Handled = true;
            return;
        }
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        e.Handled = true;

        switch (e.Key)
        {
            case Key.F when tab is not null: ShowSearchBar(); break;
            case Key.P when tab is not null: await PrintActiveTabAsync(); break;
            case Key.I: ToggleInvertColors(); break;
            // In a text box, Ctrl+C / Ctrl+A keep their usual meaning.
            case Key.C when !inTextBox: await CopySelectionAsync(); break;
            case Key.A when !inTextBox: tab?.Pages.SelectAllOnPage(); break;
            case Key.O or Key.T: await ShowOpenDialogAsync(); break;
            case Key.W or Key.F4: if (Tabs.SelectedItem is TabItem item) CloseTab(item); break;
            case Key.Tab: CycleTab(shift ? -1 : 1); break;
            case Key.PageDown: CycleTab(1); break;
            case Key.PageUp: CycleTab(-1); break;
            case Key.OemPlus or Key.Add: tab?.Pages.ZoomIn(); break;
            case Key.OemMinus or Key.Subtract: tab?.Pages.ZoomOut(); break;
            case Key.D0 or Key.NumPad0: tab?.Pages.SetZoomMode(ZoomMode.FitPage); break;
            case Key.D1 or Key.NumPad1: tab?.Pages.SetZoom(1.0); break;
            case Key.D2 or Key.NumPad2: tab?.Pages.SetZoomMode(ZoomMode.FitWidth); break;
            case Key.G when tab is not null: PageBox.Focus(); PageBox.SelectAll(); break;
            case Key.Home: tab?.Pages.GoToPage(0); break;
            case Key.End: tab?.Pages.GoToPage(int.MaxValue); break;
            default: e.Handled = false; break;
        }
    }

    /// <summary>Plain navigation keys, unless a text box has focus.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Source is TextBox || ActiveTab is not { } tab) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        e.Handled = true;

        var scroller = tab.Scroller;
        switch (e.Key)
        {
            case Key.Home: scroller.ScrollToHome(); break;
            case Key.End: scroller.ScrollToEnd(); break;
            case Key.PageDown: scroller.PageDown(); break;
            case Key.PageUp: scroller.PageUp(); break;
            case Key.Space when e.KeyModifiers.HasFlag(KeyModifiers.Shift): scroller.PageUp(); break;
            case Key.Space: scroller.PageDown(); break;
            case Key.Down: scroller.LineDown(); break;
            case Key.Up: scroller.LineUp(); break;
            case Key.Left: tab.Pages.GoToPage(tab.Pages.CurrentPage - 1); break;
            case Key.Right: tab.Pages.GoToPage(tab.Pages.CurrentPage + 1); break;
            default: e.Handled = false; break;
        }
    }

    // ---------------------------------------------------------------- bench / snapshot

    private void OnPageRendered(object? sender, PageRenderedEventArgs e)
    {
        if (e.PageIndex != 0 || _benchReported) return;
        if (_options.SnapshotPath is { } snapshot)
            e.Bitmap.Save(snapshot, new PngBitmapEncoderOptions());
        if (!_options.Bench) return;

        _benchReported = true;
        RequestAnimationFrame(async _ => // after the frame showing the page is presented
        {
            double ms = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            Console.WriteLine($"startup_to_first_page_ms={ms:F0}");
            if (_options.BenchScrollPages > 0)
                await ScrollBenchAsync(_options.BenchScrollPages);
            Close();
        });
    }

    /// <summary>
    /// --bench-scroll N: pages down N times at key-repeat pace, lets renders settle, then reports memory and
    /// CPU. A repeatable stand-in for "read through a long document".
    /// </summary>
    private async Task ScrollBenchAsync(int pages)
    {
        if (ActiveTab is not { } tab) return;
        using var self = Process.GetCurrentProcess();
        TimeSpan cpuBefore = self.TotalProcessorTime;
        var wall = Stopwatch.StartNew();

        // Frame pacing: a chain of animation-frame callbacks records when each frame actually happens.
        var frameTimes = new List<double>();
        bool measuring = true;
        void OnFrame(TimeSpan _)
        {
            frameTimes.Add(wall.Elapsed.TotalMilliseconds);
            if (measuring) RequestAnimationFrame(OnFrame);
        }
        RequestAnimationFrame(OnFrame);

        for (int i = 0; i < pages; i++)
        {
            tab.Scroller.PageDown();
            await Task.Delay(50);
        }
        measuring = false;
        double scrollMs = wall.Elapsed.TotalMilliseconds;
        var gaps = frameTimes.Zip(frameTimes.Skip(1), (a, b) => b - a).Order().ToList();
        double P(double q) => gaps.Count == 0 ? 0 : gaps[Math.Min(gaps.Count - 1, (int)(q * gaps.Count))];
        Console.WriteLine($"frames={frameTimes.Count} fps={frameTimes.Count / (scrollMs / 1000):F0} " +
                          $"frame_p50_ms={P(0.5):F1} frame_p95_ms={P(0.95):F1} frame_max_ms={(gaps.Count > 0 ? gaps[^1] : 0):F0}");
        await Task.Delay(1000); // last renders
        GC.Collect();
        GC.WaitForPendingFinalizers();
        self.Refresh();
        static double Mb(long bytes) => bytes / 1048576.0;
        Console.WriteLine(
            $"scroll_pages={pages} page_reached={tab.Pages.CurrentPage + 1} " +
            $"cpu_ms={(self.TotalProcessorTime - cpuBefore).TotalMilliseconds:F0} wall_ms={scrollMs:F0} " +
            $"ws_mb={Mb(self.WorkingSet64):F0} private_mb={Mb(self.PrivateMemorySize64):F0} " +
            $"managed_mb={Mb(GC.GetTotalMemory(false)):F1} cache_mb={Mb(tab.Pages.CacheBytes):F0}");
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_options.Automated)
        {
            // Save every open tab's position; the active tab ends up on top of the recent list.
            foreach (var item in TabItems.Reverse().OrderBy(t => ReferenceEquals(t, Tabs.SelectedItem)))
                if (item.Content is DocumentTab tab)
                    _recent.Touch(tab.FilePath, tab.Pages.GetViewState());
            _recent.Save();
        }
        foreach (var item in TabItems.ToList())
            if (item.Content is DocumentTab tab)
                tab.Close(_worker);
        _worker.Dispose();
        base.OnClosed(e);
    }
}
