using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DrugaMusic.Models;
using DrugaMusic.Services;
using Microsoft.Win32;
using NAudio.Wave;

namespace SONORA.Native;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Track> tracks = new();
    private readonly ObservableCollection<string> artists = new();
    private readonly ICollectionView tracksView;
    private readonly PlaybackService playback = new();
    private readonly FirebaseAuthService auth;
    private readonly FirebaseDatabaseService cloud = new();
    private readonly DispatcherTimer timer;
    private readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SONORA");
    private string LibraryFile => Path.Combine(dataDir, "library.json");
    private List<string> queue = new();
    private int queueIndex = -1;
    private bool updatingSeek, seeking, favoritesOnly;
    private Track? Current => queueIndex >= 0 && queueIndex < queue.Count ? tracks.FirstOrDefault(t => t.Id == queue[queueIndex]) : null;

    public ObservableCollection<string> Mixes { get; } = new() { "Микс дня", "Микс недели", "Для тебя" };
    public ObservableCollection<string> Genres { get; } = new() { "Поп", "Рок", "Электроника", "Хип-хоп", "Инди", "Джаз" };

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(dataDir);
        auth = new FirebaseAuthService(dataDir);
        tracksView = CollectionViewSource.GetDefaultView(tracks);
        TracksGrid.ItemsSource = tracksView;
        ArtistsList.ItemsSource = artists;
        DataContext = this;

        LoadLibrary();
        playback.TrackEnded += (_, _) => Dispatcher.BeginInvoke(AdvanceAfterEnd);
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => UpdateProgress();
        timer.Start();
        Closed += (_, _) => { timer.Stop(); playback.Dispose(); };
        _ = RestoreAccountAsync();
    }

    private async Task RestoreAccountAsync()
    {
        await auth.RestoreSessionAsync();
        UpdateAccountButton();
    }

    private void UpdateAccountButton()
    {
        AccountButton.Content = auth.IsSignedIn ? (auth.GoogleDisplayName ?? auth.Email ?? "Аккаунт") : "Войти в аккаунт";
    }

    private void LoadLibrary()
    {
        try
        {
            if (File.Exists(LibraryFile))
                foreach (var track in JsonSerializer.Deserialize<List<Track>>(File.ReadAllText(LibraryFile)) ?? new())
                    if (File.Exists(track.FilePath)) tracks.Add(track);
        }
        catch (Exception ex) { MessageBox.Show("Не удалось загрузить библиотеку: " + ex.Message, "SONORA"); }
        RefreshLibrary();
    }

    private void SaveLibrary()
    {
        Directory.CreateDirectory(dataDir);
        var temp = LibraryFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(tracks, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, LibraryFile, true);
    }

    private void RefreshLibrary()
    {
        TrackCount.Text = $"{tracks.Count} треков";
        EmptyStateText.Visibility = tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        artists.Clear();
        foreach (var artist in tracks.Select(t => t.Artist).Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a).Take(40))
            artists.Add(artist);
        ArtistCount.Text = $"{artists.Count} исполнителей";
        tracksView.Refresh();
    }

    private static string SaveArtwork(TagLib.File tag, string path, string directory)
    {
        try
        {
            var picture = tag.Tag.Pictures.FirstOrDefault();
            if (picture is null || picture.Data.Data.Length == 0) return "";
            var artworkDir = Path.Combine(directory, "Artwork");
            Directory.CreateDirectory(artworkDir);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
            var ext = picture.MimeType.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
            var target = Path.Combine(artworkDir, hash + ext);
            if (!File.Exists(target)) File.WriteAllBytes(target, picture.Data.Data);
            return target;
        }
        catch { return ""; }
    }

    private void AddMusic_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Добавить музыку в SONORA",
            Filter = "Аудиофайлы|*.mp3;*.wav;*.aiff;*.aif;*.flac;*.m4a;*.wma|Все файлы|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        var added = 0;
        foreach (var path in dialog.FileNames)
        {
            if (tracks.Any(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                using var audio = new AudioFileReader(path);
                using var tag = TagLib.File.Create(path);
                tracks.Add(new Track
                {
                    FilePath = path,
                    Title = string.IsNullOrWhiteSpace(tag.Tag.Title) ? Path.GetFileNameWithoutExtension(path) : tag.Tag.Title,
                    Artist = string.IsNullOrWhiteSpace(tag.Tag.FirstPerformer) ? "Неизвестный исполнитель" : tag.Tag.FirstPerformer,
                    Album = tag.Tag.Album ?? "",
                    Genre = tag.Tag.FirstGenre ?? "",
                    Year = tag.Tag.Year == 0 ? null : tag.Tag.Year,
                    ArtworkPath = SaveArtwork(tag, path, dataDir),
                    DurationText = audio.TotalTime.ToString(@"m\:ss")
                });
                added++;
            }
            catch { }
        }
        SaveLibrary();
        RefreshLibrary();
        if (added == 0) MessageBox.Show("Новых поддерживаемых аудиофайлов не найдено.", "SONORA");
    }

    private void TracksGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TracksGrid.SelectedItem is Track track) StartQueue(track);
    }

    private void StartQueue(Track track)
    {
        queue = tracksView.Cast<Track>().Select(t => t.Id).ToList();
        if (!queue.Contains(track.Id)) queue = tracks.Select(t => t.Id).ToList();
        queueIndex = queue.IndexOf(track.Id);
        PlayCurrent();
    }

    private void PlayCurrent()
    {
        var track = Current;
        if (track is null) return;
        try
        {
            playback.Play(track.FilePath);
            playback.SetVolume((float)VolumeSlider.Value);
            NowPlayingTitle.Text = track.Title;
            NowPlayingArtist.Text = track.Artist;
            DurationText.Text = playback.Duration.ToString(@"m\:ss");
            PlayPauseButton.Content = "Ⅱ";
            TracksGrid.SelectedItem = track;
            TracksGrid.ScrollIntoView(track);
            UpdateProgress();
        }
        catch (Exception ex) { MessageBox.Show("Не удалось воспроизвести трек: " + ex.Message, "SONORA"); }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (queueIndex < 0)
        {
            if (tracks.Count > 0) StartQueue(tracks[0]);
            return;
        }
        playback.Pause();
        PlayPauseButton.Content = playback.IsPlaying ? "Ⅱ" : "▶";
    }

    private void Next_Click(object sender, RoutedEventArgs e) => Advance(false);
    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (playback.Position > TimeSpan.FromSeconds(3)) { playback.Seek(TimeSpan.Zero); return; }
        if (queue.Count == 0) return;
        queueIndex = (queueIndex - 1 + queue.Count) % queue.Count;
        PlayCurrent();
    }

    private void AdvanceAfterEnd() => Advance(true);
    private void Advance(bool ended)
    {
        if (queue.Count == 0) return;
        if (queueIndex + 1 >= queue.Count)
        {
            if (ended) { PlayPauseButton.Content = "▶"; return; }
            queueIndex = 0;
        }
        else queueIndex++;
        PlayCurrent();
    }

    private void UpdateProgress()
    {
        PlayPauseButton.Content = playback.IsPlaying ? "Ⅱ" : "▶";
        if (playback.Duration <= TimeSpan.Zero) return;
        CurrentTimeText.Text = playback.Position.ToString(@"m\:ss");
        if (!seeking)
        {
            updatingSeek = true;
            SeekSlider.Maximum = Math.Max(1, playback.Duration.TotalSeconds);
            SeekSlider.Value = Math.Clamp(playback.Position.TotalSeconds, 0, SeekSlider.Maximum);
            updatingSeek = false;
        }
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!updatingSeek && !seeking && playback.Duration > TimeSpan.Zero)
            playback.Seek(TimeSpan.FromSeconds(e.NewValue));
    }
    private void SeekSlider_MouseDown(object sender, MouseButtonEventArgs e) => seeking = true;
    private void SeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        seeking = false;
        if (playback.Duration > TimeSpan.Zero) playback.Seek(TimeSpan.FromSeconds(SeekSlider.Value));
    }
    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        playback.SetVolume((float)e.NewValue);

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string page) return;
        switch (page)
        {
            case "home":
                favoritesOnly = false;
                PageTitle.Text = "Главная";
                break;
            case "browse":
                favoritesOnly = false;
                PageTitle.Text = "Обзор";
                break;
            case "favorites":
                favoritesOnly = true;
                PageTitle.Text = "Избранное";
                break;
            case "search":
                favoritesOnly = false;
                PageTitle.Text = "Поиск по библиотеке";
                var query = PromptText("Поиск музыки", "Введи название трека, исполнителя или альбома:");
                if (query is not null)
                {
                    tracksView.Filter = item => item is Track t &&
                        (t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         t.Album.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         t.Genre.Contains(query, StringComparison.OrdinalIgnoreCase));
                }
                return;
        }
        tracksView.Filter = item => item is Track t && (!favoritesOnly || t.IsFavorite);
        tracksView.Refresh();
    }

    private static string? PromptText(string title, string message)
    {
        var window = new Window { Title = title, Width = 390, Height = 170, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 13, 20)), Foreground = System.Windows.Media.Brushes.White };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = message, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
        var input = new TextBox { Height = 32, Padding = new Thickness(8) };
        panel.Children.Add(input);
        var ok = new Button { Content = "Найти", Margin = new Thickness(0, 12, 0, 0), IsDefault = true };
        ok.Click += (_, _) => window.DialogResult = true;
        panel.Children.Add(ok);
        window.Content = panel;
        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private async void Account_Click(object sender, RoutedEventArgs e)
    {
        if (auth.IsSignedIn)
        {
            var answer = MessageBox.Show($"Выполнен вход: {auth.Email ?? auth.GoogleDisplayName}\n\nСинхронизировать библиотечные настройки сейчас?\n«Нет» — выйти из аккаунта.", "Аккаунт SONORA", MessageBoxButton.YesNoCancel);
            if (answer == MessageBoxResult.Cancel) return;
            if (answer == MessageBoxResult.No) auth.SignOut();
            else await SyncCloudAsync();
            UpdateAccountButton();
            return;
        }

        AccountButton.IsEnabled = false;
        try
        {
            await auth.SignInWithGoogleAsync();
            UpdateAccountButton();
            await SyncCloudAsync();
            MessageBox.Show("Вход выполнен.", "SONORA");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Вход в SONORA"); }
        finally { AccountButton.IsEnabled = true; }
    }

    private async Task SyncCloudAsync()
    {
        if (!auth.IsSignedIn || string.IsNullOrWhiteSpace(auth.UserId)) return;
        try
        {
            var token = await auth.GetValidIdTokenAsync();
            var remote = await cloud.ReadAsync(auth.UserId, token) ?? new CloudLibrarySnapshot();
            remote.FavoriteTrackKeys = tracks.Where(t => t.IsFavorite).Select(t => t.Id).Distinct().ToList();
            remote.UpdatedAtUtc = DateTime.UtcNow;
            await cloud.WriteAsync(auth.UserId, token, remote);
            MessageBox.Show("Избранное и настройки отправлены в Firebase.", "SONORA");
        }
        catch (Exception ex) { MessageBox.Show("Ошибка синхронизации: " + ex.Message, "SONORA"); }
    }
}