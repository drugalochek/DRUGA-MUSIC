using System.IO;
using System.Text.Json;
using DrugaMusic.Models;

namespace DrugaMusic.Services;

public sealed class ExternalLibraryStore
{
    private readonly string dataDirectory;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

    public ExternalLibraryStore(string dataDirectory) => this.dataDirectory = dataDirectory;

    private string TracksFile => Path.Combine(dataDirectory, "external-tracks.json");
    private string PlaylistsFile => Path.Combine(dataDirectory, "external-playlists.json");

    public List<ExternalTrack> LoadTracks() => Load<ExternalTrack>(TracksFile);
    public List<ExternalPlaylist> LoadPlaylists() => Load<ExternalPlaylist>(PlaylistsFile);
    public void SaveTracks(IEnumerable<ExternalTrack> tracks) => Save(TracksFile, tracks);
    public void SavePlaylists(IEnumerable<ExternalPlaylist> playlists) => Save(PlaylistsFile, playlists);

    public static string CreateMatchKey(string artist, string title)
    {
        static string Normalize(string value) =>
            string.Join(' ', (value ?? "").Trim().ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Normalize(artist) + "|" + Normalize(title);
    }

    public static ExternalTrack? FindMatch(IEnumerable<ExternalTrack> tracks, string provider,
        string providerTrackId, string artist, string title, string isrc = "")
    {
        var items = tracks.ToList();
        if (!string.IsNullOrWhiteSpace(providerTrackId))
        {
            var byProviderId = items.FirstOrDefault(track =>
                track.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) &&
                track.ProviderTrackId.Equals(providerTrackId, StringComparison.OrdinalIgnoreCase));
            if (byProviderId is not null) return byProviderId;
        }

        if (!string.IsNullOrWhiteSpace(isrc))
        {
            var byIsrc = items.FirstOrDefault(track =>
                track.Isrc.Equals(isrc, StringComparison.OrdinalIgnoreCase));
            if (byIsrc is not null) return byIsrc;
        }

        var key = CreateMatchKey(artist, title);
        return items.FirstOrDefault(track => CreateMatchKey(track.Artist, track.Title) == key);
    }

    private List<T> Load<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return new List<T>();
            return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path)) ?? new List<T>();
        }
        catch (JsonException)
        {
            var backup = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            try { File.Move(path, backup, true); } catch { }
            return new List<T>();
        }
        catch (IOException) { return new List<T>(); }
    }

    private void Save<T>(string path, IEnumerable<T> items)
    {
        Directory.CreateDirectory(dataDirectory);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(items, jsonOptions));
        File.Move(temporaryPath, path, true);
    }
}
