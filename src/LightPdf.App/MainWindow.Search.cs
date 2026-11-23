using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace LightPdf.App;

/// <summary>Find bar and clipboard for the main window.</summary>
public partial class MainWindow
{
    private static readonly IBrush NoResultsBrush = Brushes.IndianRed;

    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _syncingSearchBox; // true while we set SearchBox.Text ourselves

    private void InitSearchBar()
    {
        _searchDebounce.Tick += (_, _) => RunSearch();
        SearchBox.TextChanged += (_, _) =>
        {
            if (_syncingSearchBox) return;
            _searchDebounce.Stop();
            _searchDebounce.Start(); // search as you type, once typing pauses
        };
        SearchBox.KeyDown += OnSearchBoxKeyDown;
    }

    private void ShowSearchBar()
    {
        SearchBar.IsVisible = true;
        SearchBox.Focus();
        SearchBox.SelectAll();
        UpdateSearchStatus();
    }

    private void CloseSearchBar()
    {
        _searchDebounce.Stop();
        SearchBar.IsVisible = false;
        ActiveTab?.Pages.ClearSearch();
        ActiveTab?.Scroller.Focus();
    }

    private void RunSearch()
    {
        _searchDebounce.Stop();
        ActiveTab?.Pages.StartSearch(SearchBox.Text ?? "", MatchCaseButton.IsChecked == true);
    }

    private void NextMatch(int direction)
    {
        if (ActiveTab?.Pages is not { } pages) return;
        // Typed something new (or changed match case)? Search first; it jumps to the first hit itself.
        if (SearchBar.IsVisible && (_searchDebounce.IsEnabled
            || (SearchBox.Text ?? "").Trim() != pages.SearchQuery
            || (MatchCaseButton.IsChecked == true) != pages.SearchMatchCase))
        {
            RunSearch();
            return;
        }
        pages.GoToMatch(direction);
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                NextMatch(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                e.Handled = true;
                break;
            case Key.Escape:
                CloseSearchBar();
                e.Handled = true;
                break;
        }
    }

    private void OnSearchPrevClick(object? sender, RoutedEventArgs e) => NextMatch(-1);
    private void OnSearchNextClick(object? sender, RoutedEventArgs e) => NextMatch(1);
    private void OnMatchCaseClick(object? sender, RoutedEventArgs e) => RunSearch();
    private void OnSearchCloseClick(object? sender, RoutedEventArgs e) => CloseSearchBar();

    /// <summary>Each tab keeps its own search; the bar shows the active tab's.</summary>
    private void SyncSearchBarToTab()
    {
        if (!SearchBar.IsVisible) return;
        _searchDebounce.Stop();
        _syncingSearchBox = true;
        SearchBox.Text = ActiveTab?.Pages.SearchQuery ?? "";
        _syncingSearchBox = false;
        MatchCaseButton.IsChecked = ActiveTab?.Pages.SearchMatchCase ?? false;
        UpdateSearchStatus();
    }

    private void UpdateSearchStatus()
    {
        var pages = ActiveTab?.Pages;
        SearchStatus.ClearValue(TextBlock.ForegroundProperty);
        if (pages is null || pages.SearchQuery.Length == 0)
        {
            SearchStatus.Text = "";
        }
        else if (pages.MatchCount == 0)
        {
            SearchStatus.Text = pages.IsSearching ? "Searching…" : "No results";
            if (!pages.IsSearching) SearchStatus.Foreground = NoResultsBrush;
        }
        else
        {
            // "+" while later pages are still being searched.
            SearchStatus.Text = $"{pages.CurrentMatchNumber} of {pages.MatchCount}{(pages.IsSearching ? "+" : "")}";
        }
    }

    private async Task CopySelectionAsync()
    {
        if (ActiveTab?.Pages is not { } pages || Clipboard is not { } clipboard) return;
        string? text = await pages.GetSelectedTextAsync();
        if (string.IsNullOrEmpty(text)) return;
        await clipboard.SetTextAsync(text);
        ShowStatus($"Copied {text.Length:N0} characters");
    }
}
