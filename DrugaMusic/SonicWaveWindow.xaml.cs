using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using DrugaMusic.Services;

namespace DrugaMusic;

public partial class SonicWaveWindow : Window
{
    private bool initialized;
    private readonly FirebaseAuthService accountAuth = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SONORA"));

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

            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SONORA", "WebView2");
            Directory.CreateDirectory(userData);
            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.Settings.IsZoomControlEnabled = false;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;

            // A localhost origin keeps Firebase's supported local-development auth flow
            // while the actual files remain packaged with the desktop application.
            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "localhost", root, CoreWebView2HostResourceAccessKind.Allow);

            Browser.CoreWebView2.WebMessageReceived += async (_, args) =>
            {
                try
                {
                    using var message = JsonDocument.Parse(args.WebMessageAsJson);
                    var type = message.RootElement.TryGetProperty("type", out var typeValue)
                        ? typeValue.GetString()
                        : null;
                    if (type == "google-sign-out")
                    {
                        accountAuth.SignOut();
                        return;
                    }
                    if (type != "google-sign-in") return;

                    await accountAuth.SignInWithGoogleAsync();
                    Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
                    {
                        type = "google-sign-in-result",
                        accessToken = accountAuth.GoogleAccessToken
                    }));
                }
                catch (Exception ex)
                {
                    Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
                    {
                        type = "google-sign-in-error",
                        message = ex.Message
                    }));
                }
            };

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