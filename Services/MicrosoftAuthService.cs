using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TVLauncher.Services;

/// <summary>
/// Autenticación Microsoft para Minecraft.
/// Flujo principal: Authorization Code + PKCE con redirect a un servidor HTTP local
/// (loopback) y apertura del navegador del sistema. Si el proveedor rechaza el
/// redirect local, se usa automáticamente Device Code Flow como respaldo.
/// Después: Microsoft OAuth -> Xbox Live (XBL) -> XSTS -> Minecraft /login_with_xbox.
/// </summary>
public class MicrosoftAuthService
{
    // Client ID v2 (Azure-AD GUID) de una app pública registrada. Los IDs v1 de
    // 16 hex (p. ej. el antiguo de Mojang "00000000402b5328") son rechazados por
    // el endpoint v2 con AADSTS70001 -> HTTP 400.
    private const string DefaultClientId = "c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb";
    private const string Scope = "XboxLive.signin offline_access";

    private const string AuthorizeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
    private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string XblUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string MinecraftAuthUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string MinecraftProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

    /// <summary>Puerto preferido para el redirect loopback (coincide con apps conocidas).</summary>
    private const int PreferredLoopbackPort = 1337;

    private readonly ConfigService? _configService;

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public MicrosoftAuthService(ConfigService? configService = null)
    {
        _configService = configService;
    }

    private string ClientId
    {
        get
        {
            string? configured = _configService?.Config.MicrosoftClientId;
            return string.IsNullOrWhiteSpace(configured) ? DefaultClientId : configured.Trim();
        }
    }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<DeviceCodeInfo>? DeviceCodeReceived;

    public async Task<MicrosoftAuthResult> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        MicrosoftTokenResponse msToken;

        try
        {
            // 1-2. Authorization Code Flow con servidor local + navegador del sistema
            StatusChanged?.Invoke(this, "Abriendo el navegador para iniciar sesión con Microsoft...");
            msToken = await AuthenticateWithLocalServerAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"[MSAuth] Authorization Code Flow no disponible: {ex.Message}");
            StatusChanged?.Invoke(this, "Cambiando al flujo de código de dispositivo...");

            // 1-2 (respaldo). Device Code Flow
            StatusChanged?.Invoke(this, "Solicitando código de dispositivo de Microsoft...");
            var deviceCode = await RequestDeviceCodeAsync(cancellationToken);

            DeviceCodeReceived?.Invoke(this, new DeviceCodeInfo
            {
                UserCode = deviceCode.UserCode,
                VerificationUri = deviceCode.VerificationUri,
                Message = deviceCode.Message
            });
            StatusChanged?.Invoke(this, "Abre el enlace en tu navegador e introduce el código");

            msToken = await PollForTokenAsync(deviceCode, cancellationToken);
        }

        return await CompleteLoginAsync(msToken, cancellationToken);
    }

    /// <summary>
    /// Levanta un servidor HTTP temporal en localhost, abre el navegador en la página
    /// de autorización de Microsoft con PKCE y captura el <c>code</c> de la redirección.
    /// </summary>
    private async Task<MicrosoftTokenResponse> AuthenticateWithLocalServerAsync(CancellationToken cancellationToken)
    {
        using var listener = CreateLoopbackListener(out int port);
        string redirectUri = $"http://localhost:{port}/";

        string state = CreateRandomToken(32);
        string codeVerifier = CreateRandomToken(64);
        string codeChallenge = CreateCodeChallenge(codeVerifier);

        string authorizeUrl = AuthorizeUrl +
            $"?client_id={Uri.EscapeDataString(ClientId)}" +
            "&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            "&response_mode=query" +
            $"&scope={Uri.EscapeDataString(Scope)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
            "&code_challenge_method=S256" +
            "&prompt=select_account";

        Log.Info($"[MSAuth] Authorization Code Flow en {redirectUri}");
        StatusChanged?.Invoke(this, "Esperando la autorización en el navegador...");
        OpenBrowser(authorizeUrl);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));

        // El navegador puede abrir una preconexión que nunca envía datos: se descarta
        // tras unos segundos para no bloquear la petición real de redirección.
        var idleConnectionTimeout = TimeSpan.FromSeconds(10);

        string? code = null;
        string? error = null;
        string? errorDescription = null;

        while (code == null && error == null)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("La autenticación con Microsoft no se completó a tiempo.");
            }

            using (client)
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);

                string? requestLine;
                try
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    idle.CancelAfter(idleConnectionTimeout);

                    requestLine = await reader.ReadLineAsync(idle.Token);
                    // Consumir cabeceras hasta la línea vacía.
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(idle.Token))) { }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    throw new TimeoutException("La autenticación con Microsoft no se completó a tiempo.");
                }
                catch (OperationCanceledException)
                {
                    continue; // conexión inactiva (preconexión del navegador)
                }

                if (string.IsNullOrWhiteSpace(requestLine))
                    continue;

                var requestParts = requestLine.Split(' ');
                if (requestParts.Length < 2)
                    continue;

                var query = ParseQueryString(requestParts[1]);

                if (query.TryGetValue("code", out string? returnedCode))
                {
                    code = returnedCode;
                }
                else if (query.TryGetValue("error", out string? returnedError))
                {
                    error = returnedError;
                    query.TryGetValue("error_description", out errorDescription);
                }
                else
                {
                    // favicon.ico u otras peticiones del navegador
                    await WriteHttpResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8",
                        "Not found", timeout.Token);
                    continue;
                }

                if (error != null)
                {
                    await WriteHttpResponseAsync(stream, 400, "Bad Request", "text/html; charset=utf-8",
                        BuildResultPage("No se pudo iniciar sesión", errorDescription ?? error), timeout.Token);
                    throw new InvalidOperationException($"Microsoft devolvió un error: {errorDescription ?? error}");
                }

                if (!query.TryGetValue("state", out string? returnedState) ||
                    !string.Equals(returnedState, state, StringComparison.Ordinal))
                {
                    await WriteHttpResponseAsync(stream, 400, "Bad Request", "text/html; charset=utf-8",
                        BuildResultPage("Solicitud inválida", "El estado OAuth no coincide. Inténtalo de nuevo."), timeout.Token);
                    throw new InvalidOperationException("El estado OAuth devuelto no coincide (posible CSRF).");
                }

                // Página de confirmación; intenta cerrar la pestaña automáticamente.
                await WriteHttpResponseAsync(stream, 200, "OK", "text/html; charset=utf-8",
                    BuildResultPage("Sesión iniciada", "Ya puedes cerrar esta pestaña y volver a TVLauncher."), timeout.Token);
            }
        }

        if (code == null)
            throw new InvalidOperationException($"No se recibió el código de autorización: {errorDescription ?? error}");

        return await ExchangeCodeAsync(code, codeVerifier, redirectUri, cancellationToken);
    }

    private async Task<MicrosoftTokenResponse> ExchangeCodeAsync(string code, string codeVerifier,
        string redirectUri, CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", ClientId),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
            new KeyValuePair<string, string>("code_verifier", codeVerifier),
            new KeyValuePair<string, string>("scope", Scope)
        });

        var response = await _http.PostAsync(TokenUrl, content, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        Log.Info($"[MSAuth] Token (authorization_code) -> HTTP {(int)response.StatusCode}: {Truncate(json)}");

        if (!response.IsSuccessStatusCode)
        {
            var error = JsonSerializer.Deserialize<MicrosoftTokenError>(json);
            throw new Exception(
                $"Error al canjear el código ({(int)response.StatusCode}): {error?.ErrorDescription ?? error?.Error ?? json}");
        }

        return JsonSerializer.Deserialize<MicrosoftTokenResponse>(json)
               ?? throw new Exception("Error al obtener el token de Microsoft: respuesta vacía");
    }

    private async Task<MicrosoftAuthResult> CompleteLoginAsync(MicrosoftTokenResponse msToken,
        CancellationToken cancellationToken)
    {
        // 3. Xbox Live
        StatusChanged?.Invoke(this, "Autenticando con Xbox Live...");
        var xblToken = await AuthenticateXboxLiveAsync(msToken.AccessToken, cancellationToken);

        // 4. XSTS
        StatusChanged?.Invoke(this, "Obteniendo token XSTS...");
        var xstsToken = await AuthenticateXSTSAsync(xblToken.Token, cancellationToken);

        // 5. Minecraft
        StatusChanged?.Invoke(this, "Iniciando sesión en Minecraft...");
        var minecraftAuth = await AuthenticateMinecraftAsync(
            xblToken.DisplayClaims.Xui[0].Uhs, xstsToken.Token, cancellationToken);

        // 6. Perfil
        StatusChanged?.Invoke(this, "Obteniendo perfil de Minecraft...");
        var profile = await GetMinecraftProfileAsync(minecraftAuth.AccessToken, cancellationToken);

        return new MicrosoftAuthResult
        {
            Username = profile.Name,
            Uuid = profile.Id,
            AccessToken = minecraftAuth.AccessToken,
            RefreshToken = msToken.RefreshToken ?? string.Empty
        };
    }

    // ---------- Servidor loopback y utilidades OAuth ----------

    private static LoopbackListener CreateLoopbackListener(out int port)
    {
        // Preferir el puerto clásico de las apps de Minecraft; si está ocupado,
        // usar uno efímero (Entra ID admite loopback con puerto dinámico).
        foreach (int candidate in new[] { PreferredLoopbackPort, 0 })
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, candidate);
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
                return new LoopbackListener(listener, port);
            }
            catch (SocketException) when (candidate != 0)
            {
                // Puerto ocupado: probar con uno efímero
            }
        }

        throw new InvalidOperationException("No se pudo abrir un puerto local para el login de Microsoft.");
    }

    /// <summary>Envoltorio descartable para detener el listener al terminar el login.</summary>
    private sealed class LoopbackListener : IDisposable
    {
        private readonly TcpListener _listener;

        public LoopbackListener(TcpListener listener, int port)
        {
            _listener = listener;
            Port = port;
        }

        public int Port { get; }

        public Task<TcpClient> AcceptTcpClientAsync(CancellationToken cancellationToken)
            => _listener.AcceptTcpClientAsync(cancellationToken).AsTask();

        public void Dispose() => _listener.Stop();
    }

    private static Dictionary<string, string> ParseQueryString(string url)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int queryIndex = url.IndexOf('?');
        if (queryIndex < 0 || queryIndex == url.Length - 1)
            return result;

        foreach (string pair in url[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            string value = parts.Length > 1
                ? Uri.UnescapeDataString(parts[1].Replace('+', ' '))
                : string.Empty;

            if (key.Length > 0)
                result[key] = value;
        }

        return result;
    }

    private static async Task WriteHttpResponseAsync(NetworkStream stream, int statusCode, string reason,
        string contentType, string body, CancellationToken cancellationToken)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string headers =
            $"HTTP/1.1 {statusCode} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n";

        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static string BuildResultPage(string title, string message) => $$"""
        <!DOCTYPE html>
        <html lang="es">
        <head>
          <meta charset="utf-8">
          <title>{{WebUtility.HtmlEncode(title)}} - TVLauncher</title>
          <style>
            :root { color-scheme: dark; }
            body { margin: 0; min-height: 100vh; display: grid; place-items: center;
                   background: #121212; color: #f5f5f5; font-family: 'Segoe UI', system-ui, sans-serif; }
            .card { background: #1e1e1e; border: 1px solid #3f3f3f; border-radius: 12px;
                    padding: 32px 40px; max-width: 460px; text-align: center; }
            h1 { font-size: 20px; margin: 0 0 12px; }
            p { color: #b0b0b0; line-height: 1.5; margin: 0; }
            .dot { width: 10px; height: 10px; border-radius: 50%; background: #107c10;
                   display: inline-block; margin-right: 8px; }
          </style>
        </head>
        <body>
          <div class="card">
            <h1><span class="dot"></span>{{WebUtility.HtmlEncode(title)}}</h1>
            <p>{{WebUtility.HtmlEncode(message)}}</p>
          </div>
          <script>setTimeout(function () { window.close(); }, 1500);</script>
        </body>
        </html>
        """;

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"No se pudo abrir el navegador predeterminado: {ex.Message}", ex);
        }
    }

    private static string CreateRandomToken(int byteLength)
        => Base64UrlEncode(RandomNumberGenerator.GetBytes(byteLength));

    private static string CreateCodeChallenge(string verifier)
        => Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<DeviceCodeResponse> RequestDeviceCodeAsync(CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", ClientId),
            new KeyValuePair<string, string>("scope", Scope)
        });

        var response = await _http.PostAsync(DeviceCodeUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(
                $"No se pudo iniciar el Device Code Flow ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonSerializer.Deserialize<DeviceCodeResponse>(json);

        return doc ?? throw new Exception("Error al obtener código de dispositivo");
    }

    private async Task<MicrosoftTokenResponse> PollForTokenAsync(DeviceCodeResponse deviceCode, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(deviceCode.Interval);
        var expiresAt = DateTime.UtcNow.AddSeconds(deviceCode.ExpiresIn);

        while (DateTime.UtcNow < expiresAt && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, cancellationToken);

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("device_code", deviceCode.DeviceCode)
            });

            var response = await _http.PostAsync(TokenUrl, content, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            Log.Info($"[MSAuth] Token poll -> HTTP {(int)response.StatusCode}: {json}");

            if (response.IsSuccessStatusCode)
            {
                var token = JsonSerializer.Deserialize<MicrosoftTokenResponse>(json);
                return token ?? throw new Exception("Error al obtener token de Microsoft");
            }

            var error = JsonSerializer.Deserialize<MicrosoftTokenError>(json);
            if (error?.Error == "authorization_pending")
                continue;
            if (error?.Error == "slow_down")
            {
                interval = interval.Add(TimeSpan.FromSeconds(5));
                continue;
            }

            throw new Exception($"Error de autenticación Microsoft ({(int)response.StatusCode}): {error?.ErrorDescription ?? error?.Error ?? json}");
        }

        throw new Exception("Tiempo de autenticación agotado");
    }

    private async Task<XblResponse> AuthenticateXboxLiveAsync(string accessToken, CancellationToken cancellationToken)
    {
        var request = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={accessToken}"
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(XblUrl, content, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        Log.Info($"[MSAuth] XBL -> HTTP {(int)response.StatusCode}: {Truncate(json)}");

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Error Xbox Live ({(int)response.StatusCode}): {json}");

        var result = JsonSerializer.Deserialize<XblResponse>(json);
        return result ?? throw new Exception("Error al autenticar con Xbox Live: respuesta vacía");
    }

    private async Task<XstsResponse> AuthenticateXSTSAsync(string xblToken, CancellationToken cancellationToken)
    {
        var request = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xblToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(XstsUrl, content, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        Log.Info($"[MSAuth] XSTS -> HTTP {(int)response.StatusCode}: {Truncate(json)}");

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Error XSTS ({(int)response.StatusCode}): {json}");

        var result = JsonSerializer.Deserialize<XstsResponse>(json);
        return result ?? throw new Exception("Error al autenticar con XSTS: respuesta vacía");
    }

    private async Task<MinecraftAuthResponse> AuthenticateMinecraftAsync(string uhs, string xstsToken, CancellationToken cancellationToken)
    {
        var request = new
        {
            identityToken = $"XBL3.0 x={uhs};{xstsToken}"
        };

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(MinecraftAuthUrl, content, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        Log.Info($"[MSAuth] Minecraft -> HTTP {(int)response.StatusCode}: {Truncate(json)}");

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Error autenticándose con Minecraft ({(int)response.StatusCode}): {json}");

        var result = JsonSerializer.Deserialize<MinecraftAuthResponse>(json);
        return result ?? throw new Exception("Error al autenticar con Minecraft Services: respuesta vacía");
    }

    private static string Truncate(string value) => value.Length > 300 ? value[..300] + "..." : value;

    private async Task<MinecraftProfile> GetMinecraftProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _http.GetAsync(MinecraftProfileUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Error obteniendo perfil de Minecraft ({(int)response.StatusCode}). ¿La cuenta posee Minecraft Java Edition?");

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<MinecraftProfile>(json);

        return result ?? throw new Exception("Error al obtener perfil de Minecraft: respuesta vacía");
    }
}

public class MicrosoftAuthResult
{
    public string Username { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}

public class DeviceCodeInfo
{
    public string UserCode { get; set; } = string.Empty;
    public string VerificationUri { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

#region DTOs
public class DeviceCodeResponse
{
    [JsonPropertyName("device_code")]
    public string DeviceCode { get; set; } = string.Empty;
    
    [JsonPropertyName("user_code")]
    public string UserCode { get; set; } = string.Empty;
    
    [JsonPropertyName("verification_uri")]
    public string VerificationUri { get; set; } = string.Empty;
    
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
    
    [JsonPropertyName("interval")]
    public int Interval { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public class MicrosoftTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;
    
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;
}

public class MicrosoftTokenError
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;
    
    [JsonPropertyName("error_description")]
    public string ErrorDescription { get; set; } = string.Empty;
}

public class XblResponse
{
    [JsonPropertyName("Token")]
    public string Token { get; set; } = string.Empty;
    
    [JsonPropertyName("DisplayClaims")]
    public DisplayClaims DisplayClaims { get; set; } = new();
}

public class DisplayClaims
{
    [JsonPropertyName("xui")]
    public Xui[] Xui { get; set; } = Array.Empty<Xui>();
}

public class Xui
{
    [JsonPropertyName("uhs")]
    public string Uhs { get; set; } = string.Empty;
}

public class XstsResponse
{
    [JsonPropertyName("Token")]
    public string Token { get; set; } = string.Empty;
}

public class MinecraftAuthResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;
}

public class MinecraftProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
#endregion
