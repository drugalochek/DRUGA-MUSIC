using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DrugaMusic.Models;
using DrugaMusic.Services;

namespace Sonora.ViewModels;

public sealed class ArtistTile
{
    public string Name { get; init; } = "";
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "♫" : Name[..1].ToUpperInvariant();
    public string CountText { get; init; } = "";
}

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly string dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SONORA");
    private readonly PlaybackService playback = new();
    private readonly FirebaseAuthService auth;
    private readonly FirebaseProfileService profiles = new();
    private readonly DispatcherTimer timer;
    private List<string> queue = [];
    private int queueIndex = -1;

    public ObservableCollection<Track> Tracks { get; } = [];
    public ObservableCollection<Track> VisibleTracks { get; } = [];
    public ObservableCollection<Track> FavoriteTracks { get; } = [];
    public ObservableCollection<ArtistTile> Artists { get; } = [];

    [ObservableProperty] private Track? currentTrack;
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private string searchQuery = "";
    [ObservableProperty] private string currentSection = "home";
    [ObservableProperty] private double progress;
    [ObservableProperty] private double volume = 0.8;
    [ObservableProperty] private string currentTimeText = "0:00";
    [ObservableProperty] private string durationText = "0:00";
    [ObservableProperty] private string statusText = "Готово";
    [ObservableProperty] private string authMessage = "Войди, чтобы синхронизировать профиль и настройки.";
    [ObservableProperty] private string email = "";
    [ObservableProperty] private string nickname = "";
    [ObservableProperty] private string profileName = "Гость";
    [ObservableProperty] private string accountStatusText = "Локальный профиль";
    [ObservableProperty] private bool isSignedIn;
    [ObservableProperty] private Bitmap? currentArtwork;

    public string CurrentTrackTitle => CurrentTrack?.Title ?? "Выбери музыку";
    public string CurrentTrackArtist => CurrentTrack?.Artist ?? "SONORA";
    public string PlayButtonGlyph => IsPlaying ? "Ⅱ" : "▶";
    public string TrackCountText => $"{Tracks.Count} треков";
    public string ArtistCountText => $"{Artists.Count} исполнителей";
    public string FavoriteCountText => $"{FavoriteTracks.Count} треков";
    public bool IsLibraryEmpty => Tracks.Count == 0;
    public bool IsFavoritesEmpty => FavoriteTracks.Count == 0;
    public bool HasCurrentTrack => CurrentTrack is not null;
    public string EmptyLibraryText => "Пока здесь пусто. Добавь музыку — и SONORA соберёт твою коллекцию.";
    public string EmptyFavoritesText => "Здесь появятся треки, которые ты добавишь в избранное.";
    public string GreetingText => DateTime.Now.Hour switch
    {
        < 6 => "Ночная музыка для тебя",
        < 12 => "Доброе утро",
        < 18 => "Хорошего дня",
        _ => "Добрый вечер"
    };

    public MainWindowViewModel()
    {
        Directory.CreateDirectory(dataDirectory);
        auth = new FirebaseAuthService(dataDirectory);
        LoadLibrary();
        playback.TrackEnded += (_, _) => Dispatcher.UIThread.Post(NextTrack);
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => UpdateProgress();
        timer.Start();
        _ = RestoreAccountAsync();
    }

    partial void OnCurrentTrackChanged(Track? value)
    {
        OnPropertyChanged(nameof(CurrentTrackTitle));
        OnPropertyChanged(nameof(CurrentTrackArtist));
        OnPropertyChanged(nameof(HasCurrentTrack));
        LoadCurrentArtwork(value?.ArtworkPath);
    }

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayButtonGlyph));
    partial void OnVolumeChanged(double value) => playback.SetVolume((float)Math.Clamp(value, 0, 1));
    partial void OnSearchQueryChanged(string value) => RefreshArtistsAndLibrary();

    private void LoadCurrentArtwork(string? path)
    {
        CurrentArtwork?.Dispose();
        CurrentArtwork = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            using var stream = File.OpenRead(path);
            CurrentArtwork = new Bitmap(stream);
        }
        catch { CurrentArtwork = null; }
    }

    public void ImportFiles(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path) || Tracks.Any(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                using var audio = new NAudio.Wave.AudioFileReader(path);
                using var tag = TagLib.File.Create(path);
                Tracks.Add(new Track
                {
                    FilePath = path,
                    Title = string.IsNullOrWhiteSpace(tag.Tag.Title) ? Path.GetFileNameWithoutExtension(path) : tag.Tag.Title,
                    Artist = string.IsNullOrWhiteSpace(tag.Tag.FirstPerformer) ? "Неизвестный исполнитель" : tag.Tag.FirstPerformer,
                    Album = tag.Tag.Album ?? "",
                    Genre = tag.Tag.FirstGenre ?? "",
                    Year = tag.Tag.Year == 0 ? null : tag.Tag.Year,
                    ArtworkPath = SaveArtwork(tag, path),
                    DurationText = audio.TotalTime.ToString(@"m\:ss")
                });
                added++;
            }
            catch
            {
                // Unsupported or damaged files are skipped without interrupting the import batch.
            }
        }

        SaveLibrary();
        RefreshArtistsAndLibrary();
        StatusText = added == 0 ? "Новых поддерживаемых файлов не найдено" : $"Добавлено треков: {added}";
    }

    public void PlayTrack(Track track)
    {
        queue = Tracks.Select(t => t.Id).ToList();
        queueIndex = queue.IndexOf(track.Id);
        StartCurrentTrack();
    }

    public void TogglePlayback()
    {
        if (CurrentTrack is null)
        {
            var first = Tracks.FirstOrDefault();
            if (first is not null) PlayTrack(first);
            return;
        }
        playback.Pause();
        IsPlaying = playback.IsPlaying;
    }

    public void SeekToPercent(double percent)
    {
        if (playback.Duration <= TimeSpan.Zero) return;
        var clamped = Math.Clamp(percent, 0, 100);
        playback.Seek(TimeSpan.FromMilliseconds(playback.Duration.TotalMilliseconds * clamped / 100d));
        UpdateProgress();
    }

    public void NextTrack()
    {
        if (queue.Count == 0) return;
        queueIndex = (queueIndex + 1) % queue.Count;
        StartCurrentTrack();
    }

    public void PreviousTrack()
    {
        if (queue.Count == 0) return;
        queueIndex = queueIndex <= 0 ? queue.Count - 1 : queueIndex - 1;
        StartCurrentTrack();
    }

    public void StopAndClearCurrentTrack()
    {
        playback.Stop();
        IsPlaying = false;
        CurrentTrack = null;
        CurrentArtwork?.Dispose();
        CurrentArtwork = null;
    }

    public void ToggleFavorite(Track track)
    {
        track.IsFavorite = !track.IsFavorite;
        SaveLibrary();
        RefreshArtistsAndLibrary();
    }

    private void StartCurrentTrack()
    {
        var id = queueIndex >= 0 && queueIndex < queue.Count ? queue[queueIndex] : null;
        var track = Tracks.FirstOrDefault(t => t.Id == id);
        if (track is null) return;

        try
        {
            playback.Play(track.FilePath);
            CurrentTrack = track;
            DurationText = playback.Duration.ToString(@"m\:ss");
            IsPlaying = true;
            UpdateProgress();
        }
        catch (Exception ex)
        {
            StatusText = "Не удалось воспроизвести файл: " + ex.Message;
            IsPlaying = false;
        }
    }

    private void UpdateProgress()
    {
        if (CurrentTrack is null || playback.Duration <= TimeSpan.Zero)
        {
            Progress = 0;
            CurrentTimeText = "0:00";
            return;
        }

        var position = playback.Position;
        Progress = Math.Clamp(position.TotalMilliseconds / playback.Duration.TotalMilliseconds * 100d, 0, 100);
        CurrentTimeText = position.ToString(@"m\:ss");
        DurationText = playback.Duration.ToString(@"m\:ss");
        IsPlaying = playback.IsPlaying;
    }

    private void LoadLibrary()
    {
        var file = Path.Combine(dataDirectory, "library.json");
        try
        {
            if (File.Exists(file))
            {
                foreach (var track in JsonSerializer.Deserialize<List<Track>>(File.ReadAllText(file)) ?? [])
                    if (File.Exists(track.FilePath)) Tracks.Add(track);
            }
        }
        catch (Exception ex)
        {
            StatusText = "Не удалось загрузить библиотеку: " + ex.Message;
        }
        RefreshArtistsAndLibrary();
    }

    private void SaveLibrary()
    {
        var file = Path.Combine(dataDirectory, "library.json");
        File.WriteAllText(file, JsonSerializer.Serialize(Tracks, new JsonSerializerOptions { WriteIndented = true }));
    }

    private string SaveArtwork(TagLib.File tag, string path)
    {
        try
        {
            var picture = tag.Tag.Pictures.FirstOrDefault();
            var bytes = picture?.Data.Data;
            if (bytes is null || bytes.Length == 0) return "";

            var directory = Path.Combine(dataDirectory, "Artwork");
            Directory.CreateDirectory(directory);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
            var extension = picture?.MimeType == "image/png" ? ".png" : ".jpg";
            var target = Path.Combine(directory, hash + extension);
            if (!File.Exists(target)) File.WriteAllBytes(target, bytes);
            return target;
        }
        catch { return ""; }
    }

    private void RefreshArtistsAndLibrary()
    {
        var filtered = Tracks.Where(t =>
            string.IsNullOrWhiteSpace(SearchQuery) ||
            t.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
            t.Artist.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
            t.Album.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)).ToList();

        VisibleTracks.Clear();
        foreach (var track in filtered) VisibleTracks.Add(track);

        FavoriteTracks.Clear();
        foreach (var track in filtered.Where(t => t.IsFavorite)) FavoriteTracks.Add(track);

        Artists.Clear();
        foreach (var group in filtered.Where(t => !string.IsNullOrWhiteSpace(t.Artist))
                     .GroupBy(t => t.Artist.Trim(), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).Take(24))
            Artists.Add(new ArtistTile { Name = group.Key, CountText = $"{group.Count()} треков" });

        OnPropertyChanged(nameof(TrackCountText));
        OnPropertyChanged(nameof(ArtistCountText));
        OnPropertyChanged(nameof(FavoriteCountText));
        OnPropertyChanged(nameof(IsLibraryEmpty));
        OnPropertyChanged(nameof(IsFavoritesEmpty));
        OnPropertyChanged(nameof(EmptyLibraryText));
        OnPropertyChanged(nameof(EmptyFavoritesText));
    }

    private async Task RestoreAccountAsync()
    {
        try
        {
            if (await auth.RestoreSessionAsync())
            {
                IsSignedIn = true;
                Email = auth.Email ?? "";
                ProfileName = auth.GoogleDisplayName ?? Email;
                AccountStatusText = "Аккаунт SONORA подключён";
                await LoadProfileAsync();
                AuthMessage = "Сессия восстановлена.";
            }
        }
        catch (Exception ex) { AuthMessage = ex.Message; }
    }

    private async Task LoadProfileAsync()
    {
        if (!auth.IsSignedIn || string.IsNullOrWhiteSpace(auth.UserId)) return;
        try
        {
            var profile = await profiles.ReadAsync(auth.UserId, await auth.GetValidIdTokenAsync());
            if (profile is not null)
            {
                Nickname = profile.Nickname;
                if (!string.IsNullOrWhiteSpace(profile.Nickname)) ProfileName = profile.Nickname;
            }
        }
        catch (Exception ex) { AuthMessage = "Профиль не загрузился: " + ex.Message; }
    }

    public async Task SaveProfileAsync()
    {
        if (!auth.IsSignedIn || string.IsNullOrWhiteSpace(auth.UserId))
        {
            ProfileName = string.IsNullOrWhiteSpace(Nickname) ? "Гость" : Nickname;
            AuthMessage = "Профиль сохранён только локально. Войди в аккаунт для облачной синхронизации.";
            return;
        }

        try
        {
            var profile = new UserProfile
            {
                Nickname = Nickname.Trim(),
                AvatarType = "google",
                GooglePhotoUrl = auth.GooglePhotoUrl ?? ""
            };
            await profiles.WriteAsync(auth.UserId, await auth.GetValidIdTokenAsync(), profile);
            ProfileName = string.IsNullOrWhiteSpace(Nickname) ? auth.GoogleDisplayName ?? Email : Nickname.Trim();
            AuthMessage = "Профиль сохранён.";
        }
        catch (Exception ex) { AuthMessage = "Не удалось сохранить профиль: " + ex.Message; }
    }

    public async Task SignInAsync(string email, string password)
    {
        try
        {
            await auth.SignInAsync(email.Trim(), password);
            await CompleteSignInAsync();
            AuthMessage = "Вход выполнен.";
        }
        catch (Exception ex) { AuthMessage = ex.Message; }
    }

    public async Task RegisterAsync(string email, string password)
    {
        try
        {
            await auth.RegisterAsync(email.Trim(), password);
            await CompleteSignInAsync();
            AuthMessage = "Аккаунт создан.";
        }
        catch (Exception ex) { AuthMessage = ex.Message; }
    }

    public async Task GoogleSignInAsync()
    {
        try
        {
            await auth.SignInWithGoogleAsync();
            await CompleteSignInAsync();
            AuthMessage = "Вход через Google выполнен.";
        }
        catch (Exception ex) { AuthMessage = ex.Message; }
    }

    private async Task CompleteSignInAsync()
    {
        IsSignedIn = auth.IsSignedIn;
        Email = auth.Email ?? "";
        ProfileName = auth.GoogleDisplayName ?? Email;
        AccountStatusText = "Аккаунт SONORA подключён";
        await LoadProfileAsync();
    }

    public void SignOut()
    {
        auth.SignOut();
        IsSignedIn = false;
        Email = "";
        Nickname = "";
        ProfileName = "Гость";
        AccountStatusText = "Локальный профиль";
        AuthMessage = "Ты вышел из аккаунта. Локальная музыка осталась на устройстве.";
    }

    public void Dispose()
    {
        timer.Stop();
        playback.Dispose();
        CurrentArtwork?.Dispose();
    }
}