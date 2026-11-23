using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LightPdf.App;

/// <summary>Modal prompt for a document's password. Returns null when cancelled.</summary>
internal sealed class PasswordDialog : Window
{
    private readonly TextBox _password;

    private PasswordDialog(string fileName, bool retry)
    {
        Title = "Password required";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _password = new TextBox { PasswordChar = '•', PlaceholderText = "Password" };
        var open = new Button { Content = "Open", IsDefault = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        open.Click += (_, _) => Close(_password.Text ?? "");
        cancel.Click += (_, _) => Close(null);

        var message = new TextBlock
        {
            Text = $"“{fileName}” is password protected. Enter the password to open it.",
            TextWrapping = TextWrapping.Wrap,
        };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(message);
        if (retry)
            panel.Children.Add(new TextBlock { Text = "That password is incorrect. Try again.", Foreground = Brushes.IndianRed });
        panel.Children.Add(_password);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(open);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;

        Opened += (_, _) => _password.Focus();
    }

    public static Task<string?> AskAsync(Window owner, string fileName, bool retry)
        => new PasswordDialog(fileName, retry).ShowDialog<string?>(owner);
}
