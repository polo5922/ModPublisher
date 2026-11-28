using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModPublisher.Services;

namespace ModPublisher.ViewModels;

/// <summary>Une barre du graphique : part Modrinth, part CurseForge, et le vide jusqu'à la plus grande barre.</summary>
public sealed class StatBar(SiteSplit split, long max, string? detail = null)
{
    public string Label { get; } = split.Label;
    public string? Detail { get; } = detail;
    public long Modrinth { get; } = split.Modrinth;
    public long CurseForge { get; } = split.CurseForge;
    public long Total { get; } = split.Total;
    public GridLength ModrinthWidth { get; } = new(split.Modrinth, GridUnitType.Star);
    public GridLength CurseForgeWidth { get; } = new(split.CurseForge, GridUnitType.Star);
    public GridLength RestWidth { get; } = new(Math.Max(0, max - split.Total), GridUnitType.Star);
    public string Tooltip { get; } = L.T($"Modrinth : {split.Modrinth:N0}\nCurseForge : {split.CurseForge:N0}",
        $"Modrinth: {split.Modrinth:N0}\nCurseForge: {split.CurseForge:N0}");
}

/// <summary>Un projet du compte, sur l'un des sites.</summary>
public sealed record ProjectStat(string Name, string Site, long Downloads, long? Followers, DateTime? Created, DateTime? Updated, string Url, bool IsCurrent)
{
    public string FollowersText => Followers?.ToString("N0") ?? "—";
}

public sealed partial class StatsViewModel(MainViewModel main) : ObservableObject
{
    private AppServices S => main.S;
    private ModPage? _mod;

    // Chiffres du mod choisi
    [ObservableProperty] private long _totalDownloads;
    [ObservableProperty] private long _modrinthDownloads;
    [ObservableProperty] private long _curseForgeDownloads;
    [ObservableProperty] private long _followers;
    [ObservableProperty] private int _versionCount;
    [ObservableProperty] private string _lastRelease = "—";
    [ObservableProperty] private string _created = "—";
    [ObservableProperty] private string _modrinthShare = "";
    [ObservableProperty] private string _curseForgeShare = "";
    [ObservableProperty] private GridLength _modrinthShareWidth = new(1, GridUnitType.Star);
    [ObservableProperty] private GridLength _curseForgeShareWidth = new(1, GridUnitType.Star);
    [ObservableProperty] private string _modrinthStatus = "";
    [ObservableProperty] private string _curseForgeStatus = "";
    [ObservableProperty] private string _updated = "";

    public ObservableCollection<StatBar> ByVersion { get; } = new();
    public ObservableCollection<StatBar> ByGameVersion { get; } = new();

    // Tous les projets du compte
    public ObservableCollection<ProjectStat> Projects { get; } = new();
    [ObservableProperty] private ProjectStat? _selectedProject;
    [ObservableProperty] private long _accountDownloads;
    [ObservableProperty] private long _accountModrinth;
    [ObservableProperty] private long _accountCurseForge;
    [ObservableProperty] private long _accountFollowers;
    [ObservableProperty] private string _accountStatus = "";

    public void Load(ModPage? mod)
    {
        _mod = mod;
        Clear();
    }

    private void Clear()
    {
        TotalDownloads = ModrinthDownloads = CurseForgeDownloads = Followers = 0;
        VersionCount = 0;
        LastRelease = Created = "—";
        ModrinthShare = CurseForgeShare = Updated = "";
        ModrinthShareWidth = CurseForgeShareWidth = new GridLength(1, GridUnitType.Star);
        ByVersion.Clear();
        ByGameVersion.Clear();
        ModrinthStatus = string.IsNullOrEmpty(_mod?.ModrinthProject) ? L.T("Pas de projet Modrinth dans les réglages", "No Modrinth project in the settings") : "";
        CurseForgeStatus = _mod is { CurseForgeProjectId: not 0 } ? "" : L.T("Pas de projet CurseForge dans les réglages", "No CurseForge project in the settings");
    }

    [RelayCommand]
    private Task Refresh() => main.RunAsync(L.T("Lecture des statistiques", "Reading statistics"), RefreshAsync);

    [RelayCommand]
    private void OpenProject(ProjectStat? project)
    {
        if ((project ?? SelectedProject)?.Url is { Length: > 0 } url) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>Lecture seule : chaque site est lu à part, une erreur sur l'un n'empêche pas d'afficher l'autre.</summary>
    private async Task RefreshAsync()
    {
        if (_mod is null) return;
        Clear();
        var mrVersions = new List<SiteVersion>();
        var cfVersions = new List<SiteVersion>();
        DateTime? created = null;
        string? modrinthId = null;

        if (!string.IsNullOrEmpty(_mod.ModrinthProject))
        {
            try
            {
                var p = await S.Modrinth.GetProjectAsync(_mod.ModrinthProject);
                modrinthId = (string?)p["id"];
                ModrinthDownloads = (long?)p["downloads"] ?? 0;
                Followers = (long?)p["followers"] ?? 0;
                created = Date(p["published"]);
                foreach (var v in (await S.Modrinth.GetVersionsAsync(modrinthId!)).OfType<JsonObject>())
                    mrVersions.Add(new SiteVersion((string?)v["version_number"] ?? "?", Date(v["date_published"]) ?? default,
                        (long?)v["downloads"] ?? 0, Strings(v["game_versions"])));
                ModrinthStatus = L.T($"{mrVersions.Count} versions", $"{mrVersions.Count} versions");
            }
            catch (Exception e) { ModrinthStatus = Short(e); main.Log(L.T("Modrinth : ", "Modrinth: ") + Short(e)); }
        }

        // Liste des projets du compte : donne le total CurseForge du mod (et sert au tableau du bas)
        var cfProjects = new List<CfProjectSummary>();
        string? cfError = null;
        try { cfProjects = await S.CurseForge.GetMyProjectsAsync(); }
        catch (Exception e) { cfError = Short(e); }

        if (_mod.CurseForgeProjectId != 0)
        {
            try
            {
                var known = mrVersions.Select(v => v.Number).ToList();
                var files = await S.CurseForge.GetFilesAsync(_mod.CurseForgeProjectId);
                cfVersions.AddRange(files.Select(f => new SiteVersion(VersionsViewModel.GuessNumber(f, known), f.Date, f.Downloads, f.GameVersions)));
                // Le total du site compte aussi les fichiers supprimés : on le préfère à la somme des fichiers
                var summary = cfProjects.FirstOrDefault(p => p.Id == _mod.CurseForgeProjectId);
                CurseForgeDownloads = summary?.Downloads ?? files.Sum(f => f.Downloads);
                if (summary is not null && (created is null || summary.Created < created)) created = summary.Created;
                CurseForgeStatus = L.T($"{files.Count} fichiers", $"{files.Count} files");
            }
            catch (Exception e) { CurseForgeStatus = Short(e); main.Log(L.T("CurseForge : ", "CurseForge: ") + Short(e)); }
        }

        Show(mrVersions, cfVersions, created);
        await LoadAccountAsync(cfProjects, cfError, modrinthId);
        Updated = L.T($"Mis à jour à {DateTime.Now:t}", $"Updated at {DateTime.Now:t}");
    }

    /// <summary>Remplit chiffres et graphiques à partir des versions lues (aussi utilisé par le mode démonstration).</summary>
    public void Show(List<SiteVersion> modrinth, List<SiteVersion> curseForge, DateTime? created)
    {
        TotalDownloads = ModrinthDownloads + CurseForgeDownloads;
        if (TotalDownloads > 0)
        {
            var mr = (double)ModrinthDownloads / TotalDownloads;
            ModrinthShare = $"Modrinth {mr:P0}";
            CurseForgeShare = $"CurseForge {1 - mr:P0}";
            ModrinthShareWidth = new GridLength(ModrinthDownloads, GridUnitType.Star);
            CurseForgeShareWidth = new GridLength(CurseForgeDownloads, GridUnitType.Star);
        }

        var versions = StatsMath.ByVersion(modrinth, curseForge);
        VersionCount = versions.Count;
        var max = versions.Select(v => v.Total).DefaultIfEmpty(0).Max();
        ByVersion.Clear();
        foreach (var v in versions) ByVersion.Add(new StatBar(v, max, v.Date == default ? null : v.Date.ToString("d")));

        var games = StatsMath.ByGameVersion(modrinth, curseForge);
        max = games.Select(v => v.Total).DefaultIfEmpty(0).Max();
        ByGameVersion.Clear();
        foreach (var g in games) ByGameVersion.Add(new StatBar(g, max));

        var last = versions.Select(v => v.Date).DefaultIfEmpty().Max();
        LastRelease = last == default ? "—" : last.ToString("d");
        Created = created is { } c && c != DateTime.MinValue ? c.ToString("d") : "—";
    }

    private async Task LoadAccountAsync(List<CfProjectSummary> cfProjects, string? cfError, string? currentModrinthId)
    {
        var list = new List<ProjectStat>();
        var notes = new List<string>();
        try
        {
            if (!S.Modrinth.HasToken) notes.Add(L.T("Modrinth : pas connecté", "Modrinth: not signed in"));
            foreach (var p in (await S.Modrinth.GetMyProjectsAsync()).OfType<JsonObject>())
                list.Add(new ProjectStat((string?)p["title"] ?? "?", "Modrinth", (long?)p["downloads"] ?? 0, (long?)p["followers"] ?? 0,
                    Date(p["published"]), Date(p["updated"]), $"https://modrinth.com/{(string?)p["project_type"] ?? "mod"}/{(string?)p["slug"]}",
                    (string?)p["id"] == currentModrinthId));
        }
        catch (Exception e) { notes.Add(L.T("Modrinth : ", "Modrinth: ") + Short(e)); }

        if (cfError is not null) notes.Add(L.T("CurseForge : ", "CurseForge: ") + cfError);
        foreach (var p in cfProjects)
            list.Add(new ProjectStat(p.Name, "CurseForge", p.Downloads, null, p.Created, p.LastRelease,
                $"https://www.curseforge.com/projects/{p.Id}", p.Id == _mod?.CurseForgeProjectId));

        ShowAccount(list);
        AccountStatus = string.Join(" · ", notes);
    }

    public void ShowAccount(List<ProjectStat> list)
    {
        Projects.Clear();
        foreach (var p in list.OrderByDescending(p => p.Downloads)) Projects.Add(p);
        AccountModrinth = list.Where(p => p.Site == "Modrinth").Sum(p => p.Downloads);
        AccountCurseForge = list.Where(p => p.Site == "CurseForge").Sum(p => p.Downloads);
        AccountDownloads = AccountModrinth + AccountCurseForge;
        AccountFollowers = list.Sum(p => p.Followers ?? 0);
    }

    private static DateTime? Date(JsonNode? node) => DateTime.TryParse((string?)node, out var d) ? d.ToLocalTime() : null;

    private static List<string> Strings(JsonNode? node) => (node as JsonArray ?? new()).Select(x => (string?)x ?? "").ToList();

    private static string Short(Exception e) => e.Message.Split('\n')[0];
}
