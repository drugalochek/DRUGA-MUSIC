using NAudio.Wave;
namespace DrugaMusic.Services;
public sealed class PlaybackService : IDisposable
{
    private WaveOutEvent? output;
    private AudioFileReader? reader;
    public event EventHandler? PlaybackStopped;
    public bool IsPlaying => output?.PlaybackState == PlaybackState.Playing;
    public TimeSpan Position => reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => reader?.TotalTime ?? TimeSpan.Zero;
    public void Play(string path)
    {
        Stop();
        reader = new AudioFileReader(path);
        output = new WaveOutEvent();
        output.PlaybackStopped += (_, _) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
        output.Init(reader);
        output.Play();
    }
    public void Pause()
    {
        if (output?.PlaybackState == PlaybackState.Playing) output.Pause();
        else if (output?.PlaybackState == PlaybackState.Paused) output.Play();
    }
    public void SetVolume(float volume) { if (reader is not null) reader.Volume = Math.Clamp(volume, 0f, 1f); }
    public void Stop() { output?.Stop(); output?.Dispose(); reader?.Dispose(); output = null; reader = null; }
    public void Dispose() => Stop();
}