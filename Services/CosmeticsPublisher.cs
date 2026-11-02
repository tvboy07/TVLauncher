using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TVLauncher.Models;

namespace TVLauncher.Services;

/// <summary>
/// Publica los cosméticos equipados en la Edge Function
/// <c>publish-cosmetics</c> de Supabase.
///
/// La <c>apikey</c> enviada es la clave anon pública; la autorización es el
/// token de Minecraft de la cuenta activa, que la función valida contra Mojang
/// antes de escribir. El token nunca se registra en logs.
///
/// Ningún fallo de red debe bloquear el lanzamiento del juego: todos los
/// caminos de error se convierten en <see cref="PublishCosmeticsStatus.Failed"/>.
/// </summary>
public sealed class CosmeticsPublisher : ICosmeticsPublisher
{
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);

    private readonly ConfigService _configService;
    private readonly AuthService _authService;
    private readonly HttpClient _httpClient;

    public CosmeticsPublisher(ConfigService configService, AuthService authService)
        : this(configService, authService, new HttpClient { Timeout = PublishTimeout })
    {
    }

    internal CosmeticsPublisher(
        ConfigService configService,
        AuthService authService,
        HttpClient httpClient)
    {
        _configService = configService;
        _authService = authService;
        _httpClient = httpClient;
    }

    public async Task<PublishCosmeticsResult> PublishCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        var account = _authService.ActiveAccount;
        if (account is null || account.Type != AccountType.Microsoft)
            return new PublishCosmeticsResult(PublishCosmeticsStatus.Skipped);

        var config = _configService.Config;
        if (string.IsNullOrWhiteSpace(config.CosmeticsSupabaseUrl)
            || string.IsNullOrWhiteSpace(config.CosmeticsSupabaseAnonKey))
        {
            return new PublishCosmeticsResult(PublishCosmeticsStatus.Skipped);
        }

        if (string.IsNullOrWhiteSpace(account.AccessToken))
            return new PublishCosmeticsResult(PublishCosmeticsStatus.Skipped);

        return await PublishAsync(
            config.EquippedCapeId,
            config.EquippedHatId,
            account.AccessToken,
            cancellationToken);
    }

    internal async Task<PublishCosmeticsResult> PublishAsync(
        string? capeId,
        string? hatId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var config = _configService.Config;
        if (string.IsNullOrWhiteSpace(config.CosmeticsSupabaseUrl)
            || string.IsNullOrWhiteSpace(config.CosmeticsSupabaseAnonKey))
        {
            return new PublishCosmeticsResult(PublishCosmeticsStatus.Skipped);
        }

        string endpoint = config.CosmeticsSupabaseUrl.TrimEnd('/')
            + "/functions/v1/publish-cosmetics";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.TryAddWithoutValidation("apikey", config.CosmeticsSupabaseAnonKey);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
            request.Content = new StringContent(
                JsonConvert.SerializeObject(new { cape_id = capeId, hat_id = hatId }),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
                return new PublishCosmeticsResult(PublishCosmeticsStatus.Published);

            return new PublishCosmeticsResult(
                PublishCosmeticsStatus.Failed,
                (int)response.StatusCode,
                $"Supabase respondió {(int)response.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            return new PublishCosmeticsResult(
                PublishCosmeticsStatus.Failed,
                null,
                "La publicación tardó demasiado");
        }
        catch (HttpRequestException ex)
        {
            return new PublishCosmeticsResult(
                PublishCosmeticsStatus.Failed,
                null,
                $"No se pudo contactar con Supabase: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new PublishCosmeticsResult(
                PublishCosmeticsStatus.Failed,
                null,
                $"Error inesperado al publicar cosméticos: {ex.Message}");
        }
    }
}
