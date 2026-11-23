using Avalonia.Interactivity;
using Avalonia.Threading;

namespace LightPdf.App;

/// <summary>
/// The message bar under the toolbar: short notices (auto-hide), errors (stay until dismissed),
/// and progress with an optional action button (e.g. "Cancel printing").
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private Action? _messageAction;
    private bool _messageTimerHooked;

    /// <summary>Last message shown (used by automated runs to report failures).</summary>
    private string LastMessage { get; set; } = "";

    /// <summary>A short notice that disappears by itself.</summary>
    private void ShowStatus(string message) => ShowMessage(message, autoHide: true);

    /// <summary>A problem the user should see; stays until dismissed or replaced.</summary>
    private void ShowError(string message) => ShowMessage(message, autoHide: false);

    /// <summary>Information worth reading at leisure (e.g. About); stays until dismissed.</summary>
    private void ShowInfo(string message) => ShowMessage(message, autoHide: false);

    /// <summary>Ongoing work, optionally with a button (e.g. "Cancel printing"); stays until replaced or cleared.</summary>
    private void ShowProgress(string message, string? actionLabel = null, Action? action = null)
        => ShowMessage(message, autoHide: false, actionLabel, action);

    private void ShowMessage(string message, bool autoHide, string? actionLabel = null, Action? action = null)
    {
        if (!_messageTimerHooked)
        {
            _messageTimer.Tick += (_, _) => ClearMessage();
            _messageTimerHooked = true;
        }
        LastMessage = message;
        MessageText.Text = message;
        _messageAction = action;
        MessageActionButton.Content = actionLabel;
        MessageActionButton.IsVisible = actionLabel is not null && action is not null;
        MessageBar.IsVisible = true;
        _messageTimer.Stop();
        if (autoHide) _messageTimer.Start();
    }

    /// <summary>An unexpected error that was caught globally (already written to the error log).</summary>
    internal void ReportUnexpectedError(Exception exception)
        => ShowError($"Something went wrong: {exception.Message} (details in {ErrorLog.FilePath})");

    private void ClearMessage()
    {
        _messageTimer.Stop();
        _messageAction = null;
        MessageBar.IsVisible = false;
    }

    private void OnMessageDismissClick(object? sender, RoutedEventArgs e) => ClearMessage();

    private void OnMessageActionClick(object? sender, RoutedEventArgs e) => _messageAction?.Invoke();
}
