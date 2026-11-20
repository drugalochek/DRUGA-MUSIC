using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using DrugaMusic.Models;

namespace DrugaMusic.Services;

public sealed class FirebaseDatabaseService
{
    private const string DatabaseUrl = "https://sonora-ab012-default-rtdb.firebaseio.com";
    private static readonly HttpClient Http = new();

    public async Task<CloudLibrarySnapshot?> ReadAsync(string userId, string idToken)
    {
        var url = $"{DatabaseUrl}/users/{Uri.EscapeDataString(userId)}/sonora.json?auth={Uri.EscapeDataString(idToken)}";
        using var response = await Http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ошибка чтения облачных данных ({(int)response.StatusCode}): {body}");
        if (string.IsNullOrWhiteSpace(body) || body == "null") return null;
        return JsonSerializer.Deserialize<CloudLibrarySnapshot>(body);
    }

    public async Task WriteAsync(string userId, string idToken, CloudLibrarySnapshot snapshot)
    {
        var url = $"{DatabaseUrl}/users/{Uri.EscapeDataString(userId)}/sonora.json?auth={Uri.EscapeDataString(idToken)}";
        using var response = await Http.PutAsJsonAsync(url, snapshot);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ошибка записи облачных данных ({(int)response.StatusCode}): {body}");
    }
}

public sealed class CloudLibrarySnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<string> FavoriteTrackKeys { get; set; } = new();
    public List<CloudPlaylist> Playlists { get; set; } = new();
    public double Volume { get; set; } = 0.75;
    public int RepeatMode { get; set; }
    public bool Shuffle { get; set; }
}

public sealed class CloudPlaylist
{
    public string Name { get; set; } = "Новый плейлист";
    public List<string> TrackKeys { get; set; } = new();
}
