using System.Windows;
using System.Windows.Controls;
using VCSyncBackupApp.ViewModels;

namespace VCSyncBackupApp;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _pauseAutoScroll;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.SetOwnerWindow(this);
        await _viewModel.InitializeAsync();
        PassphraseBox.Password = _viewModel.Passphrase;
    }

    private void PassphraseBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.Passphrase = PassphraseBox.Password;
    }

    private void AddServerButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddServerDialog($"Server{_viewModel.Servers.Count + 1}")
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.ServerResult is not null)
        {
            _viewModel.AddServer(dialog.ServerResult);
        }
    }

    public void ShowThemedDialog(string title, string message)
    {
        ThemedDialog.Show(this, title, message);
    }

    private void AboutButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowThemedDialog(
            "About VC Sync",
            "Owner: VChannel\nDeveloper: @sir_yan\nApp Name: VC Sync\nVersion: 1.0.0");
    }

    private void LogTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_pauseAutoScroll)
        {
            return;
        }

        LogTextBox.ScrollToEnd();
    }

    private void PauseAutoScrollCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        _pauseAutoScroll = true;
    }

    private void PauseAutoScrollCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        _pauseAutoScroll = false;
        LogTextBox.ScrollToEnd();
    }
}