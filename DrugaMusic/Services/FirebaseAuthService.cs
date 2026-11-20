using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DrugaMusic.Services;

public sealed class FirebaseAuthService
{
    private const string ApiKey = "AIzaSyCgBtQNo-cebuAoJ7u9vkOGHHpHI0DxSWQ";
    private const string AuthFileName = "firebase-session.bin";
    private static readonly HttpClient Http = new();
    private readonly string sessionPath;

    public string? UserId { get; private set; }
    public string? Email { get; private set; }
    public string? IdToken { get; private set; }
    private string? refreshToken;

    public bool IsSignedIn => !string.IsNullOrWhiteSpace(IdToken);

    public FirebaseAuthService(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        sessionPath = Path.Combine(dataDirectory, AuthFileName);
    }

    public async Task<bool> RestoreSessionAsync()
    {
        try
        {
            if (!File.Exists(sessionPath)) return false;
            var protectedBytes = await File.ReadAllBytesAsync(sessionPath);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            refreshToken = Encoding.UTF8.GetString(bytes);
            await RefreshTokenAsync();
            return IsSignedIn;
        }
        catch
        {
            ClearSession();
            return false;
        }
    }

    public Task SignInAsync(string email, string password) =>
        AuthenticateAsync("signInWithPassword", email, password);

    public Task RegisterAsync(string email, string password) =>
        AuthenticateAsync("signUp", email, password);

    private async Task AuthenticateAsync(string method, string email, string password)
    {
        var url = $"https://identitytoolkit.googleapis.com/v1/accounts:{method}?key={ApiKey}";
        using var response = await Http.PostAsJsonAsync(url, new
        {
            email,
            password,
            returnSecureToken = true
        });
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ParseError(body));

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        UserId = root.GetProperty("localId").GetString();
        Email = root.GetProperty("email").GetString();
        IdToken = root.GetProperty("idToken").GetString();
        refreshToken = root.GetProperty("refreshToken").GetString();
        await SaveSessionAsync();
    }

    private async Task RefreshTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("Сессия отсутствует. Войди в аккаунт заново.");

        using var response = await Http.PostAsync(
            $"https://securetoken.googleapis.com/v1/token?key={ApiKey}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            }));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ParseError(body));

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        IdToken = root.GetProperty("id_token").GetString();
        refreshToken = root.GetProperty("refresh_token").GetString();
        UserId = root.GetProperty("user_id").GetString();
        await SaveSessionAsync();
    }

    private async Task SaveSessionAsync()
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var bytes = Encoding.UTF8.GetBytes(refreshToken);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(sessionPath, protectedBytes);
    }

    public async Task<string> GetValidIdTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(IdToken)) await RefreshTokenAsync();
        return IdToken!;
    }

    public void SignOut()
    {
        ClearSession();
        try { if (File.Exists(sessionPath)) File.Delete(sessionPath); } catch { }
    }

    private void ClearSession()
    {
        UserId = null;
        Email = null;
        IdToken = null;
        refreshToken = null;
    }

    private static string ParseError(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var code = json.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "UNKNOWN";
            return code switch
            {
                "EMAIL_EXISTS" => "Этот адрес уже зарегистрирован.",
                "INVALID_EMAIL" => "Проверь адрес электронной почты.",
                "WEAK_PASSWORD" => "Пароль слишком короткий. Нужно не менее 6 символов.",
                "EMAIL_NOT_FOUND" or "INVALID_PASSWORD" or "INVALID_LOGIN_CREDENTIALS" => "Неверная почта или пароль.",
                "USER_DISABLED" => "Этот аккаунт отключён.",
                "OPERATION_NOT_ALLOWED" => "В Firebase не включён способ входа Email/Password.",
                "TOO_MANY_ATTEMPTS_TRY_LATER" => "Слишком много попыток. Попробуй позже.",
                _ => $"Firebase: {code}"
            };
        }
        catch { return "Не удалось связаться с Firebase. Проверь интернет и настройки проекта."; }
    }
}
