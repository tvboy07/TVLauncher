using System.Linq;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

/// <summary>
/// Catálogo de versiones de Minecraft que ofrece la tienda. Antes estaba
/// hardcodeado (solo 1.18.2 → 1.20.1); ahora se deriva de
/// <c>/v2/tag/game_version</c> de Modrinth.
/// </summary>
public sealed class GameVersionCatalogTests
{
    private const string TagPayload = """
        [
          { "version": "1.21.4", "version_type": "release", "date": "2024-12-03T10:12:57Z", "major": true },
          { "version": "1.21.4-pre1", "version_type": "snapshot", "date": "2024-11-25T12:00:00Z", "major": false },
          { "version": "1.20.1", "version_type": "release", "date": "2023-06-12T12:00:00Z", "major": false },
          { "version": "1.8.9", "version_type": "release", "date": "2015-12-09T12:00:00Z", "major": false },
          { "version": "b1.7.3", "version_type": "beta", "date": "2011-07-08T12:00:00Z", "major": false },
          { "version": "1.20.1", "version_type": "release", "date": "2023-06-12T12:00:00Z", "major": false }
        ]
        """;

    [Fact]
    public void ParseGameVersions_ReturnsReleasesNewestFirst()
    {
        var versions = ModrinthService.ParseGameVersions(TagPayload);

        Assert.Equal(new[] { "1.21.4", "1.20.1", "1.8.9" }, versions);
    }

    [Fact]
    public void ParseGameVersions_ExcludesSnapshotsAndBetas()
    {
        var versions = ModrinthService.ParseGameVersions(TagPayload);

        Assert.DoesNotContain("1.21.4-pre1", versions);
        Assert.DoesNotContain("b1.7.3", versions);
    }

    [Fact]
    public void ParseGameVersions_RemovesDuplicates()
    {
        var versions = ModrinthService.ParseGameVersions(TagPayload);

        Assert.Equal(versions.Count, versions.Distinct().Count());
    }

    [Fact]
    public void ParseGameVersions_IncludesVersionsOlderThanTheOldHardcodedList()
    {
        var versions = ModrinthService.ParseGameVersions(TagPayload);

        // El desplegable antiguo empezaba en 1.18.2; ahora debe llegar hasta 1.8.9.
        Assert.Contains("1.8.9", versions);
    }

    [Fact]
    public void ParseGameVersions_OnEmptyOrInvalidJson_ReturnsEmptyList()
    {
        Assert.Empty(ModrinthService.ParseGameVersions(""));
        Assert.Empty(ModrinthService.ParseGameVersions("[]"));
        Assert.Empty(ModrinthService.ParseGameVersions("no es json"));
    }
}
