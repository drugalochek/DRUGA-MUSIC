using System;
using System.Collections.Generic;
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
 private readonly Func<int,Track?> playQueueIndex;
 private readonly DispatcherTimer timer;
 private bool seeking;
 private bool updating;
 private bool changingQueue;

 public NowPlayingWindow(Track? current, PlaybackService playback, IReadOnlyList<Track> queue,
  int currentIndex, Func<Track?> previous, Func<Track?> next, Func<int,Track?> playQueueIndex)
 {
  InitializeComponent();
  this.playback=playback;
  this.previous=previous;
  this.next=next;
  this.playQueueIndex=playQueueIndex;
  QueueList.ItemsSource=queue;
  changingQueue=true;
  if(currentIndex>=0 && currentIndex<queue.Count) QueueList.SelectedIndex=currentIndex;
  changingQueue=false;
  ShowTrack(current);
  Volume.Value=0.75;
  timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
  timer.Tick+=(_,_)=>UpdatePosition();
  timer.Start();
  Closed+=(_,_)=>timer.Stop();
 }
 private void ShowTrack(Track? track)
 {
  if(track is null)return;
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
  PositionText.Text=playback.Position.ToString(@"m\:ss");
  if(!seeking && playback.Duration>TimeSpan.Zero){
   updating=true;Seek.Maximum=Math.Max(1,playback.Duration.TotalSeconds);Seek.Value=Math.Clamp(playback.Position.TotalSeconds,0,Seek.Maximum);updating=false;
  }
  PlayButton.Content=playback.IsPlaying?"Ⅱ":"▶";
 }
 private void Seek_ValueChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(!updating&&!seeking&&playback.Duration>TimeSpan.Zero)playback.Seek(TimeSpan.FromSeconds(e.NewValue));}
 private void Seek_MouseDown(object sender,MouseButtonEventArgs e)=>seeking=true;
 private void Seek_MouseUp(object sender,MouseButtonEventArgs e){seeking=false;if(playback.Duration>TimeSpan.Zero)playback.Seek(TimeSpan.FromSeconds(Seek.Value));}
 private void Volume_ValueChanged(object sender,RoutedPropertyChangedEventArgs<double> e)=>playback.SetVolume((float)e.NewValue);
 private void Play_Click(object sender,RoutedEventArgs e){playback.Pause();PlayButton.Content=playback.IsPlaying?"Ⅱ":"▶";}
 private void Previous_Click(object sender,RoutedEventArgs e)=>ShowTrack(previous());
 private void Next_Click(object sender,RoutedEventArgs e)=>ShowTrack(next());
 private void QueueList_SelectionChanged(object sender,SelectionChangedEventArgs e){if(!changingQueue&&QueueList.SelectedIndex>=0)ShowTrack(playQueueIndex(QueueList.SelectedIndex));}
}