using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
namespace SONORA.Native;
public partial class MainWindow : Window
{
 public ObservableCollection<string> Artists { get; } = new() { "Исполнитель 1", "Исполнитель 2", "Исполнитель 3", "Исполнитель 4", "Исполнитель 5", "Исполнитель 6" };
 public ObservableCollection<string> Mixes { get; } = new() { "Микс дня", "Микс недели", "Для тебя", "Рок-микс" };
 public ObservableCollection<string> Genres { get; } = new() { "Поп", "Рок", "Электроника", "Хип-хоп", "Инди", "Джаз" };
 public ObservableCollection<string> TopArtists { get; } = new() { "Артист 1", "Артист 2", "Артист 3", "Артист 4", "Артист 5" };
 public ObservableCollection<string> RecentTracks { get; } = new() { "Трек 01", "Трек 02", "Трек 03", "Трек 04", "Трек 05", "Трек 06" };
 public MainWindow()
 {
  InitializeComponent();
  DataContext = this;
 }
 private void Navigation_Click(object sender, RoutedEventArgs e)
 {
  if (sender is Button button && button.Tag is string page)
   MessageBox.Show($"Раздел «{button.Content}» будет подключён к нативной навигации SONORA.", "SONORA");
 }
}