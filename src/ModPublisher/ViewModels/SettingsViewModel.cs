using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModPublisher.Services;

namespace ModPublisher.ViewModels;

/// <summary>Réglages du mod, enregistrés dans son modpage/publish.json (partagé avec publish.py).</summary>
public sealed partial class SettingsViewModel(MainViewModel main) : ObservableObject
{
    private ModPage? _mod;

    [ObservableProperty] private string _modrinthProject = "";
    [ObservableProperty] private string _curseForgeProjectId = "";
    [ObservableProperty] private string _gameVersions = "";
    [ObservableProperty] private string _loaders = "";
    [ObservableProperty] private string _environments = "";
    [ObservableProperty] private string _javaVersions = "";
    [ObservableProperty] private string _releaseType = "release";
    [ObservableProperty] private string _versionName = "";
    [ObservableProperty] private string _jar = "";
    [ObservableProperty] private string _changelog = "";
    [ObservableProperty] private string _modrinthDependencies = "";
    [ObservableProperty] private string _curseForgeRelations = "";
    [ObservableProperty] private string _sourceUrl = "";
    [ObservableProperty] private string _issuesUrl = "";
    [ObservableProperty] private string _wikiUrl = "";
    [ObservableProperty] private string _discordUrl = "";
    [ObservableProperty] private string _info = "";

    public static string[] ReleaseTypes { get; } = ["release", "beta", "alpha"];

    public void Load(ModPage? mod)
    {
        _mod = mod;
        if (mod is null) return;
        mod.Reload();
        var s = mod.Settings;
        ModrinthProject = mod.ModrinthProject;
        CurseForgeProjectId = mod.CurseForgeProjectId == 0 ? "" : mod.CurseForgeProjectId.ToString();
        GameVersions = string.Join(", ", mod.GameVersions);
        Loaders = string.Join(", ", mod.Loaders);
        var cf = mod.Section("curseforge");
        Environments = string.Join(", ", mod.StringList(cf["environments"]));
        JavaVersions = string.Join(", ", mod.StringList(cf["java_versions"]));
        ReleaseType = mod.ReleaseType;
        VersionName = (string?)s["version_name"] ?? "{name} {version}";
        Jar = (string?)s["jar"] ?? "";
        Changelog = (string?)s["changelog"] ?? "modpage/changelog.md";
        ModrinthDependencies = string.Join(", ", (mod.Section("modrinth")["dependencies"] as JsonArray ?? new())
            .Select(d => $"{(string?)d!["project_id"]}:{(string?)d["dependency_type"]}"));
        CurseForgeRelations = string.Join(", ", (cf["relations"] as JsonArray ?? new())
            .Select(d => $"{(string?)d!["slug"]}:{(string?)d["type"]}"));
        var links = mod.Section("links");
        SourceUrl = (string?)links["source"] ?? "";
        IssuesUrl = (string?)links["issues"] ?? "";
        WikiUrl = (string?)links["wiki"] ?? "";
        DiscordUrl = (string?)links["discord"] ?? "";
        Info = L.T($"Version actuelle (gradle.properties) : {mod.GradleVersion() ?? "?"} · fichier : {mod.Dir}\\publish.json", $"Current version (gradle.properties): {mod.GradleVersion() ?? "?"} · file: {mod.Dir}\\publish.json");
    }

    private static JsonArray List(string text) =>
        new(text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => (JsonNode)x).ToArray());

    private static IEnumerable<(string, string)> Pairs(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split(':', 2, StringSplitOptions.TrimEntries))
            .Select(p => (p[0], p.Length > 1 ? p[1] : ""));

    [RelayCommand]
    private void Save()
    {
        if (_mod is null) return;
        var s = _mod.Settings;
        _mod.ModrinthProject = ModrinthProject;
        _mod.CurseForgeProjectId = long.TryParse(CurseForgeProjectId.Trim(), out var id) ? id : 0;
        s["game_versions"] = List(GameVersions);
        s["loaders"] = List(Loaders);
        s["release_type"] = ReleaseType;
        s["version_name"] = VersionName;
        if (Jar.Trim().Length > 0) s["jar"] = Jar.Trim(); else s.Remove("jar");
        s["changelog"] = Changelog.Trim();

        var cf = _mod.Section("curseforge");
        cf["environments"] = List(Environments);
        cf["java_versions"] = List(JavaVersions);
        cf["relations"] = new JsonArray(Pairs(CurseForgeRelations)
            .Select(p => (JsonNode)new JsonObject { ["slug"] = p.Item1, ["type"] = p.Item2.Length > 0 ? p.Item2 : "requiredDependency" }).ToArray());
        _mod.Section("modrinth")["dependencies"] = new JsonArray(Pairs(ModrinthDependencies)
            .Select(p => (JsonNode)new JsonObject { ["project_id"] = p.Item1, ["dependency_type"] = p.Item2.Length > 0 ? p.Item2 : "required" }).ToArray());

        var links = _mod.Section("links");
        links["source"] = SourceUrl.Trim();
        links["issues"] = IssuesUrl.Trim();
        links["wiki"] = WikiUrl.Trim();
        links["discord"] = DiscordUrl.Trim();

        _mod.SaveSettings();
        main.Log(L.T($"Réglages enregistrés : {_mod.Dir}\\publish.json", $"Settings saved: {_mod.Dir}\\publish.json"));
    }
}
