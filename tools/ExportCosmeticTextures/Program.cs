using TVLauncher.Services;

string output = args.Length > 0
    ? args[0]
    : Path.Combine("fabric-mod", "src", "main", "resources", "assets", "tvl-cosmetics", "textures");

Directory.CreateDirectory(output);
File.WriteAllBytes(Path.Combine(output, "cape_tvlauncher.png"), CosmeticTextureFactory.CreateCapePng());
File.WriteAllBytes(Path.Combine(output, "hat_tvlauncher.png"), CosmeticTextureFactory.CreateHatPng());
Console.WriteLine($"Texturas escritas en {output}");
