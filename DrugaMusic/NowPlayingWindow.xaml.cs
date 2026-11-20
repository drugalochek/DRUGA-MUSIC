using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DrugaMusic.Models;
using DrugaMusic.Services;

namespace DrugaMusic;

public partial class NowPlayingWindow : Window
{
 private readonly PlaybackService playback;
 private readonly Func<Track?> previous;
 private readonly Func<Track?> next;
 private readonly Func<Track?> currentTrackProvider;
 private string? shownTrackId;
 private readonly Func<int,Track?> playQueueIndex;
 private readonly Action<float> volumeChanged;
 private readonly Func<int,int,Track?> moveQueueItem;
 private readonly Func<int,Track?> removeQueueItem;
 private readonly ObservableCollection<Track> queueItems;
 private readonly DispatcherTimer timer;
 private bool seeking;
 private bool updating;
 private bool changingQueue;

 public NowPlayingWindow(Track? current, PlaybackService playback, IReadOnlyList<Track> queue,
  int currentIndex, float initialVolume, Func<Track?> previous, Func<Track?> next, Func<Track?> currentTrackProvider, Func<int,Track?> playQueueIndex, Action<float> volumeChanged, Func<int,int,Track?> moveQueueItem, Func<int,Track?> removeQueueItem)
 {
  InitializeComponent();
  this.playback=playback;
  this.previous=previous;
  this.next=next;
  this.currentTrackProvider=currentTrackProvider;
  this.playQueueIndex=playQueueIndex;
  this.volumeChanged=volumeChanged;
  this.moveQueueItem=moveQueueItem;
  this.removeQueueItem=removeQueueItem;
  queueItems=new ObservableCollection<Track>(queue);
  QueueList.ItemsSource=queueItems;
  changingQueue=true;
  if(currentIndex>=0 && currentIndex<queue.Count) QueueList.SelectedIndex=currentIndex;
  changingQueue=false;
  ShowTrack(current);
  Volume.Value=initialVolume;
  timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
  timer.Tick+=(_,_)=>UpdatePosition();
  timer.Start();
  Closed+=(_,_)=>timer.Stop();
 }
 private void ShowTrack(Track? track)
 {
  if(track is null){shownTrackId=null;TitleText.Text="Ничего не воспроизводится";ArtistText.Text="";AlbumText.Text="";Cover.Source=null;return;}
  shownTrackId=track.Id;
  TitleText.Text=track.Title;
  ArtistText.Text=track.Artist;
  AlbumText.Text=string.IsNullOrWhiteSpace(track.Album)? "":track.Album;
  try {
   if(File.Exists(track.ArtworkPath)){
    var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=700;image.UriSource=new Uri(track.ArtworkPath);image.EndInit();image.Freeze();Cover.Source=image;
   } else Cover.Source=null;
  } catch { Cover.Source=null; }
  DurationText.Text=playback.Duration.ToString(@"m\:ss");
  PlayButton.Content=playback.IsPlaying?"Ⅱ":"▶";
 }
 private void UpdatePosition()
 {
  var active=currentTrackProvider();if(active?.Id!=shownTrackId)ShowTrack(active);
  var activeIndex=active is null?-1:queueItems.ToList().FindIndex(t=>t.Id==active.Id);
  if(activeIndex>=0&&QueueList.SelectedIndex!=activeIndex)
  {
   changingQueue=true;QueueList.SelectedIndex=activeIndex;changingQueue=false;
  }
  PositionText.Text=playback.Position.ToString(@"m\:ss");
  if(!seeking && playback.Duration>TimeSpan.Zero){
   updating=true;Seek.Maximum=Math.Max(1,playback.Duration.TotalSeconds);Seek.Value=Math.Clamp(playback.Position.TotalSeconds,0,Seek.Maximum);updating=false;
  }
  PlayButton.Content=playback.IsPlaying?"Ⅱ":"▶";
 }
 private void Seek_ValueChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(playback is not null&&!updating&&!seeking&&playback.Duration>TimeSpan.Zero)playback.Seek(TimeSpan.FromSeconds(e.NewValue));}
 private void Seek_MouseDown(object sender,MouseButtonEventArgs e)=>seeking=true;
 private void Seek_MouseUp(object sender,MouseButtonEventArgs e){seeking=false;if(playback.Duration>TimeSpan.Zero)playback.Seek(TimeSpan.FromSeconds(Seek.Value));}
 private void Volume_ValueChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(playback is not null)volumeChanged((float)e.NewValue);}
 private void Play_Click(object sender,RoutedEventArgs e){playback.Pause();PlayButton.Content=playback.IsPlaying?"Ⅱ":"▶";}
 private void Previous_Click(object sender,RoutedEventArgs e)=>ShowTrack(previous());
 private void Next_Click(object sender,RoutedEventArgs e)=>ShowTrack(next());
 private void QueueList_SelectionChanged(object sender,SelectionChangedEventArgs e){if(!changingQueue&&QueueList.SelectedIndex>=0)ShowTrack(playQueueIndex(QueueList.SelectedIndex));}
 private void MoveUp_Click(object sender,RoutedEventArgs e)=>MoveSelected(-1);
 private void MoveDown_Click(object sender,RoutedEventArgs e)=>MoveSelected(1);
 private void MoveSelected(int offset)
 {
  var from=QueueList.SelectedIndex;var to=from+offset;
  if(from<0||to<0||to>=queueItems.Count)return;
  var current=moveQueueItem(from,to);
  queueItems.Move(from,to);
  changingQueue=true;QueueList.SelectedIndex=to;changingQueue=false;
  ShowTrack(current);
 }
 private void Remove_Click(object sender,RoutedEventArgs e)
 {
  var index=QueueList.SelectedIndex;if(index<0||index>=queueItems.Count)return;
  var current=removeQueueItem(index);
  queueItems.RemoveAt(index);
  if(queueItems.Count>0){var selected=Math.Min(index,queueItems.Count-1);changingQueue=true;QueueList.SelectedIndex=selected;changingQueue=false;}
  ShowTrack(current);
 }
}