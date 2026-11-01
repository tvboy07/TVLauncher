# Cosméticos In-Game Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publicar capa/sombrero desde TVLauncher a Supabase y renderizarlos en Fabric 1.21.11 para todos los jugadores visibles que usen el launcher, sin depender del servidor.

**Architecture:** El launcher publica `UUID → IDs` en una Edge Function de Supabase que verifica el token de Minecraft. Un mod cliente Fabric consulta la tabla pública por los UUID de los jugadores visibles, cachea los perfiles y los renderiza con un `FeatureRenderer`; las cuentas offline solo usan un fallback local.

**Tech Stack:** .NET 8 / Avalonia 11.1.3 / SkiaSharp; Supabase Postgres + Edge Functions (Deno/TypeScript); Fabric 1.21.11 / Java 21 / Fabric Loom 1.19.3 / Gradle 8.14.3 / Yarn 1.21.11+build.6 / Fabric API 0.141.3+1.21.11.

**Spec:** `docs/superpowers/specs/2026-10-31-in-game-cosmetics-design.md`

## Global Constraints

- Minecraft objetivo: **1.21.11** únicamente.
- Java: **21**.
- Fabric Loader: **0.20.1**.
- Yarn mappings: **1.21.11+build.6**.
- Fabric API: **0.141.3+1.21.11**.
- Fabric Loom: **1.19.3**.
- Gradle Wrapper: **8.14.3**.
- Mod ID: `tvl-cosmetics`; versión `0.1.0`.
- IDs soportados en la beta: `cape_tvlauncher`, `hat_tvlauncher`.
- Sin plugin de servidor; sin dependencia de Paper.
- Cuentas offline: render local únicamente; nunca publican.
- La `service_role` de Supabase nunca se escribe en el launcher, el mod ni el repositorio.
- La anon key es pública; solo debe tener `SELECT`.
- Mantener los **169 tests** .NET existentes en verde.
- Un commit por tarea, con el árbol limpio al terminar.

## Review Focus

- **Token de Minecraft caducado o revocado:** la publicación debe fallar sin lanzar excepción y sin impedir que el juego arranque.
- **Servidor offline-mode:** el UUID que ve el cliente no es el oficial; el mod debe encontrar la fila por `offline_uuid`.
- **Supabase caído o lento:** el juego debe seguir funcionando; el mod no debe bloquear el hilo de render.
- **Capa vanilla solapada:** si el jugador tiene `cape_tvlauncher`, la capa de Mojang no debe dibujarse encima.
- **IDs o configuración inválidos:** IDs desconocidos y JSON malformado deben ignorarse sin romper el cliente.

---

## File Structure

### Launcher

- `Models/AppConfig.cs`: URL y anon key públicas de Supabase; retirada del CDN muerto.
- `Models/CosmeticModConfig.cs`: contrato JSON que recibe el mod.
- `Services/AuthService.cs`: UUID offline correcto y migración de cuentas.
- `Services/CosmeticTextureFactory.cs`: generación única de texturas para visor y mod.
- `Services/CosmeticsPublisher.cs`: publicación HTTP a la Edge Function.
- `Services/ICosmeticsPublisher.cs`: interfaz para pruebas e inyección.
- `Services/CosmeticsService.cs`: extracción del jar, configuración de instancia y publicación.
- `ViewModels/SettingsViewModel.cs`: campos de Supabase.
- `Views/SettingsView.axaml`: sección “Sincronización in-game”.
- `App.axaml.cs`: registro DI del publicador.
- `Resources/tvl-cosmetics.jar`: artefacto embebido.
- `TVLauncher.csproj`: recurso embebido.

### Backend

- `supabase/migrations/20261031_cosmetics_profiles.sql`
- `supabase/functions/publish-cosmetics/index.ts`
- `supabase/functions/publish-cosmetics/offline_uuid.ts`
- `supabase/functions/publish-cosmetics/offline_uuid_test.ts`
- `supabase/config.toml`
- `docs/SUPABASE_SETUP.md`

### Mod

- `fabric-mod/build.gradle`
- `fabric-mod/settings.gradle`
- `fabric-mod/gradle.properties`
- `fabric-mod/gradlew`, `fabric-mod/gradlew.bat`, `fabric-mod/gradle/wrapper/`
- `fabric-mod/src/main/java/net/tvlauncher/cosmetics/TvCosmeticsClient.java`
- `fabric-mod/src/main/java/net/tvlauncher/cosmetics/CosmeticsConfig.java`
- `fabric-mod/src/main/java/net/tvlauncher/cosmetics/CosmeticProfile.java`
- `fabric-mod/src/main/java/net/tvlauncher/cosmetics/CosmeticsApi.java`
- `fabric-mod/src/main/java/net/tvlauncher/cosmetics/SupabaseCosmeticsApi.java`
- `fabric-mod/src/main/java/net/tvlauncher/cosmetics/CosmeticsRepository.java`
- `fabric-mod/src/main/java/net/tvlauncher/cosmetics/CosmeticRenderResolver.java`
- `fabric-mod/src/main/java/net/tvlauncher/cosmetics/CosmeticRenderState.java`
- `fabric-mod/src/main/java/net/tvlauncher/cosmetics/PlayerIdentityMap.java`
- `fabric-mod/src/main/java/net/tvlauncher/cosmetics/TvCosmeticsFeatureRenderer.java`
- `fabric-mod/src/main/java/net/tvlauncher/cosmetics/CosmeticRenderLayers.java`
- `fabric-mod/src/main/java/net/tvlauncher/cosmetics/mixin/CapeFeatureRendererMixin.java`
- `fabric-mod/src/main/resources/fabric.mod.json`
- `fabric-mod/src/main/resources/tvl-cosmetics.mixins.json`
- `fabric-mod/src/main/resources/assets/tvl-cosmetics/textures/cape_tvlauncher.png`
- `fabric-mod/src/main/resources/assets/tvl-cosmetics/textures/hat_tvlauncher.png`
- `scripts/build-cosmetics-mod.ps1`
- `tools/ExportCosmeticTextures/`

### Tests

- `tests/TVLauncher.Tests/OfflineUuidTests.cs`
- `tests/TVLauncher.Tests/CosmeticTextureFactoryTests.cs`
- `tests/TVLauncher.Tests/CosmeticSettingsTests.cs`
- `tests/TVLauncher.Tests/CosmeticsPublisherTests.cs`
- `tests/TVLauncher.Tests/CosmeticsServiceTests.cs`
- `fabric-mod/src/test/java/net/tvlauncher/cosmetics/...`

---

### Task 1: Corregir el UUID offline y migrar cuentas

**Files:**
- Modify: `Services/AuthService.cs`
- Test: `tests/TVLauncher.Tests/OfflineUuidTests.cs`

**Interfaces:**
- Consumes: `ConfigService(string configDirectory)` y `AccountsStore.Save(IReadOnlyList<MinecraftAccount>)` existentes.
- Produces: `internal static string AuthService.ComputeOfflineUuid(string username)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TVLauncher.Models;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

public sealed class OfflineUuidTests
{
    [Fact]
    public void ComputeOfflineUuid_MatchesJavaForNotch()
    {
        string uuid = AuthService.ComputeOfflineUuid("Notch");
        Assert.Equal("b50ad385-829d-3141-a216-7e7d7539ba7f", uuid);
    }

    [Fact]
    public void LoadAccounts_MigratesTheLegacyOfflineUuid()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        var store = new AccountsStore(config.Config.DataPath);
        store.Save(new List<MinecraftAccount>
        {
            new()
            {
                Username = "Notch",
                Uuid = "85d30ab5-9d82-4131-a216-7e7d7539ba7f",
                Type = AccountType.Offline,
                IsActive = true
            }
        });

        var auth = new AuthService(config);

        Assert.Equal(
            "b50ad385-829d-3141-a216-7e7d7539ba7f",
            Assert.Single(auth.Accounts).Uuid);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore --filter "FullyQualifiedName~OfflineUuidTests"
```

Expected: FAIL because `ComputeOfflineUuid` does not exist and the migration is absent.

- [ ] **Step 3: Implement the big-endian UUID and migration**

In `Services/AuthService.cs`:

- Add `internal static string ComputeOfflineUuid(string username)`.
- Compute `MD5("OfflinePlayer:" + username)`.
- Set version 3: `hash[6] = (hash[6] & 0x0F) | 0x30`.
- Set variant: `hash[8] = (hash[8] & 0x3F) | 0x80`.
- Format the 16 bytes manually as `8-4-4-4-12` in big-endian order. Do **not** use `new Guid(byte[])`.
- Change `GetOfflineUuid` to call `ComputeOfflineUuid`.
- In `LoadAccounts`, after `_accountsWritable = true`, loop offline accounts, recompute `Uuid`, and call `SaveAccounts()` once if any changed.

- [ ] **Step 4: Run the tests to verify they pass**

Run the same command. Expected: PASS (2 tests).

- [ ] **Step 5: Run the full .NET suite**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```powershell
git add Services/AuthService.cs tests/TVLauncher.Tests/OfflineUuidTests.cs
git commit -m "fix(cosmetics): use the Java offline UUID and migrate accounts"
```

---

### Task 2: Extraer la fábrica de texturas y generar los PNG del mod

**Files:**
- Create: `Services/CosmeticTextureFactory.cs`
- Create: `tools/ExportCosmeticTextures/ExportCosmeticTextures.csproj`
- Create: `tools/ExportCosmeticTextures/Program.cs`
- Create: `fabric-mod/src/main/resources/assets/tvl-cosmetics/textures/cape_tvlauncher.png`
- Create: `fabric-mod/src/main/resources/assets/tvl-cosmetics/textures/hat_tvlauncher.png`
- Modify: `ViewModels/SkinsPreviewViewModel.cs`
- Modify: `tests/TVLauncher.Tests/TVLauncher.Tests.csproj`
- Test: `tests/TVLauncher.Tests/CosmeticTextureFactoryTests.cs`

**Interfaces:**
- Produces: `public static byte[] CosmeticTextureFactory.CreateCapePng()`.
- Produces: `public static byte[] CosmeticTextureFactory.CreateHatPng()`.

- [ ] **Step 1: Write the failing tests**

`tests/TVLauncher.Tests/CosmeticTextureFactoryTests.cs`:

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

public sealed class CosmeticTextureFactoryTests
{
    [Fact]
    public void CapePng_Is64x32()
    {
        byte[] png = CosmeticTextureFactory.CreateCapePng();
        Assert.Equal(64u, ReadBigEndianUInt32(png, 16));
        Assert.Equal(32u, ReadBigEndianUInt32(png, 20));
    }

    [Fact]
    public void HatPng_Is64x64()
    {
        byte[] png = CosmeticTextureFactory.CreateHatPng();
        Assert.Equal(64u, ReadBigEndianUInt32(png, 16));
        Assert.Equal(64u, ReadBigEndianUInt32(png, 20));
    }

    [Fact]
    public void CommittedModAssets_MatchTheFactory()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "CosmeticAssets");
        Assert.Equal(
            Sha256(CosmeticTextureFactory.CreateCapePng()),
            Sha256(File.ReadAllBytes(Path.Combine(root, "cape_tvlauncher.png"))));
        Assert.Equal(
            Sha256(CosmeticTextureFactory.CreateHatPng()),
            Sha256(File.ReadAllBytes(Path.Combine(root, "hat_tvlauncher.png"))));
    }

    private static uint ReadBigEndianUInt32(byte[] bytes, int offset)
        => ((uint)bytes[offset] << 24)
         | ((uint)bytes[offset + 1] << 16)
         | ((uint)bytes[offset + 2] << 8)
         | bytes[offset + 3];

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));
}
```

Add to `tests/TVLauncher.Tests/TVLauncher.Tests.csproj` inside an `ItemGroup`:

```xml
<ItemGroup>
  <None Include="..\..\fabric-mod\src\main\resources\assets\tvl-cosmetics\textures\*.png"
        Link="CosmeticAssets\%(Filename)%(Extension)"
        CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore --filter "FullyQualifiedName~CosmeticTextureFactoryTests"
```

Expected: FAIL because `CosmeticTextureFactory` and the PNGs do not exist.

- [ ] **Step 3: Move the texture generation into the factory**

In `TVLauncher.csproj`, exclude the tool from the main compile glob, next to the existing `tests\**` exclusions:

```xml
<Compile Remove="tools\**" />
<None Remove="tools\**" />
<EmbeddedResource Remove="tools\**" />
```

Move the bodies of `GenerateTvLauncherCape()` and `GenerateTvLauncherHat()` from `ViewModels/SkinsPreviewViewModel.cs` into `Services/CosmeticTextureFactory.cs` as public static methods. The drawing code and dimensions must remain identical. Update the four call sites in `SkinsPreviewViewModel` to:

```csharp
CosmeticTextureFactory.CreateCapePng()
CosmeticTextureFactory.CreateHatPng()
```

- [ ] **Step 4: Create the export tool**

`tools/ExportCosmeticTextures/ExportCosmeticTextures.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\TVLauncher.csproj" />
  </ItemGroup>
</Project>
```

`Program.cs`:

```csharp
using TVLauncher.Services;

string output = args.Length > 0
    ? args[0]
    : Path.Combine("fabric-mod", "src", "main", "resources", "assets", "tvl-cosmetics", "textures");

Directory.CreateDirectory(output);
File.WriteAllBytes(Path.Combine(output, "cape_tvlauncher.png"), CosmeticTextureFactory.CreateCapePng());
File.WriteAllBytes(Path.Combine(output, "hat_tvlauncher.png"), CosmeticTextureFactory.CreateHatPng());
Console.WriteLine($"Texturas escritas en {output}");
```

- [ ] **Step 5: Generate the PNG assets**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" run --project tools\ExportCosmeticTextures\ExportCosmeticTextures.csproj -- fabric-mod\src\main\resources\assets\tvl-cosmetics\textures
```

Expected: two PNG files created.

- [ ] **Step 6: Run the tests to verify they pass**

Run the filter from Step 2. Expected: PASS (3 tests).

- [ ] **Step 7: Build and run the full suite**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build TVLauncher.csproj --no-restore --configuration Debug
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore
```

Expected: build 0 errors; all tests pass.

- [ ] **Step 8: Commit**

```powershell
git add Services/CosmeticTextureFactory.cs ViewModels/SkinsPreviewViewModel.cs TVLauncher.csproj tools/ExportCosmeticTextures tests/TVLauncher.Tests/CosmeticTextureFactoryTests.cs tests/TVLauncher.Tests/TVLauncher.Tests.csproj fabric-mod/src/main/resources/assets/tvl-cosmetics/textures
git commit -m "refactor(cosmetics): share texture generation with the in-game mod"
```

---

### Task 3: Añadir la configuración pública de Supabase y su UI

**Files:**
- Modify: `Models/AppConfig.cs`
- Modify: `ViewModels/SettingsViewModel.cs`
- Modify: `Views/SettingsView.axaml`
- Test: `tests/TVLauncher.Tests/CosmeticSettingsTests.cs`

**Interfaces:**
- Produces: `AppConfig.CosmeticsSupabaseUrl` (`string`).
- Produces: `AppConfig.CosmeticsSupabaseAnonKey` (`string`).
- Produces: `SettingsViewModel.CosmeticsSupabaseUrl` and `CosmeticsSupabaseAnonKey`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO;
using TVLauncher.Services;
using TVLauncher.ViewModels;
using Xunit;

namespace TVLauncher.Tests;

public sealed class CosmeticSettingsTests
{
    [Fact]
    public void ConfigService_RoundTripsTheSupabaseSettings()
    {
        using var dir = new TempDirectory();
        var service = new ConfigService(dir.Root);
        service.Config.CosmeticsSupabaseUrl = "https://demo.supabase.co";
        service.Config.CosmeticsSupabaseAnonKey = "anon-key";
        service.SaveConfig();

        var reloaded = new ConfigService(dir.Root);

        Assert.Equal("https://demo.supabase.co", reloaded.Config.CosmeticsSupabaseUrl);
        Assert.Equal("anon-key", reloaded.Config.CosmeticsSupabaseAnonKey);
    }

    [Fact]
    public void ConfigService_IgnoresTheLegacyJarUrl()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("config.json", """
            {
              "CosmeticsJarUrl": "https://cdn.tvlauncher.net/cosmetics/tvl-cosmetics.jar",
              "CosmeticsSupabaseUrl": "https://demo.supabase.co"
            }
            """);

        var service = new ConfigService(dir.Root);

        Assert.Equal("https://demo.supabase.co", service.Config.CosmeticsSupabaseUrl);
    }

    [Fact]
    public void SettingsViewModel_SavesTheSupabaseSettings()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        var vm = new SettingsViewModel(config)
        {
            CosmeticsSupabaseUrl = "https://demo.supabase.co",
            CosmeticsSupabaseAnonKey = "anon-key"
        };

        vm.SaveSettingsCommand.Execute(null);

        Assert.Equal("https://demo.supabase.co", config.Config.CosmeticsSupabaseUrl);
        Assert.Equal("anon-key", config.Config.CosmeticsSupabaseAnonKey);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore --filter "FullyQualifiedName~CosmeticSettingsTests"
```

Expected: FAIL because the properties do not exist.

- [ ] **Step 3: Implement the config fields and settings**

- In `AppConfig`, remove `CosmeticsJarUrl` and add:

```csharp
public string CosmeticsSupabaseUrl { get; set; } = string.Empty;
public string CosmeticsSupabaseAnonKey { get; set; } = string.Empty;
```

- In `SettingsViewModel`, add two `[ObservableProperty]` string fields, load them in `LoadSettings`, and assign them in `SaveSettings`.
- In `SettingsView.axaml`, add a card after “Módulos”:

```xml
<Border Classes="card" Padding="20">
    <StackPanel Spacing="14">
        <StackPanel Spacing="2">
            <TextBlock Text="Sincronización in-game" Classes="h2"/>
            <TextBlock Text="Supabase permite que los jugadores con el launcher se vean entre sí"
                       Classes="caption"/>
        </StackPanel>
        <StackPanel Spacing="8">
            <TextBlock Text="URL DE SUPABASE" Classes="label"/>
            <TextBox Text="{Binding CosmeticsSupabaseUrl}"
                     Watermark="https://tu-proyecto.supabase.co"/>
            <TextBlock Text="ANON KEY" Classes="label"/>
            <TextBox Text="{Binding CosmeticsSupabaseAnonKey}"
                     Watermark="Clave pública anon"/>
        </StackPanel>
    </StackPanel>
</Border>
```

- [ ] **Step 4: Run the tests to verify they pass**

Run the filter from Step 2. Expected: PASS (3 tests).

- [ ] **Step 5: Build and run the full suite**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build TVLauncher.csproj --no-restore --configuration Debug
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore
```

Expected: build 0 errors; all tests pass.

- [ ] **Step 6: Commit**

```powershell
git add Models/AppConfig.cs ViewModels/SettingsViewModel.cs Views/SettingsView.axaml tests/TVLauncher.Tests/CosmeticSettingsTests.cs
git commit -m "feat(cosmetics): configure Supabase from settings"
```

---

### Task 4: Crear el scaffold de Supabase

**Files:**
- Create: `supabase/migrations/20261031_cosmetics_profiles.sql`
- Create: `supabase/functions/publish-cosmetics/index.ts`
- Create: `supabase/functions/publish-cosmetics/offline_uuid.ts`
- Create: `supabase/functions/publish-cosmetics/offline_uuid_test.ts`
- Create: `supabase/config.toml`
- Create: `docs/SUPABASE_SETUP.md`

**Interfaces:**
- Produces: `POST /functions/v1/publish-cosmetics` with body `{ cape_id, hat_id }`.
- Produces: `offlineUuid(name: string): string`.
- Consumes: Supabase project URL, anon key and service role (only at deploy time).

- [ ] **Step 1: Write the SQL migration**

`supabase/migrations/20261031_cosmetics_profiles.sql`:

```sql
create table public.cosmetics_profiles (
  id             bigint generated always as identity primary key,
  minecraft_uuid uuid unique,
  offline_uuid   uuid unique,
  cape_id        text,
  hat_id         text,
  updated_at     timestamptz not null default now(),
  constraint at_least_one_uuid
    check (minecraft_uuid is not null or offline_uuid is not null)
);

alter table public.cosmetics_profiles enable row level security;

create policy "cosmetics are publicly readable"
  on public.cosmetics_profiles
  for select
  to anon, authenticated
  using (true);
```

- [ ] **Step 2: Write the offline UUID helper and its test**

`offline_uuid.ts` exports `offlineUuid(name: string): string`. Use `npm:spark-md5@3.0.2`, set version/variant bits, and format `8-4-4-4-12` big-endian.

`offline_uuid_test.ts`:

```ts
import { assertEquals } from "jsr:@std/assert@1";
import { offlineUuid } from "./offline_uuid.ts";

Deno.test("Notch offline UUID matches Java", () => {
  assertEquals(offlineUuid("Notch"), "b50ad385-829d-3141-a216-7e7d7539ba7f");
});
```

- [ ] **Step 3: Write the Edge Function**

`index.ts`:

- `Deno.serve(async (req) => { ... })`.
- Reject non-POST with `405`.
- Read `Authorization`; require `Bearer <token>`.
- Validate body IDs: `null` or `^[a-z0-9_]{1,64}$`; otherwise `400`.
- Call `https://api.minecraftservices.com/minecraft/profile` with the token.
- On non-200 return `401` or `502`; never write.
- Parse `{ id, name }`.
- Compute `offline_uuid` with `offlineUuid(name)`.
- Use `createClient(Deno.env.get("SUPABASE_URL")!, Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!)`.
- Upsert `cosmetics_profiles` on `minecraft_uuid` with `{ minecraft_uuid, offline_uuid, cape_id, hat_id, updated_at: new Date().toISOString() }`.
- Return `200 { ok: true }`.
- Do not log the token.

- [ ] **Step 4: Write the Supabase config and setup doc**

`supabase/config.toml`:

```toml
[functions.publish-cosmetics]
verify_jwt = false
```

`docs/SUPABASE_SETUP.md` must document:

1. Create the project.
2. Run the migration in the SQL editor.
3. Deploy the function with `supabase functions deploy publish-cosmetics --no-verify-jwt`.
4. Copy the project URL and anon key into Ajustes → Sincronización in-game.
5. Test with a real Microsoft token using `curl`.
6. Confirm that anon can select but cannot insert/update/delete.

- [ ] **Step 5: Bootstrap Deno and run the helper test**

If `deno` is not installed:

```powershell
$zip = Join-Path $env:TEMP "deno.zip"
Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip" -OutFile $zip
Expand-Archive -Path $zip -DestinationPath (Join-Path $env:TEMP "deno") -Force
& (Join-Path $env:TEMP "deno\deno.exe") test supabase/functions/publish-cosmetics/offline_uuid_test.ts
```

Expected: PASS (1 test).

- [ ] **Step 6: Static security check**

```powershell
Select-String -Path supabase/functions/publish-cosmetics/index.ts -Pattern "SERVICE_ROLE|Authorization|api.minecraftservices.com"
Select-String -Path Models/AppConfig.cs,Services/CosmeticsService.cs -Pattern "service_role|SERVICE_ROLE"
```

Expected: the Edge Function references the service role only through `Deno.env`; no client file contains the service role.

- [ ] **Step 7: Commit**

```powershell
git add supabase docs/SUPABASE_SETUP.md
git commit -m "feat(cosmetics): scaffold the Supabase backend"
```

---

### Task 5: Implementar `CosmeticsPublisher`

**Files:**
- Create: `Services/ICosmeticsPublisher.cs`
- Create: `Services/CosmeticsPublisher.cs`
- Create: `tests/TVLauncher.Tests/FakeCosmeticsPublisher.cs`
- Modify: `Services/CosmeticsService.cs`
- Modify: `App.axaml.cs`
- Test: `tests/TVLauncher.Tests/CosmeticsPublisherTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum PublishCosmeticsStatus { Published, Skipped, Failed }

public sealed record PublishCosmeticsResult(
    PublishCosmeticsStatus Status,
    int? StatusCode = null,
    string? Error = null);

public interface ICosmeticsPublisher
{
    Task<PublishCosmeticsResult> PublishCurrentAsync(
        CancellationToken cancellationToken = default);
}
```

- Produces: `internal Task<PublishCosmeticsResult> CosmeticsPublisher.PublishAsync(string? capeId, string? hatId, string accessToken, CancellationToken cancellationToken)`.
- Consumes: `ConfigService.CosmeticsSupabaseUrl`, `CosmeticsSupabaseAnonKey`, `AuthService.ActiveAccount`.

- [ ] **Step 1: Write the failing tests**

`CosmeticsPublisherTests.cs` must include:

```csharp
[Fact]
public async Task PublishAsync_SendsTheExpectedRequest()
{
    // Fake handler records the request.
    // Assert POST https://demo.supabase.co/functions/v1/publish-cosmetics
    // Assert apikey header == "anon-key"
    // Assert Authorization == "Bearer minecraft-token"
    // Assert JSON body has cape_id="cape_tvlauncher", hat_id="hat_tvlauncher"
}

[Fact]
public async Task PublishAsync_ReturnsFailedOn401WithoutThrowing()
{
    // Handler responds 401.
    // Assert Status == Failed, StatusCode == 401.
}

[Fact]
public async Task PublishAsync_ReturnsFailedOnTimeoutWithoutThrowing()
{
    // Handler throws TaskCanceledException.
    // Assert Status == Failed.
}

[Fact]
public async Task PublishCurrentAsync_SkipsWhenSupabaseIsNotConfigured()
{
    // Empty URL/key.
    // Assert Status == Skipped and the handler was not called.
}

[Fact]
public async Task PublishCurrentAsync_SkipsOfflineAccounts()
{
    // AuthService with an offline account.
    // Assert Status == Skipped and no HTTP call.
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore --filter "FullyQualifiedName~CosmeticsPublisherTests"
```

Expected: FAIL because the publisher does not exist.

- [ ] **Step 3: Implement the publisher**

- Public constructor: `CosmeticsPublisher(ConfigService configService, AuthService authService)`.
- Internal constructor: `CosmeticsPublisher(ConfigService configService, AuthService authService, HttpClient httpClient)` for tests.
- `PublishCurrentAsync`:
  - Read active account; if null or `Type != AccountType.Microsoft`, return `Skipped`.
  - If URL or key is empty, return `Skipped`.
  - Read `AccessToken`; if empty, return `Skipped`.
  - Call `PublishAsync(_configService.Config.EquippedCapeId, EquippedHatId, token, ct)`.
- `PublishAsync`:
  - Build `{base}/functions/v1/publish-cosmetics`.
  - `POST` with `StringContent(JsonConvert.SerializeObject(new { cape_id = capeId, hat_id = hatId }), Encoding.UTF8, "application/json")`.
  - Add `apikey` and `Authorization: Bearer`.
  - Catch `TaskCanceledException`, `HttpRequestException` and any exception; return `Failed` with a short message.
  - Return `Published` on success.
- Never log the token.
- Register in `App.axaml.cs`: `services.AddSingleton<ICosmeticsPublisher, CosmeticsPublisher>();`.
- Change the `CosmeticsService` constructor to `CosmeticsService(ConfigService configService, ICosmeticsPublisher publisher)` and store the publisher. Do not call it yet; Task 11 wires the calls.
- Add the shared test helper `tests/TVLauncher.Tests/FakeCosmeticsPublisher.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using TVLauncher.Services;

namespace TVLauncher.Tests;

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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run the filter from Step 2. Expected: PASS (5 tests).

- [ ] **Step 5: Build and run the full suite**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build TVLauncher.csproj --no-restore --configuration Debug
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore
```

Expected: build 0 errors; all tests pass.

- [ ] **Step 6: Commit**

```powershell
git add Services/ICosmeticsPublisher.cs Services/CosmeticsPublisher.cs Services/CosmeticsService.cs App.axaml.cs tests/TVLauncher.Tests/CosmeticsPublisherTests.cs tests/TVLauncher.Tests/FakeCosmeticsPublisher.cs
git commit -m "feat(cosmetics): publish equipped items to Supabase"
```

---

### Task 6: Crear el proyecto Fabric y compilar un jar vacío

**Files:**
- Create: `fabric-mod/build.gradle`
- Create: `fabric-mod/settings.gradle`
- Create: `fabric-mod/gradle.properties`
- Create: `fabric-mod/gradlew`, `fabric-mod/gradlew.bat`, `fabric-mod/gradle/wrapper/`
- Create: `fabric-mod/src/main/java/net/tvlauncher/cosmetics/TvCosmeticsClient.java`
- Create: `fabric-mod/src/main/resources/fabric.mod.json`
- Create: `scripts/build-cosmetics-mod.ps1`

**Interfaces:**
- Produces: `tvl-cosmetics-0.1.0.jar` with mod id `tvl-cosmetics`.
- Produces: `scripts/build-cosmetics-mod.ps1` that builds and copies the jar to `Resources/tvl-cosmetics.jar`.

- [ ] **Step 1: Bootstrap the Gradle wrapper**

Download Gradle 8.14.3 to `%TEMP%`, extract it, and run:

```powershell
& (Join-Path $env:TEMP "gradle-8.14.3\bin\gradle.bat") wrapper --gradle-version 8.14.3
```

Run it in `fabric-mod`. Expected: `gradlew`, `gradlew.bat` and `gradle/wrapper/` are created.

- [ ] **Step 2: Write the Gradle files**

`gradle.properties`:

```properties
org.gradle.jvmargs=-Xmx1G
org.gradle.parallel=true
minecraft_version=1.21.11
yarn_mappings=1.21.11+build.6
loader_version=0.20.1
fabric_version=0.141.3+1.21.11
mod_version=0.1.0
maven_group=net.tvlauncher
archives_base_name=tvl-cosmetics
```

`settings.gradle`:

```groovy
pluginManagement {
    repositories {
        maven { url = 'https://maven.fabricmc.net/' }
        gradlePluginPortal()
    }
}
rootProject.name = 'tvl-cosmetics'
```

`build.gradle`:

```groovy
plugins {
    id 'fabric-loom' version '1.19.3'
    id 'maven-publish'
}

version = project.mod_version
group = project.maven_group

base {
    archivesName = project.archives_base_name
}

repositories {
    mavenCentral()
}

dependencies {
    minecraft "com.mojang:minecraft:${project.minecraft_version}"
    mappings "net.fabricmc:yarn:${project.yarn_mappings}:v2"
    modImplementation "net.fabricmc:fabric-loader:${project.loader_version}"
    modImplementation "net.fabricmc.fabric-api:fabric-api:${project.fabric_version}"
}

processResources {
    inputs.property 'version', project.version
    filesMatching('fabric.mod.json') {
        expand 'version': project.version
    }
}

tasks.withType(JavaCompile).configureEach {
    it.options.release = 21
}

java {
    toolchain {
        languageVersion = JavaLanguageVersion.of(21)
    }
    withSourcesJar()
}
```

- [ ] **Step 3: Write the minimal mod metadata and initializer**

`fabric.mod.json`:

```json
{
  "schemaVersion": 1,
  "id": "tvl-cosmetics",
  "version": "${version}",
  "name": "TVLauncher Cosmetics",
  "description": "Render TVLauncher capes and hats for players using the launcher.",
  "authors": ["TVLauncher"],
  "license": "MIT",
  "environment": "client",
  "entrypoints": {
    "client": ["net.tvlauncher.cosmetics.TvCosmeticsClient"]
  },
  "depends": {
    "fabricloader": ">=0.16.0",
    "minecraft": "~1.21.11",
    "java": ">=21",
    "fabric-api": "*"
  }
}
```

`TvCosmeticsClient.java`:

```java
package net.tvlauncher.cosmetics;

import net.fabricmc.api.ClientModInitializer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public final class TvCosmeticsClient implements ClientModInitializer {
    public static final String MOD_ID = "tvl-cosmetics";
    public static final Logger LOGGER = LoggerFactory.getLogger(MOD_ID);

    @Override
    public void onInitializeClient() {
        LOGGER.info("TVLauncher Cosmetics initialized");
    }
}
```

- [ ] **Step 4: Build the jar**

```powershell
.\gradlew.bat build
```

Run in `fabric-mod`. Expected: `BUILD SUCCESSFUL` and `build/libs/tvl-cosmetics-0.1.0.jar`.

- [ ] **Step 5: Verify the jar contents**

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$jar = "fabric-mod\build\libs\tvl-cosmetics-0.1.0.jar"
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $jar))
$zip.Entries | Where-Object { $_.FullName -match "fabric.mod.json|TvCosmeticsClient.class" }
$zip.Dispose()
```

Expected: both entries are listed.

- [ ] **Step 6: Write the build-and-copy script**

`scripts/build-cosmetics-mod.ps1`:

```powershell
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location (Join-Path $root "fabric-mod")
try {
    & .\gradlew.bat build
    if ($LASTEXITCODE -ne 0) { throw "Gradle build failed" }
    Copy-Item "build\libs\tvl-cosmetics-0.1.0.jar" (Join-Path $root "Resources\tvl-cosmetics.jar") -Force
} finally {
    Pop-Location
}
```

- [ ] **Step 7: Commit**

```powershell
git add fabric-mod scripts/build-cosmetics-mod.ps1
git commit -m "feat(cosmetics): add the Fabric 1.21.11 mod skeleton"
```

---

### Task 7: Implementar configuración, API, caché y resolución del mod

**Files:**
- Create: `fabric-mod/src/main/java/net/tvlauncher/cosmetics/CosmeticsConfig.java`
- Create: `fabric-mod/src/main/java/net/tvlauncher/cosmetics/CosmeticProfile.java`
- Create: `fabric-mod/src/main/java/net/tvlauncher/cosmetics/CosmeticsApi.java`
- Create: `fabric-mod/src/main/java/net/tvlauncher/cosmetics/SupabaseCosmeticsApi.java`
- Create: `fabric-mod/src/main/java/net/tvlauncher/cosmetics/CosmeticsRepository.java`
- Create: `fabric-mod/src/main/java/net/tvlauncher/cosmetics/CosmeticRenderResolver.java`
- Modify: `fabric-mod/build.gradle`
- Test: `fabric-mod/src/test/java/net/tvlauncher/cosmetics/CosmeticsRepositoryTest.java`
- Test: `fabric-mod/src/test/java/net/tvlauncher/cosmetics/CosmeticRenderResolverTest.java`
- Test: `fabric-mod/src/test/java/net/tvlauncher/cosmetics/CosmeticsConfigTest.java`
- Test: `fabric-mod/src/test/java/net/tvlauncher/cosmetics/SupabaseCosmeticsApiTest.java`

**Interfaces:**
- Produces: `record CosmeticProfile(UUID minecraftUuid, UUID offlineUuid, String capeId, String hatId, Instant updatedAt)`.
- Produces: `record CosmeticsConfig(int schemaVersion, boolean enabled, String supabaseUrl, String supabaseAnonKey, String localCapeId, String localHatId, int pollSeconds)`.
- Produces: `interface CosmeticsApi { List<CosmeticProfile> fetch(Collection<UUID> uuids) throws Exception; }`.
- Produces: `CosmeticsRepository(CosmeticsApi api, Clock clock)`, `Optional<CosmeticProfile> get(UUID)`, `void refresh(Collection<UUID>)`, `void clear()`.
- Produces: `CosmeticRenderResolver.resolve(UUID uuid, boolean localPlayer, CosmeticsConfig config, CosmeticsRepository repository)` returning `Optional<ResolvedCosmetics>`.
- Produces: `record ResolvedCosmetics(String capeId, String hatId)`.

- [ ] **Step 1: Add JUnit to Gradle**

Add to `build.gradle`:

```groovy
dependencies {
    testImplementation platform('org.junit:junit-bom:5.11.4')
    testImplementation 'org.junit.jupiter:junit-jupiter'
}

test {
    useJUnitPlatform()
}
```

- [ ] **Step 2: Write the failing tests**

`CosmeticsRepositoryTest.java` must cover:

```java
@Test void positiveResultIsCachedFor60Seconds();
@Test void negativeResultIsCachedFor30Seconds();
@Test void apiFailureReturnsEmptyAndDoesNotThrow();
@Test void apiFailureBacksOffBeforeRetrying();
@Test void refreshBatchesAtMost50Uuids();
@Test void matchesByMinecraftUuidAndOfflineUuid();
```

`CosmeticRenderResolverTest.java` must cover:

```java
@Test void remoteProfileWinsOverLocalFallback();
@Test void localFallbackAppliesOnlyToTheLocalPlayer();
@Test void unknownIdsAreIgnored();
@Test void disabledConfigReturnsEmpty();
```

`CosmeticsConfigTest.java` must cover:

```java
@Test void missingFileReturnsDisabledDefaults();
@Test void malformedJsonReturnsDisabledDefaults();
@Test void pollSecondsIsClampedBetween5And300();
```

`SupabaseCosmeticsApiTest.java` must cover:

```java
@Test void parsesUuidFieldsAndNullIds();
@Test void buildsAQueryWithBothUuidColumns();
```

- [ ] **Step 3: Run the tests to verify they fail**

```powershell
.\gradlew.bat test
```

Run in `fabric-mod`. Expected: FAIL because the production classes do not exist.

- [ ] **Step 4: Implement the config, API and cache**

- `CosmeticsConfig.load(Path)` uses Gson. Missing/malformed → `enabled=false`, empty URL/key, `pollSeconds=30`.
- `SupabaseCosmeticsApi` builds:

```text
{base}/rest/v1/cosmetics_profiles?select=minecraft_uuid,offline_uuid,cape_id,hat_id,updated_at&or=(...)
```

with `apikey` and `Authorization: Bearer <anon>`.
- `SupabaseCosmeticsApi` exposes a package-private `static List<CosmeticProfile> parse(String json)` and `static String buildQuery(Collection<UUID> uuids)` so the JSON contract and the `or=(...)` query can be tested without a network server.
- `CosmeticsRepository` is thread-safe (`ConcurrentHashMap`) because the HTTP executor writes while the render thread reads.
- `CosmeticsRepository` uses `Clock` to test TTLs; positive TTL 60 s, negative 30 s.
- `refresh` splits into batches of 50; calls `api.fetch`; on exception marks the batch negative, increments a failure counter, and sets `nextRetryAt` to `min(300, 1 << failures)` seconds in the future.
- `refresh` returns immediately while `clock.instant()` is before `nextRetryAt`.
- A successful refresh resets the failure counter and `nextRetryAt`.
- `get` returns empty for expired/negative/absent entries.
- `CosmeticRenderResolver`:
  - If `!config.enabled()` → empty.
  - Remote profile wins (even if both IDs are null).
  - If no remote and `localPlayer` → use `localCapeId`/`localHatId`.
  - Otherwise empty.
  - Filter to `cape_tvlauncher` and `hat_tvlauncher`; unknown IDs become null.

- [ ] **Step 5: Run the tests to verify they pass**

```powershell
.\gradlew.bat test
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add fabric-mod
git commit -m "feat(cosmetics): fetch and cache player cosmetics in the mod"
```

---

### Task 8: Implementar identidad, renderizado y mixin de capa vanilla

**Files:**
- Create: `fabric-mod/src/main/java/net/tvlauncher/cosmetics/PlayerIdentityMap.java`
- Create: `fabric-mod/src/main/java/net/tvlauncher/cosmetics/CosmeticRenderState.java`
- Create: `fabric-mod/src/main/java/net/tvlauncher/cosmetics/CosmeticRenderLayers.java`
- Create: `fabric-mod/src/main/java/net/tvlauncher/cosmetics/TvCosmeticsFeatureRenderer.java`
- Create: `fabric-mod/src/main/java/net/tvlauncher/cosmetics/mixin/CapeFeatureRendererMixin.java`
- Create: `fabric-mod/src/main/resources/tvl-cosmetics.mixins.json`
- Modify: `fabric-mod/src/main/java/net/tvlauncher/cosmetics/TvCosmeticsClient.java`
- Modify: `fabric-mod/src/main/resources/fabric.mod.json`
- Test: `fabric-mod/src/test/java/net/tvlauncher/cosmetics/CosmeticRenderStateTest.java`

**Interfaces:**
- Consumes: `CosmeticsRepository`, `CosmeticRenderResolver`, `CosmeticsConfig`.
- Produces: `PlayerIdentityMap.get(int entityId)` returning `UUID`.
- Produces: `CosmeticRenderState.initialize(CosmeticsRepository repository, CosmeticsConfig config)`.
- Produces: `CosmeticRenderState.resolve(int entityId)` returning `Optional<ResolvedCosmetics>`.
- Produces: `TvCosmeticsFeatureRenderer` registered for `PlayerEntityRenderer`.

- [ ] **Step 1: Write the failing state test**

```java
@Test void hasCustomCapeIsTrueOnlyForCapeTvlauncher();
@Test void resolveReturnsEmptyForUnknownEntityId();
```

Each test calls `CosmeticRenderState.initialize(repository, config)` with a fake repository/config and populates `PlayerIdentityMap` explicitly.

- [ ] **Step 2: Run the test to verify it fails**

```powershell
.\gradlew.bat test --tests "net.tvlauncher.cosmetics.CosmeticRenderStateTest"
```

Expected: FAIL because the class does not exist.

- [ ] **Step 3: Implement identity tracking and state**

`PlayerIdentityMap` uses a `ConcurrentHashMap<Integer, UUID>`.

In `TvCosmeticsClient`:

- Load `CosmeticsConfig` from `FabricLoader.getInstance().getConfigDir().resolve("tvl-cosmetics.json")`.
- Create `SupabaseCosmeticsApi` and `CosmeticsRepository`.
- Call `CosmeticRenderState.initialize(repository, config)`.
- Register `ClientTickEvents.END_CLIENT_TICK`:
  - If `client.world == null`, clear identity map and repository.
  - Else rebuild `entityId → player.getUuid()` for `client.world.getPlayers()`.
  - Every `pollSeconds`, reload `tvl-cosmetics.json` if its `mtime` changed, then recreate the repository/config and call `CosmeticRenderState.initialize(...)`.
  - Every `pollSeconds`, call `repository.refresh(uuids)` on a daemon executor.
  - Refresh immediately when a new UUID appears.
- Register `LivingEntityFeatureRendererRegistrationCallback.EVENT` and add `TvCosmeticsFeatureRenderer` when the renderer is a `PlayerEntityRenderer`.
- Add `"tvl-cosmetics.mixins.json"` to `fabric.mod.json` as `"mixins": ["tvl-cosmetics.mixins.json"]`.

`CosmeticRenderState` centralizes:

```java
Optional<ResolvedCosmetics> resolve(int entityId);
boolean hasCustomCape(int entityId);
```

It gets the UUID from `PlayerIdentityMap`, determines whether it is the local player, and calls `CosmeticRenderResolver`.

- [ ] **Step 4: Implement custom render layers and the feature renderer**

`CosmeticRenderLayers` creates two layers with `RenderLayer.of` + `RenderSetup.builder(RenderPipelines.ENTITY_CUTOUT)`, texture `Sampler0`, `useLightmap()` and `useOverlay()`.

`TvCosmeticsFeatureRenderer`:

```java
public final class TvCosmeticsFeatureRenderer
        extends FeatureRenderer<PlayerEntityRenderState, PlayerEntityModel<PlayerEntityRenderState>> {

    @Override
    public void render(
            MatrixStack matrices,
            OrderedRenderCommandQueue queue,
            int light,
            PlayerEntityRenderState state,
            float limbAngle,
            float limbDistance) {
        // resolve state by state.id
        // queue.getBatchingQueue(0).submitCustom(matrices, layer, (entry, vertices) -> drawBox(...));
    }
}
```

- Cape box: 10×16×1 model units, attached behind the body, slight tilt, hidden when `state.glidingTicks > 0`.
- Hat: brim 9.2×0.5×9.2, crown 6.2×2.8×6.2, band 6.35×0.5×6.35, attached to `model.head`, 2° tilt.
- Use `matrices.push()`/`pop()` and scale model units by `1/16f`.
- Draw six faces per box with the UV layout from `CosmeticTextureFactory` (cape region `tx=2, ty=0`; hat regions `(0,0)`, `(0,16)`, `(0,32)`).

- [ ] **Step 5: Implement the vanilla cape mixin**

`tvl-cosmetics.mixins.json`:

```json
{
  "required": true,
  "package": "net.tvlauncher.cosmetics.mixin",
  "compatibilityLevel": "JAVA_21",
  "client": ["CapeFeatureRendererMixin"],
  "injectors": { "defaultRequire": 1 }
}
```

`CapeFeatureRendererMixin` injects at `HEAD` of `CapeFeatureRenderer.render(...)` and cancels when `CosmeticRenderState.hasCustomCape(state.id)`.

- [ ] **Step 6: Build and run the tests**

```powershell
.\gradlew.bat build
```

Expected: `BUILD SUCCESSFUL`.

- [ ] **Step 7: Verify the jar contains the mixin and textures**

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path "fabric-mod\build\libs\tvl-cosmetics-0.1.0.jar"))
$zip.Entries | Where-Object { $_.FullName -match "mixins.json|CapeFeatureRendererMixin|textures/(cape|hat)_tvlauncher" }
$zip.Dispose()
```

Expected: mixin config, mixin class and both textures are listed.

- [ ] **Step 8: Commit**

```powershell
git add fabric-mod
git commit -m "feat(cosmetics): render capes and hats for visible players"
```

---

### Task 9: Embeber el jar y extraerlo desde el launcher

**Files:**
- Create: `Resources/tvl-cosmetics.jar`
- Modify: `TVLauncher.csproj`
- Modify: `Services/CosmeticsService.cs`
- Test: `tests/TVLauncher.Tests/CosmeticsServiceTests.cs`

**Interfaces:**
- Produces: `internal Task<bool> CosmeticsService.EnsureModJarAsync()`.
- Consumes: embedded resource `TVLauncher.Resources.tvl-cosmetics.jar`.

- [ ] **Step 1: Build and copy the mod jar**

```powershell
.\scripts\build-cosmetics-mod.ps1
```

Expected: `Resources/tvl-cosmetics.jar` exists and is non-empty.

- [ ] **Step 2: Embed the jar**

In `TVLauncher.csproj`, add:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\tvl-cosmetics.jar"
                    LogicalName="TVLauncher.Resources.tvl-cosmetics.jar" />
</ItemGroup>
```

- [ ] **Step 3: Write the failing extraction test**

In `CosmeticsServiceTests.cs`:

```csharp
[Fact]
public async Task EnsureModJarAsync_ExtractsTheEmbeddedJar()
{
    using var dir = new TempDirectory();
    var config = new ConfigService(dir.Root);
    var service = new CosmeticsService(config, new FakeCosmeticsPublisher());

    bool ok = await service.EnsureModJarAsync();

    string path = Path.Combine(config.GetCosmeticsPath(), "tvl-cosmetics.jar");
    Assert.True(ok);
    Assert.True(File.Exists(path));
    Assert.True(new FileInfo(path).Length > 0);
}

[Fact]
public async Task EnsureModJarAsync_IsIdempotent()
{
    using var dir = new TempDirectory();
    var config = new ConfigService(dir.Root);
    var service = new CosmeticsService(config, new FakeCosmeticsPublisher());

    await service.EnsureModJarAsync();
    string path = Path.Combine(config.GetCosmeticsPath(), "tvl-cosmetics.jar");
    DateTime first = File.GetLastWriteTimeUtc(path);
    await Task.Delay(20);
    await service.EnsureModJarAsync();

    Assert.Equal(first, File.GetLastWriteTimeUtc(path));
}
```

- [ ] **Step 4: Run the tests to verify they fail**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore --filter "FullyQualifiedName~CosmeticsServiceTests"
```

Expected: FAIL because `CosmeticsService` does not expose `EnsureModJarAsync`.

- [ ] **Step 5: Implement extraction and replace the CDN path**

- Remove `_httpClient`, `DownloadCosmeticsJarAsync`, and the `CosmeticsJarUrl` usage.
- Add `private const string ModResourceName = "TVLauncher.Resources.tvl-cosmetics.jar";`.
- `EnsureModJarAsync`:
  - Serialize concurrent calls with a `SemaphoreSlim`.
  - Read the embedded stream; return `false` if missing.
  - Compute SHA-256 of the embedded bytes.
  - If the local file exists with the same hash, return `true` without touching it.
  - Write atomically to `_cosmeticsJarPath`.
- `Initialize` calls `EnsureModJarAsync` and logs failures.
- `InjectCosmeticsToInstanceAsync`:
  - Ensure the local jar exists.
  - Create `mods/`.
  - Compare SHA-256 of source and target; copy if different.
  - Log but do not throw if copy fails.

- [ ] **Step 6: Run the tests to verify they pass**

Run the filter from Step 4. Expected: PASS (2 tests).

- [ ] **Step 7: Build and run the full suite**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build TVLauncher.csproj --no-restore --configuration Debug
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore
```

Expected: build 0 errors; all tests pass.

- [ ] **Step 8: Commit**

```powershell
git add Resources/tvl-cosmetics.jar TVLauncher.csproj Services/CosmeticsService.cs tests/TVLauncher.Tests/CosmeticsServiceTests.cs
git commit -m "feat(cosmetics): bundle and inject the in-game mod"
```

---

### Task 10: Escribir la configuración del mod y limpiar los IDs equipados

**Files:**
- Create: `Models/CosmeticModConfig.cs`
- Modify: `Services/CosmeticsService.cs`
- Test: `tests/TVLauncher.Tests/CosmeticsServiceTests.cs`

**Interfaces:**
- Produces: `public sealed class CosmeticModConfig` with JSON properties `schema_version`, `enabled`, `supabase_url`, `supabase_anon_key`, `local_cape_id`, `local_hat_id`, `poll_seconds`.
- Produces: `CosmeticsService.SyncCosmeticsToInstanceAsync` writes `config/tvl-cosmetics.json`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task SyncCosmeticsToInstanceAsync_WritesTheModConfig()
{
    using var dir = new TempDirectory();
    var config = new ConfigService(dir.Root);
    config.Config.CosmeticsSupabaseUrl = "https://demo.supabase.co";
    config.Config.CosmeticsSupabaseAnonKey = "anon-key";
    config.Config.EquippedCapeId = "cape_tvlauncher";
    config.Config.EquippedHatId = "hat_tvlauncher";
    var service = new CosmeticsService(config, new FakeCosmeticsPublisher());
    var instance = new MinecraftInstance
    {
        GameDirectory = dir.Combine("instance")
    };

    await service.SyncCosmeticsToInstanceAsync(instance);

    string path = Path.Combine(instance.GameDirectory, "config", "tvl-cosmetics.json");
    string json = await File.ReadAllTextAsync(path);
    Assert.Contains("\"supabase_url\": \"https://demo.supabase.co\"", json);
    Assert.Contains("\"local_cape_id\": \"cape_tvlauncher\"", json);
    Assert.Contains("\"local_hat_id\": \"hat_tvlauncher\"", json);
    Assert.Contains("\"schema_version\": 1", json);
}

[Fact]
public async Task SyncCosmeticsToInstanceAsync_DisablesTheModWhenCosmeticsAreDisabled()
{
    // EnableCosmetics = false
    // Assert "enabled": false in the JSON.
}

[Fact]
public void LoadCosmeticsConfig_RemovesObsoleteIds()
{
    // cosmetics_config.json contains cape_1, hat_1, hat_tvlauncher.
    // Assert EquippedCosmetics does not contain cape_1/hat_1 and keeps hat_tvlauncher.
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore --filter "FullyQualifiedName~CosmeticsServiceTests"
```

Expected: FAIL because the JSON contract and cleanup do not exist.

- [ ] **Step 3: Implement the JSON contract**

`Models/CosmeticModConfig.cs` uses Newtonsoft `[JsonProperty]` for the snake_case names. Defaults:

```csharp
public int SchemaVersion { get; set; } = 1;
public bool Enabled { get; set; }
public string SupabaseUrl { get; set; } = string.Empty;
public string SupabaseAnonKey { get; set; } = string.Empty;
public string? LocalCapeId { get; set; }
public string? LocalHatId { get; set; }
public int PollSeconds { get; set; } = 30;
```

`SyncCosmeticsToInstanceAsync`:

- Create `config/`.
- Write `CosmeticModConfig` to `tvl-cosmetics.json`.
- Write the same JSON to `_cosmeticsSyncPath`.
- `Enabled = _configService.Config.EnableCosmetics`.
- `LocalCapeId = _configService.Config.EquippedCapeId`.
- `LocalHatId = _configService.Config.EquippedHatId`.
- `SupabaseUrl`/`AnonKey` from config.
- Use `JsonConvert.SerializeObject(..., Formatting.Indented)`.

`InjectCosmeticsToInstanceAsync` calls `SyncCosmeticsToInstanceAsync` **before** copying the jar.

`LoadCosmeticsConfig` removes `cape_1` and `hat_1` and adds the current `EquippedCapeId`/`EquippedHatId` if missing.

- [ ] **Step 4: Run the tests to verify they pass**

Run the filter from Step 2. Expected: PASS.

- [ ] **Step 5: Build and run the full suite**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build TVLauncher.csproj --no-restore --configuration Debug
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore
```

Expected: build 0 errors; all tests pass.

- [ ] **Step 6: Commit**

```powershell
git add Models/CosmeticModConfig.cs Services/CosmeticsService.cs tests/TVLauncher.Tests/CosmeticsServiceTests.cs
git commit -m "feat(cosmetics): write the mod configuration and clean obsolete ids"
```

---

### Task 11: Publicar al equipar, desequipar y lanzar

**Files:**
- Modify: `Services/CosmeticsService.cs`
- Test: `tests/TVLauncher.Tests/CosmeticsServiceTests.cs`

**Interfaces:**
- Consumes: `ICosmeticsPublisher.PublishCurrentAsync`.
- Produces: `CosmeticsService` calls the publisher with a 500 ms debounce on equip/unequip and once before injection.

- [ ] **Step 1: Write the failing tests**

Use the shared `FakeCosmeticsPublisher` and assert its `CallCount`.

```csharp
[Fact]
public async Task EquipCosmetic_PublishesAfterTheDebounce()
{
    using var dir = new TempDirectory();
    var config = new ConfigService(dir.Root);
    var publisher = new FakeCosmeticsPublisher();
    var service = new CosmeticsService(config, publisher) { PublishDebounce = TimeSpan.Zero };

    service.EquipCosmetic("cape_tvlauncher");
    await Task.Delay(100);

    Assert.Equal(1, publisher.CallCount);
}

[Fact]
public async Task UnequipCosmetic_PublishesAfterTheDebounce()
{
    // Same pattern with UnequipCosmetic.
}

[Fact]
public async Task InjectCosmeticsToInstanceAsync_PublishesBeforeCopyingTheMod()
{
    // Publisher.CallCount == 1 after Inject.
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore --filter "FullyQualifiedName~CosmeticsServiceTests"
```

Expected: FAIL because the publisher is not called.

- [ ] **Step 3: Implement the integration**

- Change the `CosmeticsService` constructor to accept `ICosmeticsPublisher`.
- Add `internal TimeSpan PublishDebounce { get; set; } = TimeSpan.FromMilliseconds(500);`.
- Add `private void SchedulePublish()`:
  - Cancel the previous `CancellationTokenSource`.
  - Start a task that delays `PublishDebounce`, then calls `PublishCurrentAsync`.
  - Catch `OperationCanceledException` and all other exceptions.
- Call `SchedulePublish()` at the end of `EquipCosmetic` and `UnequipCosmetic`.
- In `InjectCosmeticsToInstanceAsync`, call `await _publisher.PublishCurrentAsync()` before `SyncCosmeticsToInstanceAsync`.
- Keep the `ICosmeticsPublisher` DI registration added in Task 5.
- On `Failed`, call `StatusChanged?.Invoke(this, "No se pudieron publicar los cosméticos; el juego continuará")` and log the result. On `Published`, log success. Do not let a failed publish throw.

- [ ] **Step 4: Run the tests to verify they pass**

Run the filter from Step 2. Expected: PASS.

- [ ] **Step 5: Build and run the full suite**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build TVLauncher.csproj --no-restore --configuration Debug
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore
```

Expected: build 0 errors; all tests pass.

- [ ] **Step 6: Commit**

```powershell
git add Services/CosmeticsService.cs App.axaml.cs tests/TVLauncher.Tests/CosmeticsServiceTests.cs
git commit -m "feat(cosmetics): publish on equip, unequip and launch"
```

---

### Task 12: Documentar, compilar Release y verificar

**Files:**
- Modify: `docs/INVENTARIO.md`
- Modify: `docs/SUPABASE_SETUP.md`
- Modify: `README.md` if it documents cosmetics
- Build: `publish/win-x64/TVLauncher.exe`

**Interfaces:**
- Consumes: all previous tasks.
- Produces: release candidate and documented Supabase setup.

- [ ] **Step 1: Update the inventory**

In `docs/INVENTARIO.md`:

- Remove the dead CDN as the source of the mod.
- Document that `tvl-cosmetics.jar` is embedded in the launcher.
- Document Supabase as the global cosmetics backend.
- Document that offline accounts only render locally.
- Document that the hat can overlap a helmet in this beta.
- Document the new config fields and the setup doc.

- [ ] **Step 2: Build the mod and the launcher**

```powershell
.\scripts\build-cosmetics-mod.ps1
& "C:\Program Files\dotnet\dotnet.exe" build TVLauncher.csproj --no-restore --configuration Debug
```

Expected: mod jar copied; .NET build 0 errors.

- [ ] **Step 3: Run the full test suite**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "tests\TVLauncher.Tests\TVLauncher.Tests.csproj" --nologo --no-restore
```

Expected: all tests pass (169 existing + new).

- [ ] **Step 4: Publish the Release**

```powershell
& "C:\Program Files\dotnet\dotnet.exe" publish TVLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish/win-x64
```

Expected: `publish/win-x64/TVLauncher.exe` generated. If Windows Application Control blocks the executable on this machine, record it as an environment limitation, not a build failure.

- [ ] **Step 5: Record the hash and size**

```powershell
$exe = "publish\win-x64\TVLauncher.exe"
Get-Item $exe | Select-Object Length
Get-FileHash $exe -Algorithm SHA256
```

Expected: record both values in the final report and in `docs/INVENTARIO.md`.

- [ ] **Step 6: Verify the local in-game fallback**

1. Launch the Fabric 1.21.11 instance from the launcher.
2. Equip `cape_tvlauncher` and `hat_tvlauncher`.
3. Join a world.
4. Confirm that the local player renders both cosmetics even without Supabase configured.
5. Confirm that the log contains no mod crash.

Expected: capa y sombrero visibles en el jugador local.

- [ ] **Step 7: Verify the remote path (requires Supabase project)**

Blocked until the user provides a project URL and anon key. When available:

1. Configure Ajustes → Sincronización in-game.
2. Publish with a Microsoft account.
3. Confirm the row in `cosmetics_profiles`.
4. Join with a second launcher user.
5. Confirm both players see each other.
6. Repeat on an offline-mode server.

Expected: both players render each other's cosmetics; the row has both UUIDs.

- [ ] **Step 8: Commit**

```powershell
git add docs README.md
git commit -m "docs(cosmetics): document the Supabase backend and release state"
```

---

## Post-Plan Setup (blocked on user)

The backend cannot be deployed or verified end-to-end until a Supabase project exists. The scaffold is complete, but the beta will only sync cosmetics between players after:

1. Creating the project.
2. Running the migration.
3. Deploying `publish-cosmetics` with `--no-verify-jwt`.
4. Pasting the URL and anon key into Ajustes → Sincronización in-game.

Until then:

- the launcher compiles and tests pass;
- the mod is injected and the local player sees their own cosmetics;
- other players do not see remote cosmetics.
