using System.Windows;

namespace VCSyncBackupApp;

public partial class ThemedDialog : Window
{
    public ThemedDialog(string title, string message)
        : this(title, message, "OK")
    {
    }

    private ThemedDialog(string title, string message, string primaryButtonText, string secondaryButtonText = "")
    {
        DialogTitle = title;
        DialogMessage = message;
        PrimaryButtonText = string.IsNullOrWhiteSpace(primaryButtonText) ? "OK" : primaryButtonText;
        SecondaryButtonText = secondaryButtonText;
        InitializeComponent();

        if (UseMultilineMessage)
        {
            Width = 760;
            Height = 620;
            MinWidth = 620;
            MinHeight = 420;
            ResizeMode = ResizeMode.CanResize;
        }
    }

    public string DialogTitle { get; }

    public string DialogMessage { get; }

    public string PrimaryButtonText { get; }

    public string SecondaryButtonText { get; }

    public bool ShowSecondaryButton => !string.IsNullOrWhiteSpace(SecondaryButtonText);

    public bool UseMultilineMessage => (DialogMessage?.Length ?? 0) > 100;

    public bool ShowCopyButton =>
        !ShowSecondaryButton
        && (ContainsFailureOrError(DialogTitle) || ContainsFailureOrError(DialogMessage));

    public static void Show(Window owner, string title, string message)
    {
        var dialog = new ThemedDialog(title, message)
        {
            Owner = owner
        };

        dialog.ShowDialog();
    }

    public static void Show(string title, string message)
    {
        var dialog = new ThemedDialog(title, message);
        dialog.ShowDialog();
    }

    public static bool ShowConfirmation(Window owner, string title, string message)
    {
        var dialog = new ThemedDialog(title, message, "Yes", "No")
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true;
    }

    public static bool ShowConfirmation(string title, string message)
    {
        var dialog = new ThemedDialog(title, message, "Yes", "No");
        return dialog.ShowDialog() == true;
    }

    private void PrimaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ShowSecondaryButton)
        {
            Close();
            return;
        }

        DialogResult = true;
    }

    private void SecondaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void CopyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DialogMessage))
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(DialogMessage);
        }
        catch
        {
            // Ignore clipboard access errors to keep dialog behavior non-blocking.
        }
    }

    private static bool ContainsFailureOrError(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("fail", StringComparison.OrdinalIgnoreCase)
            || value.Contains("error", StringComparison.OrdinalIgnoreCase);
    }
}