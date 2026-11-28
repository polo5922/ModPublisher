using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Markdig;

namespace ModPublisher.Services;

public record CfProjectInfo(string Description, string Summary, Dictionary<string, string> Links, string? IconUrl);

public record CfFile(long Id, string DisplayName, string FileName, string ReleaseType, List<string> GameVersions,
    DateTime Date, long Downloads, string? Changelog);

public record CfProjectSummary(long Id, string Name, string Slug, long Downloads, DateTime Created, DateTime? LastRelease);

public record CfImage(long Id, string Url, string Title, string Description);

/// <summary>
/// Opérations de la page projet CurseForge via l'API interne de authors.curseforge.com (/_api/…), retrouvée dans le
/// code du site. Les écritures reprennent exactement ce que fait le site : on relit l'objet complet, on change les
/// champs voulus et on renvoie le tout.
/// </summary>
public sealed class CurseForgeAuthors(CurseForgeSite site)
{
    private const string Api = "/_api";

    // Types de texte du site : 1 = éditeur visuel (HTML), 5 = Markdown
    private const int Html = 1, Markdown = 5;

    // Types de liens sociaux du site (énum A5 du site)
    private static readonly Dictionary<string, int> SocialTypes = new()
    {
        ["discord"] = 2, ["website"] = 3, ["source"] = 13,
    };

    public CurseForgeSite Site { get; } = site;

    private Task<JsonNode?> Get(string path) => Site.FetchAsync("GET", Api + path);

    /// <summary>Nom du compte connecté ; la session qui marche est gardée pour le prochain lancement.</summary>
    public async Task<string?> GetUsernameAsync()
    {
        var name = (await Get("/users/user-data") as JsonObject)?["displayName"]?.GetValue<string>();
        if (name is not null) await Site.SaveSessionAsync();
        return name;
    }

    /// <summary>Projets du compte, avec leurs téléchargements (la liste « Projects » du site).</summary>
    public async Task<List<CfProjectSummary>> GetMyProjectsAsync()
    {
        var list = await Get($"/projects?filter={{}}&range={Uri.EscapeDataString("[0,99]")}&sort={Uri.EscapeDataString("[\"name\",\"ASC\"]")}");
        return (list as JsonArray ?? new()).OfType<JsonObject>().Select(p => new CfProjectSummary(
            (long?)p["id"] ?? 0, (string?)p["name"] ?? "", (string?)p["slug"] ?? "", (long?)p["totalDownloads"] ?? 0,
            Date(p["creationDate"]) ?? DateTime.MinValue, Date(p["dateLastReleasedFile"]))).ToList();
    }

    private static DateTime? Date(JsonNode? node) => DateTime.TryParse((string?)node, out var d) ? d.ToLocalTime() : null;

    // ---------- Page du projet ----------

    public async Task<CfProjectInfo> GetProjectAsync(long projectId)
    {
        var project = await Get($"/projects/{projectId}") ?? throw new ApiException(L.T("CurseForge : projet introuvable", "CurseForge: project not found"));
        var description = await Get($"/projects/description/{projectId}");
        var links = new Dictionary<string, string>();
        foreach (var link in await SocialLinksAsync(projectId))
            if (SocialTypes.FirstOrDefault(t => t.Value == (int?)link?["type"]).Key is { } key && (string?)link?["url"] is { Length: > 0 } url)
                links[key] = url;
        if ((string?)project["wikiUrl"] is { Length: > 0 } wiki) links["wiki"] = wiki;
        return new CfProjectInfo((string?)description?["Description"] ?? "", (string?)project["summary"] ?? "", links,
            (string?)project["avatarUrl"]);
    }

    private async Task<JsonArray> SocialLinksAsync(long projectId)
    {
        try { return (await Get($"/projects/social-links/{projectId}"))?["links"] as JsonArray ?? new(); }
        catch (ApiException e) when (e.Status == 404) { return new(); }
    }

    /// <summary>La description est envoyée en Markdown (type 5, que le site accepte pour ce projet).</summary>
    public Task UpdateDescriptionAsync(long projectId, string markdown) =>
        Site.FetchAsync("PUT", $"{Api}/projects/description/{projectId}",
            new JsonObject { ["description"] = markdown, ["descriptionType"] = Markdown });

    public Task UpdateSummaryAsync(long projectId, string summary) =>
        UpdateDetailsAsync(projectId, d => d["summary"] = summary);

    /// <summary>Liens des réglages : discord, website et source deviennent des liens sociaux, wiki le champ wikiUrl.</summary>
    public Task UpdateLinksAsync(long projectId, Dictionary<string, string> links) =>
        UpdateDetailsAsync(projectId, d =>
        {
            var social = (JsonArray)d["links"]!;
            foreach (var (key, type) in SocialTypes)
            {
                if (!links.TryGetValue(key, out var url) || url.Length == 0) continue;
                var existing = social.FirstOrDefault(l => (int?)l?["type"] == type);
                if (existing is not null) existing["url"] = url;
                else social.Add(new JsonObject { ["type"] = type, ["url"] = url });
            }
            if (links.TryGetValue("wiki", out var wiki) && wiki.Length > 0) d["wikiUrl"] = wiki;
        });

    /// <summary>« update-details » remplace tous les réglages généraux : on part des valeurs actuelles.</summary>
    private async Task UpdateDetailsAsync(long projectId, Action<JsonObject> change)
    {
        var p = await Get($"/projects/{projectId}") ?? throw new ApiException(L.T("CurseForge : projet introuvable", "CurseForge: project not found"));
        var details = new JsonObject
        {
            ["summary"] = p["summary"]?.DeepClone(),
            ["slug"] = p["slug"]?.DeepClone(),
            ["name"] = p["name"]?.DeepClone(),
            ["avatarUrl"] = p["avatarUrl"]?.DeepClone(),
            ["primaryCategoryId"] = p["primaryCategoryId"]?.DeepClone(),
            ["subCategoryIds"] = p["subCategoryIds"]?.DeepClone() ?? new JsonArray(),
            ["allowComments"] = p["allowComments"]?.DeepClone(),
            ["enableProjectPages"] = p["enableProjectPages"]?.DeepClone(),
            ["wikiUrl"] = (string?)p["wikiUrl"] ?? "",
            ["donationTypeId"] = p["donationTypeId"]?.DeepClone(),
            ["donationIdentifier"] = (string?)p["donationIdentifier"] ?? "",
            ["links"] = (await SocialLinksAsync(projectId)).DeepClone(),
            ["unlisted"] = p["unlisted"]?.DeepClone(),
            ["isFreemium"] = p["isFreemium"]?.DeepClone(),
        };
        change(details);
        await Site.FetchAsync("PUT", $"{Api}/projects/{projectId}/update-details", details);
    }

    /// <summary>Envoi du logo (100 Ko au plus côté site), puis enregistrement de sa nouvelle adresse.</summary>
    public async Task SetIconAsync(long projectId, string imagePath)
    {
        var data = await File.ReadAllBytesAsync(imagePath);
        if (data.Length > 102400)
            throw new InvalidOperationException(L.T($"CurseForge refuse les logos de plus de 100 Ko ({data.Length / 1024} Ko ici).", $"CurseForge rejects logos over 100 KB (this one is {data.Length / 1024} KB)."));
        var result = await Site.UploadAsync($"{Api}/projects/{projectId}/upload-avatar", "file", Path.GetFileName(imagePath), data,
            MimeType(imagePath), new Dictionary<string, string> { ["id"] = projectId.ToString() });
        if (result is null) return; // simulation
        var url = result is JsonValue v ? ((string?)v)?.Trim('"') : (string?)result["url"] ?? (string?)result["avatarUrl"];
        if (string.IsNullOrEmpty(url)) throw new ApiException(L.T("CurseForge : réponse inattendue à l'envoi du logo : ", "CurseForge: unexpected response when sending the logo: ") + result.ToJsonString());
        await UpdateDetailsAsync(projectId, d => d["avatarUrl"] = url);
    }

    // ---------- Fichiers ----------

    public async Task<List<CfFile>> GetFilesAsync(long projectId)
    {
        var filter = Uri.EscapeDataString($"{{\"projectId\":{projectId}}}");
        var list = await Get($"/project-files?filter={filter}&range={Uri.EscapeDataString("[0,99]")}&sort={Uri.EscapeDataString("[\"id\",\"DESC\"]")}");
        return (list as JsonArray ?? new()).Select(f => ToFile(f!)).ToList();
    }

    private static CfFile ToFile(JsonNode f) => new(
        (long)f["id"]!, (string?)f["displayName"] ?? "", (string?)f["fileName"] ?? "",
        (int?)f["releaseType"] switch { 2 => "beta", 3 => "alpha", _ => "release" },
        // Seules les versions de Minecraft (les étiquettes Fabric, Java 25, Client… sont reconstruites à l'envoi)
        (f["gameVersions"] as JsonArray ?? new()).Select(v => (string?)v?["Label"] ?? "").Where(l => Regex.IsMatch(l, @"^\d+(\.\d+)+")).ToList(),
        DateTime.TryParse((string?)f["dateCreated"], out var date) ? date.ToLocalTime() : DateTime.MinValue,
        (long?)f["downloads"] ?? 0, (string?)f["changelog"]);

    public async Task<string?> GetFileChangelogAsync(long projectId, long fileId) =>
        (string?)(await Get($"/project-files/{projectId}/files/{fileId}"))?["changelog"];

    /// <summary>
    /// Comme le site : le fichier complet est relu puis renvoyé modifié. Le changelog Markdown est converti en HTML
    /// (le site fait de même avant d'enregistrer).
    /// </summary>
    public async Task UpdateFileAsync(long projectId, long fileId, string changelog, string releaseType, List<long> gameVersionIds)
    {
        var file = await Get($"/project-files/{projectId}/files/{fileId}") as JsonObject
                   ?? throw new ApiException(L.T($"CurseForge : fichier {fileId} introuvable", $"CurseForge: file {fileId} not found"));
        file["changelog"] = Markdig.Markdown.ToHtml(changelog, Pipeline);
        file["changelogType"] = Html;
        file["releaseType"] = releaseType switch { "beta" => 2, "alpha" => 3, _ => 1 };
        file["gameVersions"] = new JsonArray(gameVersionIds.Select(id => (JsonNode)id).ToArray());
        file["includeDefaultProjectRelations"] = false;
        file["addedProjectRelations"] = new JsonArray();
        file["removedProjectRelations"] = new JsonArray();
        await Site.FetchAsync("PUT", $"{Api}/project-files/{projectId}/files/{fileId}", file);
    }

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseAutoLinks().Build();

    // ---------- Galerie ----------

    private async Task<List<JsonObject>> ImagesAsync(long projectId) =>
        ((await Get($"/image-attachments/{projectId}")) as JsonArray ?? new()).OfType<JsonObject>().ToList();

    public async Task<List<CfImage>> GetImagesAsync(long projectId) =>
        (await ImagesAsync(projectId)).Where(i => (int?)i["type"] != 2).OrderBy(i => (int?)i["displayIndex"] ?? 0)
            .Select(i => new CfImage((long)i["id"]!, (string?)i["url"] ?? "", (string?)i["title"] ?? "", (string?)i["description"] ?? ""))
            .ToList();

    /// <summary>Envoi de l'image (le site lui donne le nom du fichier comme titre), puis titre et description.</summary>
    public async Task AddImageAsync(long projectId, string imagePath, string title, string description)
    {
        var result = await Site.UploadAsync($"{Api}/image-attachments/image/{projectId}", "files", Path.GetFileName(imagePath),
            await File.ReadAllBytesAsync(imagePath), MimeType(imagePath), new Dictionary<string, string> { ["id"] = projectId.ToString() });
        if (result is null) return; // simulation
        var first = (result["results"] as JsonArray)?.FirstOrDefault()
                    ?? throw new ApiException(L.T("CurseForge : réponse inattendue à l'envoi d'image : ", "CurseForge: unexpected response when sending the image: ") + result.ToJsonString());
        if ((bool?)first["success"] != true) throw new ApiException(L.T($"CurseForge refuse l'image : {(string?)first["error"]}", $"CurseForge rejected the image: {(string?)first["error"]}"));
        await UpdateImageAsync(projectId, (long)first["id"]!, title, description);
    }

    public async Task UpdateImageAsync(long projectId, long imageId, string title, string description)
    {
        var image = (await ImagesAsync(projectId)).FirstOrDefault(i => (long?)i["id"] == imageId)
                    ?? throw new ApiException(L.T($"CurseForge : image {imageId} introuvable", $"CurseForge: image {imageId} not found"));
        image["title"] = title;
        image["description"] = description;
        await Site.FetchAsync("PUT", $"{Api}/image-attachments/{projectId}", image);
    }

    public Task DeleteImageAsync(long projectId, long imageId) =>
        Site.FetchAsync("DELETE", $"{Api}/image-attachments/{projectId}/{imageId}/1");

    private static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/png",
    };
}
