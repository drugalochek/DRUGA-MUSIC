using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DrugaMusic.Services;

public sealed class FirebaseAuthService
{
    private const string ApiKey = "AIzaSyCgBtQNo-cebuAoJ7u9vkOGHHpHI0DxSWQ";
    private const string GoogleClientId = "951132244373-odclgbamf6ra65l8vljkkvhcqidqlthm.apps.googleusercontent.com";
    private const string AuthFileName = "firebase-session.bin";
    private static readonly HttpClient Http = new();
    private readonly string sessionPath;

    public string? UserId { get; private set; }
    public string? Email { get; private set; }
    public string? IdToken { get; private set; }
    public string? GooglePhotoUrl { get; private set; }
    public string? GoogleDisplayName { get; private set; }
    public bool HasPasswordProvider { get; private set; }
    public bool HasGoogleProvider { get; private set; }
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
            await LoadProviderInfoAsync();
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
        HasPasswordProvider = true;
        HasGoogleProvider = false;
        GooglePhotoUrl = null;
        GoogleDisplayName = null;
        await SaveSessionAsync();
        await LoadProviderInfoAsync();
    }

    public async Task SignInWithGoogleAsync()
    {
        var (code, redirectUri) = await GetGoogleAuthorizationCodeAsync();
        using var tokenResponse = await Http.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = GoogleClientId,
                ["code"] = code,
                ["code_verifier"] = _lastCodeVerifier!,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            }));
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync();
        if (!tokenResponse.IsSuccessStatusCode)
            throw new InvalidOperationException("Google не выдал токен: " + ReadGoogleError(tokenBody));

        using var googleJson = JsonDocument.Parse(tokenBody);
        if (!googleJson.RootElement.TryGetProperty("access_token", out var googleAccessTokenElement))
            throw new InvalidOperationException("Google не вернул токен доступа. Попробуй войти ещё раз.");
        var googleAccessToken = googleAccessTokenElement.GetString();
        if (string.IsNullOrWhiteSpace(googleAccessToken))
            throw new InvalidOperationException("Google вернул пустой токен доступа.");

        var postBody = $"access_token={Uri.EscapeDataString(googleAccessToken)}&providerId=google.com";
        using var firebaseResponse = await Http.PostAsJsonAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key={ApiKey}",
            new
            {
                postBody,
                requestUri = redirectUri,
                returnIdpCredential = true,
                returnSecureToken = true
            });
        var firebaseBody = await firebaseResponse.Content.ReadAsStringAsync();
        if (!firebaseResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(ParseError(firebaseBody));

        using var firebaseJson = JsonDocument.Parse(firebaseBody);
        var root = firebaseJson.RootElement;
        UserId = root.GetProperty("localId").GetString();
        Email = root.TryGetProperty("email", out var email) ? email.GetString() : null;
        IdToken = root.GetProperty("idToken").GetString();
        refreshToken = root.GetProperty("refreshToken").GetString();
        GooglePhotoUrl = root.TryGetProperty("photoUrl", out var photo) ? photo.GetString() : null;
        GoogleDisplayName = root.TryGetProperty("displayName", out var display) ? display.GetString() : null;
        await SaveSessionAsync();
        await LoadProviderInfoAsync();
    }

    public async Task LinkGoogleAsync()
    {
        if (!IsSignedIn)
            throw new InvalidOperationException("Сначала войди в аккаунт SONORA.");
        if (HasGoogleProvider)
            throw new InvalidOperationException("Google уже привязан к этому аккаунту.");

        var existingUserId = UserId;
        var (code, redirectUri) = await GetGoogleAuthorizationCodeAsync();
        using var tokenResponse = await Http.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = GoogleClientId,
                ["code"] = code,
                ["code_verifier"] = _lastCodeVerifier!,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            }));
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync();
        if (!tokenResponse.IsSuccessStatusCode)
            throw new InvalidOperationException("Google не выдал токен: " + ReadGoogleError(tokenBody));

        using var googleJson = JsonDocument.Parse(tokenBody);
        var googleAccessToken = googleJson.RootElement.TryGetProperty("access_token", out var accessToken)
            ? accessToken.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(googleAccessToken))
            throw new InvalidOperationException("Google не вернул токен доступа.");

        var postBody = $"access_token={Uri.EscapeDataString(googleAccessToken)}&providerId=google.com";
        using var firebaseResponse = await Http.PostAsJsonAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key={ApiKey}",
            new
            {
                idToken = await GetValidIdTokenAsync(),
                postBody,
                requestUri = redirectUri,
                returnIdpCredential = true,
                returnSecureToken = true
            });
        var firebaseBody = await firebaseResponse.Content.ReadAsStringAsync();
        if (!firebaseResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(ParseError(firebaseBody));

        using var firebaseJson = JsonDocument.Parse(firebaseBody);
        var root = firebaseJson.RootElement;
        var linkedUserId = root.GetProperty("localId").GetString();
        if (!string.Equals(existingUserId, linkedUserId, StringComparison.Ordinal))
            throw new InvalidOperationException("Firebase вернул другую учётную запись. Текущая сессия не была заменена.");

        IdToken = root.GetProperty("idToken").GetString();
        refreshToken = root.GetProperty("refreshToken").GetString();
        Email = root.TryGetProperty("email", out var email) ? email.GetString() : Email;
        GooglePhotoUrl = root.TryGetProperty("photoUrl", out var photo) ? photo.GetString() : GooglePhotoUrl;
        GoogleDisplayName = root.TryGetProperty("displayName", out var display) ? display.GetString() : GoogleDisplayName;
        await SaveSessionAsync();
        await LoadProviderInfoAsync();

        if (!HasGoogleProvider)
            throw new InvalidOperationException("Firebase не подтвердил привязку Google. Проверь список провайдеров аккаунта.");
    }

    private string? _lastCodeVerifier;

    private async Task<(string Code, string RedirectUri)> GetGoogleAuthorizationCodeAsync()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(64);
        _lastCodeVerifier = Base64Url(verifierBytes);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(_lastCodeVerifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));

        using var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();

        var redirectUri = $"http://127.0.0.1:{port}";
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri + "/");
        listener.Start();

        var query = new Dictionary<string, string>
        {
            ["client_id"] = GoogleClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["prompt"] = "select_account"
        };
        var authorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth?" +
            string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        try
        {
            Process.Start(new ProcessStartInfo(authorizationUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            listener.Stop();
            throw new InvalidOperationException("Не удалось открыть браузер для входа Google: " + ex.Message);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        HttpListenerContext context;
        try { context = await listener.GetContextAsync().WaitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            listener.Stop();
            throw new InvalidOperationException("Время ожидания входа Google истекло.");
        }

        var response = context.Response;
        var request = context.Request;
        var receivedState = request.QueryString["state"];
        var code = request.QueryString["code"];
        var error = request.QueryString["error"];
        var html = "<html><meta charset='utf-8'><body style='font-family:Arial;background:#080B12;color:white;text-align:center;padding:48px'><h2>SONORA</h2><p>Авторизация завершена. Можно вернуться в приложение.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
        listener.Stop();

        if (!string.Equals(receivedState, state, StringComparison.Ordinal))
            throw new InvalidOperationException("Не удалось подтвердить ответ Google. Повтори вход.");
        if (!string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException(error == "access_denied" ? "Вход через Google отменён." : "Google вернул ошибку: " + error);
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Google не вернул код авторизации.");
        return (code, redirectUri);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task LoadProviderInfoAsync()
    {
        if (string.IsNullOrWhiteSpace(IdToken)) return;
        using var response = await Http.PostAsJsonAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:lookup?key={ApiKey}",
            new { idToken = IdToken });
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return;
        using var json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("users", out var users) || users.GetArrayLength() == 0) return;
        var user = users[0];
        if (user.TryGetProperty("photoUrl", out var photo) && !string.IsNullOrWhiteSpace(photo.GetString()))
            GooglePhotoUrl = photo.GetString();
        if (user.TryGetProperty("displayName", out var display) && !string.IsNullOrWhiteSpace(display.GetString()))
            GoogleDisplayName = display.GetString();
        var providerList = user.TryGetProperty("providerUserInfo", out var providers)
            ? providers.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();
        HasPasswordProvider = providerList.Any(provider =>
            provider.TryGetProperty("providerId", out var providerId) &&
            providerId.GetString() == "password");
        HasGoogleProvider = providerList.Any(provider =>
            provider.TryGetProperty("providerId", out var providerId) &&
            providerId.GetString() == "google.com");
    }

    public async Task SetSonoraPasswordAsync(string password)
    {
        if (!IsSignedIn || string.IsNullOrWhiteSpace(Email))
            throw new InvalidOperationException("Сначала войди в аккаунт.");
        if (password.Length < 6)
            throw new InvalidOperationException("Пароль должен содержать минимум 6 символов.");

        using var response = await Http.PostAsJsonAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:update?key={ApiKey}",
            new { idToken = await GetValidIdTokenAsync(), email = Email, password, returnSecureToken = true });
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ParseError(body));
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        IdToken = root.GetProperty("idToken").GetString();
        refreshToken = root.GetProperty("refreshToken").GetString();
        HasPasswordProvider = true;
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
        GooglePhotoUrl = null;
        GoogleDisplayName = null;
        HasPasswordProvider = false;
        HasGoogleProvider = false;
    }

    private static string ReadGoogleError(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("error_description").GetString() ??
                   json.RootElement.GetProperty("error").GetString() ?? "Неизвестная ошибка.";
        }
        catch { return "Проверь OAuth Client ID и попробуй ещё раз."; }
    }

    private static string ParseError(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var code = json.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "UNKNOWN";
            return code switch
            {
                "EMAIL_EXISTS" or "ACCOUNT_EXISTS_WITH_DIFFERENT_CREDENTIAL" =>
                    "Этот Google-адрес уже используется другим способом входа. Сначала войди в существующий аккаунт SONORA; автоматическое объединение пока не выполнено.",
                "NEED_CONFIRMATION" => "Для этого адреса уже существует другой аккаунт. Нужно отдельно связать способы входа.",
                "INVALID_EMAIL" => "Проверь адрес электронной почты.",
                "WEAK_PASSWORD" => "Пароль слишком короткий. Нужно не менее 6 символов.",
                "EMAIL_NOT_FOUND" or "INVALID_PASSWORD" or "INVALID_LOGIN_CREDENTIALS" => "Неверная почта или пароль.",
                "USER_DISABLED" => "Этот аккаунт отключён.",
                "OPERATION_NOT_ALLOWED" => "В Firebase не включён нужный способ входа.",
                "INVALID_IDP_RESPONSE" => "Google не подтвердил авторизацию. Проверь настройки провайдера Google в Firebase.",
                "TOO_MANY_ATTEMPTS_TRY_LATER" => "Слишком много попыток. Попробуй позже.",
                _ => $"Firebase: {code}"
            };
        }
        catch { return "Не удалось связаться с Firebase. Проверь интернет и настройки проекта."; }
    }
}