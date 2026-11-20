using NAudio.Wave;
namespace DrugaMusic.Services;
public sealed class PlaybackService : IDisposable
{
 private WaveOutEvent? output;
 private AudioFileReader? reader;
 public event EventHandler? TrackEnded;
 public bool IsPlaying => output?.PlaybackState == PlaybackState.Playing;
 public TimeSpan Position => reader?.CurrentTime ?? TimeSpan.Zero;
 public TimeSpan Duration => reader?.TotalTime ?? TimeSpan.Zero;
 public void Play(string path)
 {
  Stop();
  var nextReader = new AudioFileReader(path);
  var nextOutput = new WaveOutEvent();
  nextOutput.PlaybackStopped += (_, _) =>
  {
   if (ReferenceEquals(reader, nextReader) && nextReader.TotalTime > TimeSpan.Zero &&
       nextReader.CurrentTime >= nextReader.TotalTime - TimeSpan.FromMilliseconds(350))
    TrackEnded?.Invoke(this, EventArgs.Empty);
  };
  reader = nextReader; output = nextOutput;
  try { output.Init(reader); output.Play(); }
  catch { Stop(); throw; }
 }
 public void Pause()
 {
  if (output?.PlaybackState == PlaybackState.Playing) output.Pause();
  else if (output?.PlaybackState == PlaybackState.Paused) output.Play();
 }
 public void SetVolume(float volume) { if (reader is not null) reader.Volume = Math.Clamp(volume, 0f, 1f); }
 public void Seek(TimeSpan position)
 {
  if (reader is null) return;
  reader.CurrentTime = position < TimeSpan.Zero ? TimeSpan.Zero : position > reader.TotalTime ? reader.TotalTime : position;
 }
 public void Stop()
 {
  var oldOutput = output; var oldReader = reader; output = null; reader = null;
  if (oldOutput is not null) { oldOutput.Stop(); oldOutput.Dispose(); }
  oldReader?.Dispose();
 }
 public void Dispose() => Stop();
}