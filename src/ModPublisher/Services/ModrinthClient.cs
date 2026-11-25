using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace ModPublisher.Services;

/// <summary>API officielle Modrinth v2 (https://docs.modrinth.com/api/).</summary>
public sealed class ModrinthClient(ApiHttp http, Func<string?> token)
{
    public const string Api = "https://api.modrinth.com/v2";

    public bool HasToken => !string.IsNullOrEmpty(token());

    private HttpRequestMessage Req(HttpMethod method, string path, HttpContent? content = null, bool auth = true)
    {
        var req = new HttpRequestMessage(method, Api + path) { Content = content };
        if (auth && token() is { } t) req.Headers.TryAddWithoutValidation("Authorization", t);
        else if (auth && method != HttpMethod.Get) throw new ApiException(L.T("Pas connecté à Modrinth (onglet Comptes)", "Not signed in to Modrinth (Accounts tab)"));
        return req;
    }

    private Task<JsonNode?> Send(HttpRequestMessage req) => http.SendAsync(req);

    // ---------- Compte ----------

    public async Task<string> GetUsernameAsync() =>
        (string?)(await Send(Req(HttpMethod.Get, "/user")))?["username"] ?? "?";

    // ---------- Projet ----------

    public async Task<JsonObject> GetProjectAsync(string idOrSlug)
    {
        try
        {
            return (await Send(Req(HttpMethod.Get, $"/project/{Uri.EscapeDataString(idOrSlug)}")))!.AsObject();
        }
        catch (ApiException e) when (e.Status == 404)
        {
            throw new ApiException(L.T($"Projet Modrinth « {idOrSlug} » introuvable (pas encore créé, ou brouillon et pas connecté ?)", $"Modrinth project “{idOrSlug}” not found (not created yet, or a draft while signed out?)"), 404);
        }
    }

    /// <summary>Champs possibles : body (description longue), description (résumé), source_url, issues_url, wiki_url, discord_url…</summary>
    public Task<JsonNode?> ModifyProjectAsync(string projectId, JsonObject patch) =>
        Send(Req(HttpMethod.Patch, $"/project/{projectId}", ApiHttp.Json(patch)));

    public Task<JsonNode?> SetIconAsync(string projectId, string imagePath)
    {
        var ext = Path.GetExtension(imagePath).TrimStart('.').ToLowerInvariant();
        return Send(Req(HttpMethod.Patch, $"/project/{projectId}/icon?ext={ext}", Image(imagePath)));
    }

    // ---------- Galerie ----------

    public Task<JsonNode?> AddGalleryImageAsync(string projectId, string imagePath, bool featured, string? title, string? description, int? ordering = null)
    {
        var ext = Path.GetExtension(imagePath).TrimStart('.').ToLowerInvariant();
        var query = Query(("ext", ext), ("featured", featured ? "true" : "false"), ("title", title), ("description", description), ("ordering", ordering?.ToString()));
        return Send(Req(HttpMethod.Post, $"/project/{projectId}/gallery{query}", Image(imagePath)));
    }

    public Task<JsonNode?> ModifyGalleryImageAsync(string projectId, string url, bool featured, string? title, string? description, int? ordering = null)
    {
        var query = Query(("url", url), ("featured", featured ? "true" : "false"), ("title", title), ("description", description), ("ordering", ordering?.ToString()));
        return Send(Req(HttpMethod.Patch, $"/project/{projectId}/gallery{query}"));
    }

    public Task<JsonNode?> DeleteGalleryImageAsync(string projectId, string url) =>
        Send(Req(HttpMethod.Delete, $"/project/{projectId}/gallery{Query(("url", url))}"));

    // ---------- Versions ----------

    public async Task<JsonArray> GetVersionsAsync(string projectId) =>
        (await Send(Req(HttpMethod.Get, $"/project/{projectId}/version")))?.AsArray() ?? new JsonArray();

    /// <summary>data : name, version_number, changelog, dependencies, game_versions, loaders, version_type, featured.</summary>
    public async Task<JsonNode?> CreateVersionAsync(string projectId, JsonObject data, string jarPath)
    {
        data["project_id"] = projectId;
        data["file_parts"] = new JsonArray("file");
        data["primary_file"] = "file";
        var bytes = await File.ReadAllBytesAsync(jarPath);
        return await Send(Req(HttpMethod.Post, "/version", ApiHttp.Multipart("data", data, Path.GetFileName(jarPath), bytes)));
    }

    /// <summary>Champs possibles : name, changelog, version_type, game_versions, loaders, featured, status…</summary>
    public Task<JsonNode?> ModifyVersionAsync(string versionId, JsonObject patch) =>
        Send(Req(HttpMethod.Patch, $"/version/{versionId}", ApiHttp.Json(patch)));

    // ---------- Tags ----------

    public async Task<HashSet<string>> GameVersionTagsAsync() =>
        (await Send(Req(HttpMethod.Get, "/tag/game_version", auth: false)))!.AsArray()
            .Select(v => (string)v!["version"]!).ToHashSet();

    public async Task<HashSet<string>> LoaderTagsAsync() =>
        (await Send(Req(HttpMethod.Get, "/tag/loader", auth: false)))!.AsArray()
            .Select(v => (string)v!["name"]!).ToHashSet();

    // ---------- Outils ----------

    private static ByteArrayContent Image(string path)
    {
        var content = new ByteArrayContent(File.ReadAllBytes(path));
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        content.Headers.ContentType = new MediaTypeHeaderValue(ext == "jpg" ? "image/jpeg" : "image/" + ext);
        return content;
    }

    private static string Query(params (string Key, string? Value)[] items)
    {
        var parts = items.Where(i => !string.IsNullOrEmpty(i.Value))
            .Select(i => $"{i.Key}={Uri.EscapeDataString(i.Value!)}");
        return "?" + string.Join("&", parts);
    }
}
