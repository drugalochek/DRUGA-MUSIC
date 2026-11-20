using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DrugaMusic.Models;
using DrugaMusic.Services;
using NAudio.Wave;

namespace Sonora.ViewModels;

public sealed class ArtistTile
{
    public string Name { get; init; } = "";
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "♫" : Name[..1].ToUpperInvariant();
    public string CountText { get; init; } = "";
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly string dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SONORA");
    private readonly PlaybackService playback = new();
    private readonly DispatcherTimer timer;
    private List<string> queue = [];
    private int queueIndex = -1;

    public ObservableCollection<Track> Tracks { get; } = [];
    public ObservableCollection<ArtistTile> Artists { get; } = [];

    [ObservableProperty] private Track? currentTrack;
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private string searchQuery = "";
    [ObservableProperty] private string currentSection = "home";
    [ObservableProperty] private double progress;
    [ObservableProperty] private string currentTimeText = "0:00";
    [ObservableProperty] private string durationText = "0:00";
    [ObservableProperty] private string statusText = "Готово";
    [ObservableProperty] private string pageTitle = "Главная";
    [ObservableProperty] private string pageSubtitle = "Открывай любимое и находи то, что захочется переслушать.";

    public string CurrentTrackTitle => CurrentTrack?.Title ?? "Выбери музыку";
    public string CurrentTrackArtist => CurrentTrack?.Artist ?? "SONORA";
    public string PlayButtonGlyph => IsPlaying ? "Ⅱ" : "▶";
    public string TrackCountText => $"{Tracks.Count} треков";
    public string ArtistCountText => $"{Artists.Count} исполнителей";
    public bool IsLibraryEmpty => Tracks.Count == 0;
    public string EmptyLibraryText => "Пока здесь пусто. Добавь музыку — и SONORA соберёт твою коллекцию.";

    public MainWindowViewModel()
    {
        Directory.CreateDirectory(dataDirectory);
        LoadLibrary();
        playback.TrackEnded += (_, _) => Dispatcher.UIThread.Post(NextTrack);
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => UpdateProgress();
        timer.Start();
    }

    partial void OnCurrentTrackChanged(Track? value)
    {
        OnPropertyChanged(nameof(CurrentTrackTitle));
        OnPropertyChanged(nameof(CurrentTrackArtist));
    }

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayButtonGlyph));

    partial void OnSearchQueryChanged(string value) => RefreshArtistsAndLibrary();

    public void Navigate(string section)
    {
        CurrentSection = section;
        (PageTitle, PageSubtitle) = section switch
        {
            "search" => ("Поиск", "Найди трек, исполнителя или альбом в своей коллекции."),
            "library" => ("Моя музыка", "Вся музыка, которую ты добавил в SONORA."),
            "mixes" => ("Миксы", "Подборки на основе твоей музыкальной библиотеки."),
            "favorites" => ("Избранное", "Треки, к которым хочется возвращаться."),
            "profile" => ("Профиль", "Твои данные и настройки профиля."),
            "accounts" => ("Привязанные аккаунты", "Подключённые музыкальные сервисы и способы входа."),
            _ => ("Главная", "Открывай любимое и находи то, что захочется переслушать.")
        };
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
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
                using var audio = new AudioFileReader(path);
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
                {
                    if (File.Exists(track.FilePath))
                        Tracks.Add(track);
                }
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
            var extension = picture.MimeType == "image/png" ? ".png" : ".jpg";
            var target = Path.Combine(directory, hash + extension);
            if (!File.Exists(target)) File.WriteAllBytes(target, bytes);
            return target;
        }
        catch
        {
            return "";
        }
    }

    private void RefreshArtistsAndLibrary()
    {
        Artists.Clear();
        var filtered = Tracks.Where(t =>
            string.IsNullOrWhiteSpace(SearchQuery) ||
            t.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
            t.Artist.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
            t.Album.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var group in filtered.Where(t => !string.IsNullOrWhiteSpace(t.Artist))
                     .GroupBy(t => t.Artist.Trim(), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).Take(24))
        {
            Artists.Add(new ArtistTile { Name = group.Key, CountText = $"{group.Count()} треков" });
        }

        OnPropertyChanged(nameof(TrackCountText));
        OnPropertyChanged(nameof(ArtistCountText));
        OnPropertyChanged(nameof(IsLibraryEmpty));
        OnPropertyChanged(nameof(EmptyLibraryText));
    }
}