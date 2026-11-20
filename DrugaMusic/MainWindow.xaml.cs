using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DrugaMusic.Models;
using DrugaMusic.Services;
using Microsoft.Win32;
namespace DrugaMusic;
public partial class MainWindow : Window
{
 private readonly ObservableCollection<Track> tracks = new();
 private readonly ObservableCollection<Playlist> playlists = new();
 private readonly ICollectionView tracksView;
 private readonly PlaybackService playback = new();
 private readonly DispatcherTimer timer;
 private readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SONORA");
 private string LibraryFile => Path.Combine(dataDir,"library.json");
 private string PlaylistsFile => Path.Combine(dataDir,"playlists.json");
 private List<string> queue = new();
 private int queueIndex = -1;
 private bool updatingSeek, seeking, shuffle;
 private int repeat;
 private Track? Current => queueIndex >= 0 && queueIndex < queue.Count ? tracks.FirstOrDefault(t=>t.Id==queue[queueIndex]) : null;
 public MainWindow()
 {
  InitializeComponent();
  var legacyDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DrugaMusic");
  if(!Directory.Exists(dataDir)&&Directory.Exists(legacyDir)) try{Directory.Move(legacyDir,dataDir);}catch{}
  tracksView=CollectionViewSource.GetDefaultView(tracks); TracksList.ItemsSource=tracksView;
  LoadLibrary(); LoadPlaylists(); playback.TrackEnded += (_,_)=>Dispatcher.BeginInvoke(AdvanceAfterEnd);
  timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)}; timer.Tick+=(_,_)=>UpdateProgress(); timer.Start();
  Closed+=(_,_)=>{timer.Stop();playback.Dispose();};
 }
 private void LoadLibrary()
 {
  try { if(File.Exists(LibraryFile)) foreach(var t in JsonSerializer.Deserialize<List<Track>>(File.ReadAllText(LibraryFile))??new()) if(File.Exists(t.FilePath)) tracks.Add(t); }
  catch(Exception ex){MessageBox.Show("Ошибка загрузки библиотеки: "+ex.Message,"SONORA");}
  RefreshLibrary();
 }
 private void SaveLibrary(){Directory.CreateDirectory(dataDir);File.WriteAllText(LibraryFile,JsonSerializer.Serialize(tracks,new JsonSerializerOptions{WriteIndented=true}));}
 private void LoadPlaylists()
 {
  try{if(File.Exists(PlaylistsFile))foreach(var p in JsonSerializer.Deserialize<List<Playlist>>(File.ReadAllText(PlaylistsFile))??new()) {p.TrackIds=p.TrackIds.Where(id=>tracks.Any(t=>t.Id==id)).Distinct().ToList();playlists.Add(p);}}
  catch(Exception ex){MessageBox.Show("Ошибка загрузки плейлистов: "+ex.Message,"SONORA");}
 }
 private void SavePlaylists(){Directory.CreateDirectory(dataDir);File.WriteAllText(PlaylistsFile,JsonSerializer.Serialize(playlists,new JsonSerializerOptions{WriteIndented=true}));}
 private string SaveArtwork(TagLib.File file,string path)
 {
  try{
   var bytes=file.Tag.Pictures.FirstOrDefault()?.Data.Data;if(bytes is null||bytes.Length==0)return "";
   var dir=Path.Combine(dataDir,"Artwork");Directory.CreateDirectory(dir);
   var name=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
   var ext=file.Tag.Pictures[0].MimeType=="image/png"?".png":".jpg";var target=Path.Combine(dir,name+ext);
   if(!File.Exists(target))File.WriteAllBytes(target,bytes);return target;
  }catch{return "";}
 }
 private void AddMusic_Click(object sender,RoutedEventArgs e)
 {
  var dlg=new OpenFileDialog{Title="Добавить музыку в SONORA",Filter="Аудио|*.mp3;*.wav;*.aiff;*.aif|Все файлы|*.*",Multiselect=true};
  if(dlg.ShowDialog(this)!=true)return;int added=0;
  foreach(var path in dlg.FileNames){
   if(tracks.Any(t=>string.Equals(t.FilePath,path,StringComparison.OrdinalIgnoreCase)))continue;
   try{
    using var audio=new NAudio.Wave.AudioFileReader(path);using var tag=TagLib.File.Create(path);
    tracks.Add(new Track{FilePath=path,Title=string.IsNullOrWhiteSpace(tag.Tag.Title)?Path.GetFileNameWithoutExtension(path):tag.Tag.Title,
     Artist=string.IsNullOrWhiteSpace(tag.Tag.FirstPerformer)?"Неизвестный исполнитель":tag.Tag.FirstPerformer,
     Album=tag.Tag.Album??"",Genre=tag.Tag.FirstGenre??"",Year=tag.Tag.Year==0?null:tag.Tag.Year,
     ArtworkPath=SaveArtwork(tag,path),DurationText=audio.TotalTime.ToString(@"m\:ss")});added++;
   }catch{}
  }
  SaveLibrary();RefreshLibrary();if(added==0)MessageBox.Show("Новых поддерживаемых аудиофайлов не найдено.","SONORA");
 }
 private void RefreshLibrary(){TrackCountText.Text=$"{tracks.Count} треков";EmptyStateText.Visibility=tracks.Count==0?Visibility.Visible:Visibility.Collapsed;FavoriteCountText.Text=$"{tracks.Count(t=>t.IsFavorite)} избранных";}
 private void TracksList_DoubleClick(object sender,MouseButtonEventArgs e){if(TracksList.SelectedItem is Track t)StartQueue(t);}
 private void StartQueue(Track track)
 {
  queue=tracksView.Cast<Track>().Select(t=>t.Id).ToList();if(!queue.Contains(track.Id))queue=tracks.Select(t=>t.Id).ToList();
  if(shuffle){var rest=queue.Where(id=>id!=track.Id).OrderBy(_=>Random.Shared.Next()).ToList();queue=new[]{track.Id}.Concat(rest).ToList();}
  queueIndex=queue.IndexOf(track.Id);PlayCurrent();
 }
 private void PlayCurrent()
 {
  var t=Current;if(t is null)return;
  try{playback.Play(t.FilePath);playback.SetVolume((float)VolumeSlider.Value);NowPlayingTitle.Text=t.Title;NowPlayingArtist.Text=t.Artist;DurationText.Text=playback.Duration.ToString(@"m\:ss");PlayPauseButton.Content="Ⅱ";NowPlayingArtwork.Source=Artwork(t.ArtworkPath);TracksList.SelectedItem=t;TracksList.ScrollIntoView(t);UpdateProgress();}
  catch(Exception ex){MessageBox.Show("Не удалось воспроизвести файл: "+ex.Message,"SONORA");}
 }
 private BitmapImage? Artwork(string path)
 {
  try{if(!File.Exists(path))return null;var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.DecodePixelWidth=160;b.UriSource=new Uri(path);b.EndInit();b.Freeze();return b;}catch{return null;}
 }
 private void PlayPause_Click(object sender,RoutedEventArgs e){if(queueIndex<0){if(tracks.Count>0)StartQueue(tracks[0]);return;}playback.Pause();PlayPauseButton.Content=playback.IsPlaying?"Ⅱ":"▶";}
 private void Next_Click(object sender,RoutedEventArgs e)=>Advance(false);
 private void Previous_Click(object sender,RoutedEventArgs e){if(playback.Position>TimeSpan.FromSeconds(3)){playback.Seek(TimeSpan.Zero);return;}if(queue.Count==0)return;queueIndex=(queueIndex-1+queue.Count)%queue.Count;PlayCurrent();}
 private void AdvanceAfterEnd(){if(repeat==2){PlayCurrent();return;}Advance(true);}
 private void Advance(bool ended)
 {
  if(queue.Count==0)return;if(queueIndex+1>=queue.Count){if(ended&&repeat!=1){PlayPauseButton.Content="▶";return;}queueIndex=0;}else queueIndex++;PlayCurrent();
 }
 private void Shuffle_Click(object sender,RoutedEventArgs e)
 {
  shuffle=!shuffle;ShuffleButton.Content=shuffle?"🔀 Вкл.":"🔀";
  if(queueIndex>=0&&Current is not null){var id=Current.Id;var remaining=queue.Where(x=>x!=id).OrderBy(_=>shuffle?Random.Shared.Next():0).ToList();queue=new[]{id}.Concat(remaining).ToList();queueIndex=0;}
 }
 private void Repeat_Click(object sender,RoutedEventArgs e){repeat=(repeat+1)%3;RepeatButton.Content=repeat==0?"↻":repeat==1?"↻ Все":"↻ 1";}
 private void UpdateProgress()
 {
  if(playback.Duration<=TimeSpan.Zero)return;CurrentTimeText.Text=playback.Position.ToString(@"m\:ss");
  if(!seeking){updatingSeek=true;SeekSlider.Maximum=Math.Max(1,playback.Duration.TotalSeconds);SeekSlider.Value=Math.Clamp(playback.Position.TotalSeconds,0,SeekSlider.Maximum);updatingSeek=false;}
 }
 private void SeekSlider_ValueChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(!updatingSeek&&!seeking&&playback.Duration>TimeSpan.Zero)playback.Seek(TimeSpan.FromSeconds(e.NewValue));}
 private void SeekSlider_MouseDown(object sender,MouseButtonEventArgs e)=>seeking=true;
 private void SeekSlider_MouseUp(object sender,MouseButtonEventArgs e){seeking=false;if(playback.Duration>TimeSpan.Zero)playback.Seek(TimeSpan.FromSeconds(SeekSlider.Value));}
 private void VolumeSlider_ValueChanged(object sender,RoutedPropertyChangedEventArgs<double> e)=>playback.SetVolume((float)e.NewValue);
 private void SearchBox_TextChanged(object sender,TextChangedEventArgs e){var q=SearchBox.Text.Trim();tracksView.Filter=o=>o is Track t&&(q.Length==0||t.Title.Contains(q,StringComparison.OrdinalIgnoreCase)||t.Artist.Contains(q,StringComparison.OrdinalIgnoreCase)||t.Album.Contains(q,StringComparison.OrdinalIgnoreCase)||t.Genre.Contains(q,StringComparison.OrdinalIgnoreCase));}
 private void Favorite_Click(object sender,RoutedEventArgs e){if(TracksList.SelectedItem is not Track t)return;t.IsFavorite=!t.IsFavorite;FavoriteButton.Content=t.IsFavorite?"♥ В избранном":"♡ В избранное";SaveLibrary();RefreshLibrary();}
 private void Favorites_Click(object sender,RoutedEventArgs e){tracksView.Filter=o=>o is Track t&&t.IsFavorite;SetHeader("Избранное","Композиции, которые ты отметил.");}
 private void Home_Click(object sender,RoutedEventArgs e){tracksView.Filter=null;SetHeader("Твоя музыка. Твоё пространство.","Собери всё, что любишь слушать, в одном месте.");}
 private void Library_Click(object sender,RoutedEventArgs e){tracksView.Filter=null;SetHeader("Моя музыка","Твоя локальная коллекция и сохранённые композиции.");}
 private void Search_Click(object sender,RoutedEventArgs e){tracksView.Filter=null;SetHeader("Поиск музыки","Поиск по локальной библиотеке.");SearchBox.Focus();}
 private Playlist? PickPlaylist()
 {
  var w=new Window{Title="Выбрать плейлист",Owner=this,Width=340,Height=380,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(8,11,18)),Foreground=Brushes.White};
  var panel=new DockPanel{Margin=new Thickness(16)};var list=new ListBox{ItemsSource=playlists,DisplayMemberPath="Name",Background=new SolidColorBrush(Color.FromRgb(17,24,39)),Foreground=Brushes.White};
  var ok=new Button{Content="Выбрать",Margin=new Thickness(0,10,0,0)};ok.Click+=(_,_)=>{if(list.SelectedItem!=null)w.DialogResult=true;};DockPanel.SetDock(ok,Dock.Bottom);panel.Children.Add(ok);panel.Children.Add(list);w.Content=panel;return w.ShowDialog()==true?list.SelectedItem as Playlist:null;
 }
 private void AddToPlaylist_Click(object sender,RoutedEventArgs e)
 {
  if(TracksList.SelectedItem is not Track t){MessageBox.Show("Сначала выбери трек.","SONORA");return;}
  if(playlists.Count==0){Playlists_Click(sender,e);if(playlists.Count==0)return;}
  var p=PickPlaylist();if(p==null)return;if(!p.TrackIds.Contains(t.Id))p.TrackIds.Add(t.Id);SavePlaylists();
 }
 private void Playlists_Click(object sender,RoutedEventArgs e)
 {
  var w=new Window{Title="Плейлисты SONORA",Owner=this,Width=430,Height=480,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(8,11,18)),Foreground=Brushes.White};
  var root=new DockPanel{Margin=new Thickness(16)};var list=new ListBox{ItemsSource=playlists,DisplayMemberPath="Name",Background=new SolidColorBrush(Color.FromRgb(17,24,39)),Foreground=Brushes.White};
  var name=new TextBox{Width=190,Height=32,Padding=new Thickness(7),Background=new SolidColorBrush(Color.FromRgb(17,24,39)),Foreground=Brushes.White};
  var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0)};
  var add=new Button{Content="Создать"};add.Click+=(_,_)=>{if(string.IsNullOrWhiteSpace(name.Text))return;var p=new Playlist{Name=name.Text.Trim()};playlists.Add(p);SavePlaylists();list.SelectedItem=p;name.Clear();};
  var remove=new Button{Content="Удалить",Margin=new Thickness(8,0,0,0)};remove.Click+=(_,_)=>{if(list.SelectedItem is Playlist p){playlists.Remove(p);SavePlaylists();}};
  var play=new Button{Content="▶ Играть",Margin=new Thickness(8,0,0,0)};play.Click+=(_,_)=>{if(list.SelectedItem is Playlist p){queue=p.TrackIds.Where(id=>tracks.Any(t=>t.Id==id)).ToList();queueIndex=queue.Count>0?0:-1;if(queueIndex>=0)PlayCurrent();w.Close();}};
  actions.Children.Add(name);actions.Children.Add(add);actions.Children.Add(remove);actions.Children.Add(play);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);root.Children.Add(list);w.Content=root;w.ShowDialog();
 }
 private void Services_Click(object sender,RoutedEventArgs e)=>MessageBox.Show("Интеграции будут подключаться через официальные API и разрешённые механизмы воспроизведения.","SONORA");
 private void SetHeader(string title,string subtitle){GreetingText.Text=title;SubtitleText.Text=subtitle;}
}