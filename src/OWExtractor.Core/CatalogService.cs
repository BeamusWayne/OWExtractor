using System.Text.Json;

namespace OWExtractor.Core;

public static class CatalogService
{
    public static List<HeroAsset> ParseHeroes(string json, IReadOnlyList<HeroAsset>? existing = null)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("英雄 JSON 顶层应为对象。");
        var result = new List<HeroAsset>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Object || !p.Value.TryGetProperty("Name", out var name) || name.ValueKind != JsonValueKind.String) continue;
            if (p.Value.TryGetProperty("IsHero", out var isHero) && isHero.ValueKind == JsonValueKind.False) continue;
            var original = name.GetString()!;
            if (string.IsNullOrWhiteSpace(original)) continue;
            var guid = String(p.Value, "GUID") ?? p.Name;
            var old = existing?.FirstOrDefault(h => h.Guid == guid);
            result.Add(new HeroAsset {
                Guid = guid, Name = old?.Name ?? original, SelectorName = original,
                Role = (String(p.Value, "Class") ?? "") switch { "Tank" => "重装", "Damage" => "输出", "Support" => "支援", _ => "英雄" },
                Skins = old?.Skins ?? []
            });
        }
        if (result.Count == 0) throw new InvalidDataException("文件中没有可识别的英雄记录。");
        return result;
    }

    public static void ApplyLocalizedNames(List<HeroAsset> heroes, string json)
    {
        var localized = ParseHeroes(json);
        foreach (var hero in heroes)
            if (localized.FirstOrDefault(x => x.Guid == hero.Guid) is { } match) hero.Name = match.Name;
    }

    public static int ImportUnlocks(List<HeroAsset> heroes, string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("皮肤 JSON 顶层应为对象。");
        var count = 0;
        foreach (var heroProperty in doc.RootElement.EnumerateObject())
        {
            var hero = heroes.FirstOrDefault(h => h.SelectorName == heroProperty.Name || h.Name == heroProperty.Name);
            if (hero is null) continue;
            Visit(heroProperty.Value, hero);
        }
        if (count == 0) throw new InvalidDataException("没有匹配的皮肤。请使用与英雄列表相同资源语言的 list-unlocks JSON。");
        return count;

        void Visit(JsonElement node, HeroAsset hero)
        {
            if (node.ValueKind == JsonValueKind.Array) { foreach (var item in node.EnumerateArray()) Visit(item, hero); return; }
            if (node.ValueKind != JsonValueKind.Object) return;
            if (String(node, "Type") == "Skin" && String(node, "Name") is { Length: > 0 } name)
            {
                var key = String(node, "GUID") ?? name;
                var skin = hero.Skins.FirstOrDefault(s => s.Key == key || s.SelectorName == name);
                if (skin is null) { skin = new SkinAsset(); hero.Skins.Add(skin); }
                skin.Key = key; skin.SelectorName = name; skin.Name = name; count++;
                return;
            }
            foreach (var child in node.EnumerateObject()) Visit(child.Value, hero);
        }
    }

    // This import only enumerates a user-selected existing export, never a game install.
    public static int ImportExportFolder(List<HeroAsset> heroes, string root, CancellationToken token = default)
    {
        EnsureExportPath(root);
        var heroRoot = Path.GetFileName(Path.TrimEndingDirectorySeparator(root)).Equals("Heroes", StringComparison.OrdinalIgnoreCase)
            ? root : Path.Combine(root, "Heroes");
        if (!Directory.Exists(heroRoot)) throw new InvalidDataException("请选择包含 Heroes 文件夹的既有 DataTool 导出目录。");
        EnsureExportPath(heroRoot);
        var count = 0;
        var shallow = new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        var recursive = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        foreach (var heroDir in Directory.EnumerateDirectories(heroRoot, "*", shallow))
        {
            token.ThrowIfCancellationRequested();
            var heroName = Path.GetFileName(heroDir);
            var hero = heroes.FirstOrDefault(h => h.SelectorName == heroName || h.Name == heroName);
            if (hero is null) { hero = new HeroAsset { Guid = "export:" + heroName, Name = heroName, SelectorName = heroName, Role = "英雄" }; heroes.Add(hero); }
            var skinsRoot = Path.Combine(heroDir, "Skin");
            if (!Directory.Exists(skinsRoot)) continue;
            EnsureExportPath(skinsRoot);
            foreach (var category in Directory.EnumerateDirectories(skinsRoot, "*", shallow))
            foreach (var skinDir in Directory.EnumerateDirectories(category, "*", shallow))
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileName(skinDir);
                var skin = hero.Skins.FirstOrDefault(s => s.SelectorName == name);
                if (skin is null) { skin = new SkinAsset { Key = "export:" + name, Name = name, SelectorName = name }; hero.Skins.Add(skin); }
                skin.ExportDirectory = Path.GetFullPath(skinDir);
                skin.FileCount = 0; skin.ModelCount = 0; skin.Bytes = 0; skin.ExtensionCounts.Clear();
                foreach (var file in Directory.EnumerateFiles(skinDir, "*", recursive))
                {
                    token.ThrowIfCancellationRequested();
                    if (++skin.FileCount > 200000) throw new InvalidDataException("单个皮肤文件数超过导入上限，请缩小目录范围。");
                    var info = new FileInfo(file); skin.Bytes += info.Length;
                    var extension = info.Extension.ToLowerInvariant();
                    skin.ExtensionCounts[extension] = skin.ExtensionCounts.GetValueOrDefault(extension) + 1;
                    if (info.Extension.Equals(".owmdl", StringComparison.OrdinalIgnoreCase)) skin.ModelCount++;
                }
                count++;
            }
        }
        if (count == 0) throw new InvalidDataException("未找到 Heroes/英雄/Skin/分类/皮肤 导出结构。");
        return count;
    }

    public static void EnsureExportPath(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidDataException("请选择既有导出目录。");
        var parts = Path.GetFullPath(root).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(p => p.Equals("Overwatch", StringComparison.OrdinalIgnoreCase) || p.Equals("_retail_", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("game-view-", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("此目录可能是游戏安装或存储目录；离线导入只接受之前导出的 Heroes 目录。");
        for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("请选择实际导出目录，不使用目录链接。");
    }

    private static string? String(JsonElement obj, string property) => obj.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
