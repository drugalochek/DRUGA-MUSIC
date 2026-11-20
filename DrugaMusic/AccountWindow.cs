using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DrugaMusic.Services;

namespace DrugaMusic;

public sealed class AccountWindow : Window
{
    private readonly FirebaseAuthService auth;
    private readonly TextBox emailBox;
    private readonly PasswordBox passwordBox;
    private readonly TextBlock statusText;
    private readonly Button signInButton;
    private readonly Button registerButton;
    private readonly Button signOutButton;

    public AccountWindow(FirebaseAuthService auth)
    {
        this.auth = auth;
        Title = "Аккаунт SONORA";
        Width = 410;
        Height = 390;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(8, 11, 18));
        Foreground = Brushes.White;

        var root = new StackPanel { Margin = new Thickness(26) };
        root.Children.Add(new TextBlock { Text = "Аккаунт SONORA", FontSize = 23, FontWeight = FontWeights.Bold });
        root.Children.Add(new TextBlock { Text = "Войди, чтобы подключить облачную синхронизацию.", Foreground = Brush("#93A1B5"), FontSize = 12, Margin = new Thickness(0, 7, 0, 20), TextWrapping = TextWrapping.Wrap });

        statusText = new TextBlock { Foreground = Brush("#8CB8FF"), FontSize = 12, Margin = new Thickness(0, 0, 0, 14), TextWrapping = TextWrapping.Wrap };
        root.Children.Add(statusText);
        emailBox = new TextBox { Height = 38, Padding = new Thickness(10), Background = Brush("#111827"), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 13 };
        emailBox.ToolTip = "Электронная почта";
        root.Children.Add(emailBox);
        passwordBox = new PasswordBox { Height = 38, Padding = new Thickness(10), Background = Brush("#111827"), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 13, Margin = new Thickness(0, 10, 0, 0) };
        passwordBox.ToolTip = "Пароль (минимум 6 символов)";
        root.Children.Add(passwordBox);

        signInButton = MakeButton("Войти", "#287BFF");
        signInButton.Margin = new Thickness(0, 18, 0, 0);
        signInButton.Click += async (_, _) => await RunAuthAsync(false);
        root.Children.Add(signInButton);
        registerButton = MakeButton("Создать аккаунт", "#1A2638");
        registerButton.Margin = new Thickness(0, 8, 0, 0);
        registerButton.Click += async (_, _) => await RunAuthAsync(true);
        root.Children.Add(registerButton);
        signOutButton = MakeButton("Выйти из аккаунта", "#1A2638");
        signOutButton.Margin = new Thickness(0, 8, 0, 0);
        signOutButton.Click += (_, _) => { auth.SignOut(); UpdateState(); };
        root.Children.Add(signOutButton);
        Content = root;
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

    private void UpdateState()
    {
        var signedIn = auth.IsSignedIn;
        statusText.Text = signedIn ? $"Выполнен вход: {auth.Email}\nСессия будет сохранена на этом компьютере." : "Не выполнен вход. Локальная музыка продолжит работать без аккаунта.";
        emailBox.Visibility = passwordBox.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        signInButton.Visibility = registerButton.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        signOutButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        Height = signedIn ? 260 : 390;
    }

    private Button MakeButton(string text, string color) => new()
    {
        Content = text, Height = 40, Background = Brush(color), Foreground = Brushes.White,
        BorderThickness = new Thickness(0), FontSize = 13, Cursor = System.Windows.Input.Cursors.Hand
    };

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
