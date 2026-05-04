using System.Windows;
using VCSyncBackupApp.Models;

namespace VCSyncBackupApp;

public partial class AddServerDialog : Window
{
    public AddServerDialog(string defaultName)
    {
        InitializeComponent();

        NameTextBox.Text = defaultName;
        RemoteDataPathTextBox.Text = "/root/shadowbox/persisted-state/prometheus/data";
        RemoteConfigPathTextBox.Text = "/root/shadowbox/persisted-state/shadowbox_config.json";
        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    public ServerConfig? ServerResult { get; private set; }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var ip = IpTextBox.Text.Trim();
        var remoteDataPath = RemoteDataPathTextBox.Text.Trim();
        var remoteConfigPath = RemoteConfigPathTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(ip) ||
            string.IsNullOrWhiteSpace(remoteDataPath) ||
            string.IsNullOrWhiteSpace(remoteConfigPath))
        {
            ThemedDialog.Show(this, "Validation", "Please fill in Name, IP Address, Remote Data Path, and Remote Config Path.");
            return;
        }

        ServerResult = new ServerConfig
        {
            Name = name,
            IpAddress = ip,
            RemoteDataPath = remoteDataPath,
            RemoteConfigPath = remoteConfigPath,
            Status = "Idle",
            ProgressPercent = 0
        };

        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
