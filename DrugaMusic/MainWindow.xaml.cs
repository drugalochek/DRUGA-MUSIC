using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using DrugaMusic.Models;
using DrugaMusic.Services;
using Microsoft.Win32;
using TagLib;

namespace DrugaMusic;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Track> tracks = new();
    private readonly ICollectionView tracksView;
    private readonly PlaybackService playback = new();
    private readonly DispatcherTimer progressTimer;
    private readonly string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrugaMusic");
    private readonly string libraryFile;
    private int currentIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        libraryFile = Path.Combine(dataDirectory, "library.json");
        tracksView = CollectionViewSource.GetDefaultView(tracks);
        TracksList.ItemsSource = tracksView;
        LoadLibrary();
        progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        progressTimer.Tick += (_, _) => UpdateProgress();
        progressTimer.Start();
        Closed += (_, _) => { progressTimer.Stop(); playback.Dispose(); };
    }

    private void LoadLibrary()
    {
        try
        {
            if (File.Exists(libraryFile))
            {
                var saved = JsonSerializer.Deserialize<List<Track>>(File.ReadAllText(libraryFile)) ?? new();
                foreach (var track in saved.Where(t => File.Exists(t.FilePath))) tracks.Add(track);
            }
        }
        catch (Exception ex) { MessageBox.Show($"Не удалось загрузить библиотеку: {ex.Message}", "Druga Music"); }
        RefreshLibraryState();
    }

    private void SaveLibrary()
    {
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(libraryFile, JsonSerializer.Serialize(tracks.ToList(), new JsonSerializerOptions { WriteIndented = true }));
    }

    private void AddMusic_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Добавить музыку в Druga Music",
            Filter = "Аудиофайлы|*.mp3;*.wav;*.aiff;*.aif;*.wma;*.aac;*.m4a;*.flac|Все файлы|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        int added = 0;
        foreach (var path in dialog.FileNames)
        {
            if (tracks.Any(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                using var reader = new NAudio.Wave.AudioFileReader(path);
                var metadata = TagLib.File.Create(path);
                tracks.Add(new Track
                {
                    FilePath = path,
                    Title = string.IsNullOrWhiteSpace(metadata.Tag.Title) ? Path.GetFileNameWithoutExtension(path) : metadata.Tag.Title,
                    Artist = string.IsNullOrWhiteSpace(metadata.Tag.FirstPerformer) ? "Неизвестный исполнитель" : metadata.Tag.FirstPerformer,
                    Album = metadata.Tag.Album ?? "",
                    DurationText = reader.TotalTime.ToString(@"m\:ss")
                });
                added++;
            }
            catch { /* Skip unreadable or unsupported files and continue importing. */ }
        }
        SaveLibrary();
        RefreshLibraryState();
        if (added == 0) MessageBox.Show("Новых поддерживаемых аудиофайлов не найдено.", "Druga Music");
    }

    private void RefreshLibraryState()
    {
        TrackCountText.Text = $"{tracks.Count} треков";
        EmptyStateText.Visibility = tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TracksList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TracksList.SelectedItem is Track track) PlayTrack(tracks.IndexOf(track));
    }

    private void PlayTrack(int index)
    {
        if (index < 0 || index >= tracks.Count) return;
        try
        {
            currentIndex = index;
            var track = tracks[index];
            playback.Play(track.FilePath);
            playback.SetVolume((float)VolumeSlider.Value);
            NowPlayingTitle.Text = track.Title;
            NowPlayingArtist.Text = track.Artist;
            DurationText.Text = playback.Duration.ToString(@"m\:ss");
            PlayPauseButton.Content = "Ⅱ";
            TracksList.SelectedItem = track;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось воспроизвести файл. Проверь формат и доступ к файлу.\n\n{ex.Message}", "Druga Music");
        }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (currentIndex < 0) { if (tracks.Count > 0) PlayTrack(0); return; }
        playback.Pause();
        PlayPauseButton.Content = playback.IsPlaying ? "Ⅱ" : "▶";
    }

    private void Next_Click(object sender, RoutedEventArgs e) => PlayNext();
    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (tracks.Count > 0) PlayTrack(currentIndex <= 0 ? tracks.Count - 1 : currentIndex - 1);
    }
    private void PlayNext()
    {
        if (tracks.Count > 0) PlayTrack((currentIndex + 1) % tracks.Count);
    }

    private void UpdateProgress()
    {
        if (playback.Duration <= TimeSpan.Zero) return;
        CurrentTimeText.Text = playback.Position.ToString(@"m\:ss");
        PlaybackProgress.Value = Math.Clamp(playback.Position.TotalSeconds / playback.Duration.TotalSeconds * 100, 0, 100);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        playback.SetVolume((float)e.NewValue);

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        tracksView.Filter = item => item is Track t &&
            (t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             t.Album.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void Home_Click(object sender, RoutedEventArgs e) =>
        SetHeader("Твоя музыка. Твоё пространство.", "Собери всё, что любишь слушать, в одном месте.");
    private void Library_Click(object sender, RoutedEventArgs e) =>
        SetHeader("Моя музыка", "Твоя локальная коллекция и сохранённые композиции.");
    private void Search_Click(object sender, RoutedEventArgs e)
    {
        SetHeader("Поиск музыки", "Поиск по локальной библиотеке.");
        SearchBox.Focus();
    }
    private void Playlists_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("Редактор плейлистов — следующий этап разработки.", "Druga Music");
    private void Services_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("Интеграции будут добавляться через отдельные провайдеры с учётом официальных API и правил воспроизведения каждого сервиса.", "Музыкальные сервисы");
    private void SetHeader(string title, string subtitle) { GreetingText.Text = title; SubtitleText.Text = subtitle; }
}