using System.Windows;

namespace VCSyncBackupApp;

public partial class ThemedDialog : Window
{
    public ThemedDialog(string title, string message)
    {
        InitializeComponent();
        DialogTitle = title;
        DialogMessage = message;
    }

    public string DialogTitle { get; }

    public string DialogMessage { get; }

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

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}