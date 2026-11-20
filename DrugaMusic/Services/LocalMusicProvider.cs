using DrugaMusic.Models;
namespace DrugaMusic.Services;
public sealed class LocalMusicProvider : IMusicProvider
{
    private static readonly string[] SupportedExtensions = [".mp3", ".wav", ".aiff", ".aif", ".wma", ".aac", ".m4a", ".flac"];
    public string Id => "local";
    public string DisplayName => "Local files";
    public Task<IReadOnlyList<Track>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Track>>(Array.Empty<Track>());
    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}