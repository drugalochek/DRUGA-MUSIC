using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrugaMusic.Services;
using Microsoft.Win32;

namespace DrugaMusic;

public sealed class AccountWindow : Window
{
    private readonly FirebaseAuthService auth;
    private readonly FirebaseProfileService profiles = new();
    private readonly Func<Task<string>> syncAction;
    private readonly TextBox emailBox;
    private readonly PasswordBox passwordBox;
    private readonly TextBlock statusText;
    private readonly TextBlock syncStatusText;
    private readonly TextBlock profileStatusText;
    private readonly TextBox nicknameBox;
    private readonly Image avatarImage;
    private readonly Button signInButton;
    private readonly Button registerButton;
    private readonly Button googleButton;
    private readonly Button setPasswordButton;
    private readonly Button signOutButton;
    private readonly Button syncButton;
    private readonly Button saveProfileButton;
    private UserProfile currentProfile = new();
    private string? selectedAvatarBase64;

    public AccountWindow(FirebaseAuthService auth, Func<Task<string>> syncAction)
    {
        this.auth = auth;
        this.syncAction = syncAction;
        Title = "Аккаунт и подключения — SONORA";
        Width = 470;
        Height = 650;
        MinHeight = 520;
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
        googleButton = MakeButton("Продолжить с Google", "#1A2638");
        googleButton.Margin = new Thickness(0, 8, 0, 0);
        googleButton.Click += async (_, _) => await RunGoogleAuthAsync();
        root.Children.Add(googleButton);

        var profilePanel = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
        profilePanel.Children.Add(new TextBlock { Text = "ПРОФИЛЬ SONORA", Foreground = Brush("#63738A"), FontSize = 10, FontWeight = FontWeights.Bold });
        var avatarRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 10) };
        avatarImage = new Image { Width = 76, Height = 76, Stretch = System.Windows.Media.Stretch.UniformToFill };
        avatarRow.Children.Add(new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(38), ClipToBounds = true, Background = Brush("#111827"), Child = avatarImage });
        var avatarActions = new StackPanel { Margin = new Thickness(14, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var chooseAvatar = MakeButton("Выбрать аватарку", "#1A2638");
        chooseAvatar.Click += (_, _) => ChooseAvatar();
        avatarActions.Children.Add(chooseAvatar);
        var resetAvatar = MakeButton("Использовать Google-фото", "#1A2638");
        resetAvatar.Margin = new Thickness(0, 7, 0, 0);
        resetAvatar.Click += (_, _) => { selectedAvatarBase64 = null; currentProfile.AvatarData = ""; currentProfile.AvatarType = "google"; ShowAvatar(); profileStatusText.Text = "Будет использоваться фото Google, если Google-аккаунт подключён."; };
        avatarActions.Children.Add(resetAvatar);
        avatarRow.Children.Add(avatarActions);
        profilePanel.Children.Add(avatarRow);
        nicknameBox = new TextBox { Height = 38, Padding = new Thickness(10), Background = Brush("#111827"), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 13, ToolTip = "Никнейм" };
        profilePanel.Children.Add(nicknameBox);
        saveProfileButton = MakeButton("Сохранить профиль", "#287BFF");
        saveProfileButton.Margin = new Thickness(0, 9, 0, 0);
        saveProfileButton.Click += async (_, _) => await SaveProfileAsync();
        profilePanel.Children.Add(saveProfileButton);
        profileStatusText = new TextBlock { Foreground = Brush("#93A1B5"), FontSize = 11, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        profilePanel.Children.Add(profileStatusText);
        setPasswordButton = MakeButton("Задать пароль SONORA", "#1A2638");
        setPasswordButton.Margin = new Thickness(0, 8, 0, 0);
        setPasswordButton.Click += async (_, _) => await PromptForSonoraPasswordAsync();
        profilePanel.Children.Add(setPasswordButton);
        root.Children.Add(profilePanel);

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
        if (auth.IsSignedIn) _ = LoadProfileAsync();
    }

    private async Task RunAuthAsync(bool register)
    {
        if (string.IsNullOrWhiteSpace(emailBox.Text) || string.IsNullOrWhiteSpace(passwordBox.Password))
        {
            statusText.Text = "Введи почту и пароль.";
            return;
        }
        signInButton.IsEnabled = registerButton.IsEnabled = googleButton.IsEnabled = false;
        statusText.Text = register ? "Создаём аккаунт…" : "Выполняем вход…";
        try
        {
            if (register) await auth.RegisterAsync(emailBox.Text.Trim(), passwordBox.Password);
            else await auth.SignInAsync(emailBox.Text.Trim(), passwordBox.Password);
            UpdateState();
            await LoadProfileAsync();
        }
        catch (Exception ex) { statusText.Text = ex.Message; }
        finally { signInButton.IsEnabled = registerButton.IsEnabled = googleButton.IsEnabled = true; }
    }

    private async Task RunGoogleAuthAsync()
    {
        signInButton.IsEnabled = registerButton.IsEnabled = googleButton.IsEnabled = false;
        statusText.Text = "Открываем безопасный вход Google…";
        try
        {
            await auth.SignInWithGoogleAsync();
            UpdateState();
            await LoadProfileAsync();
            if (!auth.HasPasswordProvider)
                await PromptForSonoraPasswordAsync();
            statusText.Text = $"Выполнен вход через Google: {auth.Email}";
        }
        catch (Exception ex) { statusText.Text = ex.Message; }
        finally { signInButton.IsEnabled = registerButton.IsEnabled = googleButton.IsEnabled = true; }
    }

    private async Task PromptForSonoraPasswordAsync()
    {
        if (!auth.IsSignedIn || auth.HasPasswordProvider) return;
        var dialog = new Window
        {
            Title = "Пароль SONORA", Width = 360, Height = 270, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
            Background = Brush("#080B12"), Foreground = Brushes.White
        };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "Придумай пароль SONORA", FontSize = 18, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "Это отдельный пароль приложения. Пароль Google нам не передаётся.", Foreground = Brush("#93A1B5"), FontSize = 11, Margin = new Thickness(0, 7, 0, 12), TextWrapping = TextWrapping.Wrap });
        var first = new PasswordBox { Height = 36, Padding = new Thickness(9), Background = Brush("#111827"), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        var second = new PasswordBox { Height = 36, Padding = new Thickness(9), Background = Brush("#111827"), Foreground = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(first); panel.Children.Add(second);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var later = MakeButton("Позже", "#1A2638"); later.Width = 100; later.Click += (_, _) => dialog.DialogResult = false;
        var save = MakeButton("Сохранить", "#287BFF"); save.Width = 110; save.Margin = new Thickness(8, 0, 0, 0);
        save.Click += (_, _) =>
        {
            if (first.Password.Length < 6) { MessageBox.Show(dialog, "Пароль должен содержать минимум 6 символов.", "SONORA"); return; }
            if (first.Password != second.Password) { MessageBox.Show(dialog, "Пароли не совпадают.", "SONORA"); return; }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(later); buttons.Children.Add(save); panel.Children.Add(buttons); dialog.Content = panel;
        if (dialog.ShowDialog() != true)
        {
            profileStatusText.Text = "Вход через Google выполнен. Пароль SONORA можно задать позже.";
            return;
        }
        setPasswordButton.IsEnabled = false;
        try
        {
            await auth.SetSonoraPasswordAsync(first.Password);
            profileStatusText.Text = "Отдельный пароль SONORA сохранён.";
            UpdateState();
        }
        catch (Exception ex) { profileStatusText.Text = "Не удалось сохранить пароль: " + ex.Message; }
        finally { setPasswordButton.IsEnabled = auth.IsSignedIn && !auth.HasPasswordProvider; }
    }

    private async Task LoadProfileAsync()
    {
        try
        {
            var token = await auth.GetValidIdTokenAsync();
            var existingProfile = await profiles.ReadAsync(auth.UserId!, token);
            currentProfile = existingProfile ?? new UserProfile
            {
                Nickname = !string.IsNullOrWhiteSpace(auth.GoogleDisplayName)
                    ? auth.GoogleDisplayName!
                    : auth.Email?.Split('@')[0] ?? "Пользователь"
            };
            if (!string.IsNullOrWhiteSpace(auth.GooglePhotoUrl))
                currentProfile.GooglePhotoUrl = auth.GooglePhotoUrl!;
            if (string.IsNullOrWhiteSpace(currentProfile.AvatarData))
                currentProfile.AvatarType = "google";
            if (existingProfile is null || !string.IsNullOrWhiteSpace(auth.GooglePhotoUrl))
                await profiles.WriteAsync(auth.UserId!, token, currentProfile);
            nicknameBox.Text = currentProfile.Nickname;
            selectedAvatarBase64 = null;
            ShowAvatar();
            profileStatusText.Text = "Профиль и фотография Google загружены.";
        }
        catch (Exception ex) { profileStatusText.Text = "Не удалось загрузить профиль: " + ex.Message; }
    }

    private void ChooseAvatar()
    {
        var dialog = new OpenFileDialog { Title = "Выбрать аватарку", Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp|Все файлы|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 256;
            bitmap.UriSource = new Uri(dialog.FileName);
            bitmap.EndInit();
            bitmap.Freeze();
            var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            if (stream.Length > 180 * 1024)
                throw new InvalidOperationException("Изображение слишком большое. Выбери картинку попроще.");
            selectedAvatarBase64 = Convert.ToBase64String(stream.ToArray());
            currentProfile.AvatarData = selectedAvatarBase64;
            currentProfile.AvatarContentType = "image/jpeg";
            currentProfile.AvatarType = "custom";
            ShowAvatar();
            profileStatusText.Text = "Новая аватарка выбрана. Нажми «Сохранить профиль» для загрузки в облако.";
        }
        catch (Exception ex) { profileStatusText.Text = "Не удалось обработать изображение: " + ex.Message; }
    }

    private void ShowAvatar()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(currentProfile.AvatarData))
            {
                var bytes = Convert.FromBase64String(currentProfile.AvatarData);
                using var stream = new MemoryStream(bytes);
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
                avatarImage.Source = image;
            }
            else if (!string.IsNullOrWhiteSpace(currentProfile.GooglePhotoUrl))
            {
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(currentProfile.GooglePhotoUrl); image.EndInit(); image.Freeze();
                avatarImage.Source = image;
            }
            else avatarImage.Source = null;
        }
        catch { avatarImage.Source = null; }
    }

    private async Task SaveProfileAsync()
    {
        if (!auth.IsSignedIn) return;
        var nickname = nicknameBox.Text.Trim();
        if (nickname.Length < 2 || nickname.Length > 24)
        {
            profileStatusText.Text = "Никнейм должен содержать от 2 до 24 символов.";
            return;
        }
        saveProfileButton.IsEnabled = false;
        try
        {
            currentProfile.Nickname = nickname;
            var token = await auth.GetValidIdTokenAsync();
            await profiles.WriteAsync(auth.UserId!, token, currentProfile);
            selectedAvatarBase64 = null;
            profileStatusText.Text = "Профиль сохранён. Предыдущая аватарка заменена в базе данных.";
        }
        catch (Exception ex) { profileStatusText.Text = "Не удалось сохранить профиль: " + ex.Message; }
        finally { saveProfileButton.IsEnabled = true; }
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
        signInButton.Visibility = registerButton.Visibility = googleButton.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        setPasswordButton.Visibility = signedIn && !auth.HasPasswordProvider ? Visibility.Visible : Visibility.Collapsed;
        setPasswordButton.IsEnabled = signedIn && !auth.HasPasswordProvider;
        syncButton.IsEnabled = signedIn;
        signOutButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        nicknameBox.IsEnabled = saveProfileButton.IsEnabled = signedIn;
        profileStatusText.Text = signedIn ? "Настройки профиля сохраняются в Firebase." : "Войди, чтобы управлять профилем.";
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
