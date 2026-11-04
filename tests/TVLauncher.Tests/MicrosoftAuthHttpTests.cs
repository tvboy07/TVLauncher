using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

public sealed class MicrosoftAuthHttpTests
{
    [Fact]
    public async Task ProfileRequestDoesNotLeakTheTokenIntoDefaultHeaders()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var service = new MicrosoftAuthService(http);

        MinecraftProfile profile = await service.GetMinecraftProfileAsync(
            "secret-minecraft-token", CancellationToken.None);

        Assert.Equal("Notch", profile.Name);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal(
            "secret-minecraft-token",
            handler.LastRequest.Headers.Authorization.Parameter);

        // El bug anterior dejaba el token en DefaultRequestHeaders y lo enviaba
        // a cualquier petición posterior del HttpClient reutilizado.
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"uuid\",\"name\":\"Notch\"}")
            });
        }
    }
}
