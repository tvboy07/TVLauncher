using System.Threading;
using System.Threading.Tasks;

namespace TVLauncher.Services;

public enum PublishCosmeticsStatus
{
    Published,
    Skipped,
    Failed
}

public sealed record PublishCosmeticsResult(
    PublishCosmeticsStatus Status,
    int? StatusCode = null,
    string? Error = null);

/// <summary>
/// Publica los cosméticos equipados en el backend global para que el mod
/// Fabric pueda mostrarlos al resto de jugadores.
/// </summary>
public interface ICosmeticsPublisher
{
    Task<PublishCosmeticsResult> PublishCurrentAsync(
        CancellationToken cancellationToken = default);
}
