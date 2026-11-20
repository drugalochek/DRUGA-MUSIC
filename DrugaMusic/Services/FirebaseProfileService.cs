using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace DrugaMusic.Services;

public sealed class FirebaseProfileService
{
    private const string DatabaseUrl = "https://sonora-ab012-default-rtdb.firebaseio.com";
    private static readonly HttpClient Http = new();

    public async Task<UserProfile?> ReadAsync(string userId, string idToken)
    {
        var url = $"{DatabaseUrl}/users/{Uri.EscapeDataString(userId)}/profile.json?auth={Uri.EscapeDataString(idToken)}";
        using var response = await Http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Не удалось загрузить профиль ({(int)response.StatusCode}): {body}");
        return string.IsNullOrWhiteSpace(body) || body == "null"
            ? null
            : JsonSerializer.Deserialize<UserProfile>(body);
    }

    // PUT replaces the complete profile node, so the previous avatar data is overwritten.
    public async Task WriteAsync(string userId, string idToken, UserProfile profile)
    {
        profile.UpdatedAtUtc = DateTime.UtcNow;
        var url = $"{DatabaseUrl}/users/{Uri.EscapeDataString(userId)}/profile.json?auth={Uri.EscapeDataString(idToken)}";
        using var response = await Http.PutAsJsonAsync(url, profile);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Не удалось сохранить профиль ({(int)response.StatusCode}): {body}");
    }
}

public sealed class UserProfile
{
    public string Nickname { get; set; } = "";
    public string AvatarType { get; set; } = "google";
    public string AvatarData { get; set; } = "";
    public string AvatarContentType { get; set; } = "";
    public string GooglePhotoUrl { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
