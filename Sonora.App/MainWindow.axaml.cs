using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Sonora.ViewModels;

namespace Sonora;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();
        viewModel = new MainWindowViewModel();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Добавить музыку в SONORA",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Аудиофайлы")
                {
                    Patterns = ["*.mp3", "*.wav", "*.aiff", "*.aif", "*.flac", "*.m4a", "*.wma"]
                }
            ]
        });

        viewModel.ImportFiles(files.Select(file => file.Path.LocalPath));
    }

    private void Track_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DrugaMusic.Models.Track track })
            viewModel.PlayTrack(track);
    }

    private void PlayPause_Click(object? sender, RoutedEventArgs e) => viewModel.TogglePlayback();
    private void Next_Click(object? sender, RoutedEventArgs e) => viewModel.NextTrack();
    private void Previous_Click(object? sender, RoutedEventArgs e) => viewModel.PreviousTrack();

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
            viewModel.SearchQuery = box.Text ?? "";
    }

    private void Nav_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string section }) return;
        ShowPage(section);
    }

    private void Profile_Click(object? sender, RoutedEventArgs e) => ShowPage("profile");
    private void Accounts_Click(object? sender, RoutedEventArgs e) => ShowPage("accounts");
    private void Settings_Click(object? sender, RoutedEventArgs e) => ShowPage("settings");

    private void ShowPage(string section)
    {
        HomePage.IsVisible = section == "home";
        ExplorePage.IsVisible = section == "explore";
        FavoritesPage.IsVisible = section == "favorites";
        SearchPage.IsVisible = section == "search";
        ProfilePage.IsVisible = section == "profile";
        AccountsPage.IsVisible = section == "accounts";
        SettingsPage.IsVisible = section == "settings";
        NowPlayingOverlay.IsVisible = false;
        viewModel.CurrentSection = section;

        foreach (var button in this.GetVisualDescendants().OfType<Button>().Where(b => b.Tag is string))
        {
            button.Classes.Remove("active");
            if (Equals(button.Tag, section)) button.Classes.Add("active");
        }
    }

    private void MiniPlayer_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button) return;
        NowPlayingOverlay.IsVisible = true;
        e.Handled = true;
    }

    private void ClosePlayer_Click(object? sender, RoutedEventArgs e) => NowPlayingOverlay.IsVisible = false;
    private void CloseMini_Click(object? sender, RoutedEventArgs e) => viewModel.StopAndClearCurrentTrack();

    private async void SaveProfile_Click(object? sender, RoutedEventArgs e) =>
        await viewModel.SaveProfileAsync();

    private async void SignIn_Click(object? sender, RoutedEventArgs e) =>
        await viewModel.SignInAsync(EmailBox.Text ?? "", PasswordBox.Text ?? "");

    private async void Register_Click(object? sender, RoutedEventArgs e) =>
        await viewModel.RegisterAsync(EmailBox.Text ?? "", PasswordBox.Text ?? "");

    private async void GoogleSignIn_Click(object? sender, RoutedEventArgs e) =>
        await viewModel.GoogleSignInAsync();

    private void SignOut_Click(object? sender, RoutedEventArgs e) => viewModel.SignOut();
}