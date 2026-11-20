using Avalonia.Controls;
using Avalonia.Interactivity;
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
        if (sender is not Button { Tag: string section })
            return;

        viewModel.Navigate(section);
        PageTitle.Text = viewModel.PageTitle;
        PageSubtitle.Text = viewModel.PageSubtitle;

        foreach (var button in this.GetVisualDescendants().OfType<Button>().Where(b => b.Tag is string))
        {
            button.Classes.Remove("active");
            if (Equals(button.Tag, section))
                button.Classes.Add("active");
        }
    }
}