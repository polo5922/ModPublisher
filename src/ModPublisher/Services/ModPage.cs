using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ModPublisher.Services;

/// <summary>
/// Un mod sur le disque : son dossier Gradle et son sous-dossier modpage/ (description, résumé, changelog, images,
/// publish.json partagé avec modpage/publish.py).
/// </summary>
public sealed class ModPage
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public string Folder { get; }
    public string Dir => Path.Combine(Folder, "modpage");
    public JsonObject Settings { get; private set; } = new();

    public ModPage(string folder)
    {
        Folder = folder;
        Reload();
    }

    public void Reload()
    {
        var path = Path.Combine(Dir, "publish.json");
        Settings = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path))!.AsObject()
            : DefaultSettings();
    }

    public void SaveSettings()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, "publish.json"), Settings.ToJsonString(Indented) + "\n");
    }

    // ---------- Identité ----------

    private string FabricModJsonPath => Path.Combine(Folder, "src", "main", "resources", "fabric.mod.json");

    /// <summary>Un champ texte de fabric.mod.json, ou null.</summary>
    private string? FabricField(string field)
    {
        if (!File.Exists(FabricModJsonPath)) return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(FabricModJsonPath))?[field] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
        }
        catch (JsonException) { return null; }
    }

    public string Name => FabricField("name") ?? Path.GetFileName(Folder.TrimEnd('\\', '/'));

    public string ModrinthProject
    {
        get => (string?)Settings["modrinth"]?["project"] ?? "";
        set => Section("modrinth")["project"] = value.Trim();
    }

    public long CurseForgeProjectId
    {
        // Accepte un nombre de n'importe quel type (int en mémoire, long lu du fichier) ou un texte
        get => long.TryParse(Settings["curseforge"]?["project_id"]?.ToString(), out var id) ? id : 0;
        set => Section("curseforge")["project_id"] = value;
    }

    public JsonObject Section(string name)
    {
        if (Settings[name] is not JsonObject o)
        {
            o = new JsonObject();
            Settings[name] = o;
        }
        return o;
    }

    public List<string> StringList(JsonNode? node) =>
        node is JsonArray a ? a.Select(x => (string?)x ?? "").Where(x => x.Length > 0).ToList() : new();

    public List<string> GameVersions => StringList(Settings["game_versions"]);
    public List<string> Loaders => StringList(Settings["loaders"]);
    public string ReleaseType => (string?)Settings["release_type"] ?? "release";

    // ---------- Gradle ----------

    public string? GradleProperty(string key)
    {
        var path = Path.Combine(Folder, "gradle.properties");
        if (!File.Exists(path)) return null;
        var m = Regex.Match(File.ReadAllText(path), @"^" + Regex.Escape(key) + @"\s*=\s*(\S+)", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : null;
    }

    public string? GradleVersion() => GradleProperty("version") ?? GradleProperty("mod_version");

    public string VersionName(string version) => ((string?)Settings["version_name"] ?? "{name} {version}")
        .Replace("{name}", Name).Replace("{version}", version);

    public string JarPath(string version)
    {
        var pattern = (string?)Settings["jar"] ?? $"build/libs/{ArchiveName()}-{{version}}.jar";
        return Path.GetFullPath(Path.Combine(Folder, pattern.Replace("{version}", version)));
    }

    private string ArchiveName()
    {
        if (GradleProperty("archives_base_name") is { } n) return n;
        var settings = Path.Combine(Folder, "settings.gradle");
        if (File.Exists(settings))
        {
            var m = Regex.Match(File.ReadAllText(settings), @"rootProject\.name\s*=\s*['""]([^'""]+)");
            if (m.Success) return m.Groups[1].Value;
        }
        return Path.GetFileName(Folder);
    }

    // ---------- Fichiers de la page ----------

    public string DescriptionPath => Path.Combine(Dir, "description.md");
    public string LegacyModrinthDescriptionPath => Path.Combine(Dir, "description-modrinth.md");
    public string SummaryPath => Path.Combine(Dir, "summary.txt");
    public string ChangelogPath => Path.GetFullPath(Path.Combine(Folder, (string?)Settings["changelog"] ?? "modpage/changelog.md"));
    /// <summary>Icône de la page (modpage/images/icon.png), là où « Choisir l'icône » l'enregistre.</summary>
    public string PageIconPath => Path.Combine(Dir, "images", "icon.png");

    /// <summary>Icône à afficher : celle de la page, sinon celle déclarée dans fabric.mod.json, sinon null.</summary>
    public string? IconPath
    {
        get
        {
            if (File.Exists(PageIconPath)) return PageIconPath;
            if (FabricField("icon") is not { } icon) return null;
            var path = Path.Combine(Path.GetDirectoryName(FabricModJsonPath)!, icon.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? path : null;
        }
    }
    public string GalleryDir => Path.Combine(Dir, "images", "gallery");
    public string ImageUrlsPath => Path.Combine(Dir, "image-urls.json");

    public string ReadDescription() => ReadText(DescriptionPath);
    public void WriteDescription(string text) => WriteText(DescriptionPath, text);
    public string ReadSummary() => ReadText(SummaryPath).Trim();
    public void WriteSummary(string text) => WriteText(SummaryPath, text.Trim() + "\n");

    public Dictionary<string, string> ImageUrls() =>
        File.Exists(ImageUrlsPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ImageUrlsPath)) ?? new()
            : new();

    private static string ReadText(string path) => File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : "";

    private void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(path, text.Replace("\r\n", "\n"));
    }

    // ---------- Réglages par défaut d'un nouveau mod ----------

    private JsonObject DefaultSettings()
    {
        var res = Path.Combine(Folder, "src", "main", "resources");
        var loader = File.Exists(Path.Combine(res, "fabric.mod.json")) ? "fabric"
            : File.Exists(Path.Combine(res, "META-INF", "neoforge.mods.toml")) ? "neoforge"
            : File.Exists(Path.Combine(res, "META-INF", "mods.toml")) ? "forge"
            : File.Exists(Path.Combine(res, "quilt.mod.json")) ? "quilt" : "fabric";
        var mc = GradleProperty("minecraft_version");

        var modrinthDeps = new JsonArray();
        var cfRelations = new JsonArray();
        if (loader == "fabric")
        {
            modrinthDeps.Add(new JsonObject { ["project_id"] = "P7dR8mSH", ["dependency_type"] = "required", ["_note"] = "Fabric API" });
            cfRelations.Add(new JsonObject { ["slug"] = "fabric-api", ["type"] = "requiredDependency" });
        }

        return new JsonObject
        {
            ["changelog"] = "modpage/changelog.md",
            ["version_name"] = "{name} {version}",
            ["release_type"] = "release",
            ["game_versions"] = mc is null ? new JsonArray() : new JsonArray(mc),
            ["loaders"] = new JsonArray(loader),
            ["modrinth"] = new JsonObject { ["project"] = "", ["featured"] = true, ["dependencies"] = modrinthDeps },
            ["curseforge"] = new JsonObject
            {
                ["project_id"] = 0L,
                ["environments"] = new JsonArray("Client", "Server"),
                ["java_versions"] = new JsonArray(),
                ["relations"] = cfRelations,
            },
        };
    }
}
