using System.Threading;
using System.Threading.Tasks;
using TVLauncher.Services;

namespace TVLauncher.Tests;

/// <summary>
/// Publisher falso compartido por los tests de cosméticos. Cuenta las
/// llamadas para verificar el debounce y la publicación previa al lanzamiento.
/// </summary>
internal sealed class FakeCosmeticsPublisher : ICosmeticsPublisher
{
    public int CallCount { get; private set; }

    public Task<PublishCosmeticsResult> PublishCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(new PublishCosmeticsResult(PublishCosmeticsStatus.Published));
    }
}
