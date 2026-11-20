using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace DrugaMusic;

public partial class SonicWaveWindow : Window
{
    private bool initialized;

    public SonicWaveWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeBrowserAsync();
    }

    private async Task InitializeBrowserAsync()
    {
        if (initialized) return;

        try
        {
            var root = AppContext.BaseDirectory;
            var page = Path.Combine(root, "SonicWave", "index.html");
            if (!File.Exists(page))
                throw new FileNotFoundException("Не найден встроенный интерфейс Sonic Wave.", page);

            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.Settings.IsZoomControlEnabled = false;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;

            // A localhost origin keeps Firebase's supported local-development auth flow
            // while the actual files remain packaged with the desktop application.
            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "localhost", root, CoreWebView2HostResourceAccessKind.Allow);

            // Keep all navigation inside the single application window.
            Browser.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var target))
                    Browser.CoreWebView2.Navigate(target.ToString());
            };

            Browser.CoreWebView2.ProcessFailed += (_, args) =>
            {
                Dispatcher.Invoke(() => ShowError(
                    $"Встроенный браузер завершил работу ({args.ProcessFailedKind}). Перезапусти приложение."));
            };

            Browser.Source = new Uri("https://localhost/SonicWave/index.html");
            initialized = true;
            ErrorPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowError("Не удалось запустить интерфейс SONORA. " + ex.Message +
                Environment.NewLine + "Проверь, что установлен Microsoft Edge WebView2 Runtime.");
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        await InitializeBrowserAsync();
    }
}