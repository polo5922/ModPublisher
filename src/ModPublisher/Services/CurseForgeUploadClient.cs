using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace ModPublisher.Services;

/// <summary>
/// API officielle d'upload CurseForge (jeton X-Api-Token, authors.curseforge.com > API Tokens).
/// Elle ne sait qu'envoyer des fichiers : tout le reste passe par <see cref="CurseForgeSite"/>.
/// </summary>
public sealed class CurseForgeUploadClient(ApiHttp http, Func<string?> token)
{
    public const string Api = "https://minecraft.curseforge.com/api";

    public bool HasToken => !string.IsNullOrEmpty(token());

    private HttpRequestMessage Req(HttpMethod method, string path, HttpContent? content = null)
    {
        var t = token() ?? throw new ApiException(L.T("Jeton d'upload CurseForge manquant (onglet Comptes)", "CurseForge upload token missing (Accounts tab)"));
        var req = new HttpRequestMessage(method, Api + path) { Content = content };
        req.Headers.TryAddWithoutValidation("X-Api-Token", t);
        return req;
    }

    public record GameVersion(long Id, string Name, string TypeSlug);

    private List<GameVersion>? _versions;

    public async Task<List<GameVersion>> GameVersionsAsync()
    {
        if (_versions is not null) return _versions;
        var types = (await http.SendAsync(Req(HttpMethod.Get, "/game/version-types")))!.AsArray()
            .ToDictionary(t => (long)t!["id"]!, t => (string?)t!["slug"] ?? "");
        _versions = (await http.SendAsync(Req(HttpMethod.Get, "/game/versions")))!.AsArray()
            .Select(v => new GameVersion((long)v!["id"]!, (string?)v["name"] ?? "", types.GetValueOrDefault((long)v["gameVersionTypeID"]!, "")))
            .ToList();
        return _versions;
    }

    /// <summary>Traduit « 26.3 », « Fabric », « Client », « Java 25 » en IDs CurseForge.</summary>
    public static List<long> ResolveIds(IReadOnlyList<GameVersion> all, IEnumerable<string> minecraft, IEnumerable<string> loaders,
        IEnumerable<string> environments, IEnumerable<string> java)
    {
        long Find(string name, Func<string, bool> typeOk) =>
            all.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase) && typeOk(v.TypeSlug))?.Id
            ?? throw new ApiException(L.T($"Version inconnue de CurseForge : « {name} »", $"Version unknown to CurseForge: “{name}”"));

        return minecraft.Select(n => Find(n, s => s.StartsWith("minecraft-")))
            .Concat(loaders.Select(n => Find(n, s => s == "modloader")))
            .Concat(environments.Select(n => Find(n, s => s == "environment")))
            .Concat(java.Select(n => Find(n, s => s == "java")))
            .ToList();
    }

    public async Task<List<long>> ResolveIdsAsync(ModPage mod)
    {
        var cf = mod.Section("curseforge");
        return ResolveIds(await GameVersionsAsync(), mod.GameVersions, mod.Loaders,
            mod.StringList(cf["environments"]), mod.StringList(cf["java_versions"]));
    }

    /// <summary>Renvoie l'ID du fichier créé (null en simulation).</summary>
    public async Task<long?> UploadAsync(long projectId, string displayName, string changelog, string releaseType,
        IEnumerable<long> gameVersionIds, JsonArray? relations, string jarPath)
    {
        var metadata = new JsonObject
        {
            ["changelog"] = changelog,
            ["changelogType"] = "markdown",
            ["displayName"] = displayName,
            ["gameVersions"] = new JsonArray(gameVersionIds.Select(id => (JsonNode)id).ToArray()),
            ["releaseType"] = releaseType,
        };
        if (relations is { Count: > 0 })
            metadata["relations"] = new JsonObject { ["projects"] = relations.DeepClone() };

        var bytes = await File.ReadAllBytesAsync(jarPath);
        var res = await http.SendAsync(Req(HttpMethod.Post, $"/projects/{projectId}/upload-file",
            ApiHttp.Multipart("metadata", metadata, Path.GetFileName(jarPath), bytes)));
        return (long?)res?["id"];
    }
}
