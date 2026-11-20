using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DrugaMusic.Services;

namespace DrugaMusic;

public sealed class AccountWindow : Window
{
    private readonly FirebaseAuthService auth;
    private readonly Func<Task<string>> syncAction;
    private readonly TextBox emailBox;
    private readonly PasswordBox passwordBox;
    private readonly TextBlock statusText;
    private readonly TextBlock syncStatusText;
    private readonly Button signInButton;
    private readonly Button registerButton;
    private readonly Button signOutButton;
    private readonly Button syncButton;

    public AccountWindow(FirebaseAuthService auth, Func<Task<string>> syncAction)
    {
        this.auth = auth;
        this.syncAction = syncAction;
        Title = "Аккаунт и подключения — SONORA";
        Width = 470;
        Height = 570;
        MinHeight = 480;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#080B12");
        Foreground = Brushes.White;

        var root = new StackPanel { Margin = new Thickness(26) };
        root.Children.Add(new TextBlock { Text = "Аккаунт и подключения", FontSize = 23, FontWeight = FontWeights.Bold });
        root.Children.Add(new TextBlock { Text = "Управление профилем SONORA и облачными данными.", Foreground = Brush("#93A1B5"), FontSize = 12, Margin = new Thickness(0, 7, 0, 18), TextWrapping = TextWrapping.Wrap });

        statusText = new TextBlock { Foreground = Brush("#8CB8FF"), FontSize = 12, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
        root.Children.Add(statusText);

        emailBox = new TextBox { Height = 38, Padding = new Thickness(10), Background = Brush("#111827"), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 13 };
        emailBox.ToolTip = "Электронная почта";
        root.Children.Add(emailBox);
        passwordBox = new PasswordBox { Height = 38, Padding = new Thickness(10), Background = Brush("#111827"), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 13, Margin = new Thickness(0, 10, 0, 0) };
        passwordBox.ToolTip = "Пароль (минимум 6 символов)";
        root.Children.Add(passwordBox);

        signInButton = MakeButton("Войти", "#287BFF");
        signInButton.Margin = new Thickness(0, 16, 0, 0);
        signInButton.Click += async (_, _) => await RunAuthAsync(false);
        root.Children.Add(signInButton);
        registerButton = MakeButton("Создать аккаунт", "#1A2638");
        registerButton.Margin = new Thickness(0, 8, 0, 0);
        registerButton.Click += async (_, _) => await RunAuthAsync(true);
        root.Children.Add(registerButton);

        var divider = new Border { Height = 1, Background = Brush("#202B3B"), Margin = new Thickness(0, 22, 0, 18) };
        root.Children.Add(divider);
        root.Children.Add(new TextBlock { Text = "ОБЛАЧНОЕ ПОДКЛЮЧЕНИЕ", Foreground = Brush("#63738A"), FontSize = 10, FontWeight = FontWeights.Bold });
        root.Children.Add(new TextBlock { Text = "Firebase Authentication · вход и профиль", Foreground = Brush("#D7E1F0"), FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
        root.Children.Add(new TextBlock { Text = "Firebase Realtime Database · избранное, плейлисты и настройки", Foreground = Brush("#D7E1F0"), FontSize = 12, Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap });
        syncStatusText = new TextBlock { Text = "Синхронизация ещё не запускалась на этом устройстве.", Foreground = Brush("#93A1B5"), FontSize = 11, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
        root.Children.Add(syncStatusText);

        syncButton = MakeButton("Синхронизировать сейчас", "#287BFF");
        syncButton.Margin = new Thickness(0, 14, 0, 0);
        syncButton.Click += async (_, _) => await RunSyncAsync();
        root.Children.Add(syncButton);

        signOutButton = MakeButton("Выйти из аккаунта", "#1A2638");
        signOutButton.Margin = new Thickness(0, 8, 0, 0);
        signOutButton.Click += (_, _) => { auth.SignOut(); UpdateState(); };
        root.Children.Add(signOutButton);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        UpdateState();
    }

    private async Task RunAuthAsync(bool register)
    {
        if (string.IsNullOrWhiteSpace(emailBox.Text) || string.IsNullOrWhiteSpace(passwordBox.Password))
        {
            statusText.Text = "Введи почту и пароль.";
            return;
        }

        signInButton.IsEnabled = registerButton.IsEnabled = false;
        statusText.Text = register ? "Создаём аккаунт…" : "Выполняем вход…";
        try
        {
            if (register) await auth.RegisterAsync(emailBox.Text.Trim(), passwordBox.Password);
            else await auth.SignInAsync(emailBox.Text.Trim(), passwordBox.Password);
            UpdateState();
        }
        catch (Exception ex) { statusText.Text = ex.Message; }
        finally { signInButton.IsEnabled = registerButton.IsEnabled = true; }
    }

    private async Task RunSyncAsync()
    {
        if (!auth.IsSignedIn) { syncStatusText.Text = "Сначала войди в аккаунт."; return; }
        syncButton.IsEnabled = false;
        syncStatusText.Text = "Подключаемся к Realtime Database…";
        try { syncStatusText.Text = await syncAction(); }
        catch (Exception ex) { syncStatusText.Text = "Синхронизация не выполнена: " + ex.Message; }
        finally { syncButton.IsEnabled = auth.IsSignedIn; }
    }

    private void UpdateState()
    {
        var signedIn = auth.IsSignedIn;
        statusText.Text = signedIn
            ? $"Выполнен вход: {auth.Email}\nUID: {auth.UserId}"
            : "Не выполнен вход. Локальная музыка продолжит работать без аккаунта.";
        emailBox.Visibility = passwordBox.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        signInButton.Visibility = registerButton.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        syncButton.IsEnabled = signedIn;
        signOutButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        syncStatusText.Text = signedIn
            ? "Аккаунт подключён. Нажми кнопку, чтобы объединить локальные данные с облаком."
            : "Для облачного подключения сначала войди в аккаунт.";
    }

    private Button MakeButton(string text, string color) => new()
    {
        Content = text, Height = 40, Background = Brush(color), Foreground = Brushes.White,
        BorderThickness = new Thickness(0), FontSize = 13, Cursor = System.Windows.Input.Cursors.Hand
    };

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
