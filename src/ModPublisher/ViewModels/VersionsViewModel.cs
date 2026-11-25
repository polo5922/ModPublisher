using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModPublisher.Services;

namespace ModPublisher.ViewModels;

/// <summary>Une version, telle qu'elle existe sur l'un et/ou l'autre site.</summary>
public sealed partial class VersionRow : ObservableObject
{
    public required string Number { get; init; }
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _type = "release";
    [ObservableProperty] private string _gameVersions = "";
    [ObservableProperty] private string _loaders = "";
    [ObservableProperty] private DateTime _date;
    [ObservableProperty] private long _downloads;
    [ObservableProperty] private bool _featured;
    public JsonObject? Modrinth { get; set; }
    public CfFile? CurseForge { get; set; }
    public string Sites => (Modrinth is null ? "" : "Modrinth") + (Modrinth is not null && CurseForge is not null ? " + " : "") + (CurseForge is null ? "" : "CurseForge");
}

public sealed partial class VersionsViewModel(MainViewModel main) : ObservableObject
{
    private AppServices S => main.S;
    private ModPage? _mod;
    private string? _modrinthProjectId;

    public ObservableCollection<VersionRow> Rows { get; } = new();
    public static string[] ReleaseTypes { get; } = ["release", "beta", "alpha"];

    // Version sélectionnée (édition)
    [ObservableProperty] private VersionRow? _selected;
    [ObservableProperty] private string _editChangelog = "";
    [ObservableProperty] private string _editType = "release";
    [ObservableProperty] private string _editGameVersions = "";
    [ObservableProperty] private bool _editFeatured;

    // Nouvelle version
    [ObservableProperty] private string _newVersion = "";
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _newJar = "";
    [ObservableProperty] private string _newChangelog = "";
    [ObservableProperty] private string _newType = "release";
    [ObservableProperty] private string _newGameVersions = "";
    [ObservableProperty] private string _newLoaders = "";
    [ObservableProperty] private bool _buildFirst = true;
    [ObservableProperty] private bool _toModrinth = true;
    [ObservableProperty] private bool _toCurseForge = true;
    [ObservableProperty] private string _checkReport = "";

    public void Load(ModPage? mod)
    {
        _mod = mod;
        _modrinthProjectId = null;
        Rows.Clear();
        Selected = null;
        CheckReport = "";
        if (mod is not null) PrepareNew();
    }

    private static List<string> Split(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // ---------- Liste ----------

    [RelayCommand]
    private Task Refresh() => main.RunAsync(L.T("Lecture des versions", "Reading versions"), RefreshAsync);

    private async Task RefreshAsync()
    {
        if (_mod is null) return;
        var rows = new Dictionary<string, VersionRow>();
        VersionRow Row(string number) => rows.TryGetValue(number, out var r) ? r : rows[number] = new VersionRow { Number = number };

        // Chaque site est lu à part : une erreur sur l'un n'empêche pas d'afficher l'autre
        if (!string.IsNullOrEmpty(_mod.ModrinthProject))
        {
          try
          {
            _modrinthProjectId ??= (string)(await S.Modrinth.GetProjectAsync(_mod.ModrinthProject))["id"]!;
            foreach (var v in (await S.Modrinth.GetVersionsAsync(_modrinthProjectId)).Select(n => n!.AsObject()))
            {
                var r = Row((string)v["version_number"]!);
                r.Modrinth = v;
                r.Name = (string?)v["name"] ?? r.Number;
                r.Type = (string?)v["version_type"] ?? "release";
                r.GameVersions = string.Join(", ", (v["game_versions"] as JsonArray ?? new()).Select(x => (string?)x));
                r.Loaders = string.Join(", ", (v["loaders"] as JsonArray ?? new()).Select(x => (string?)x));
                r.Date = DateTime.Parse((string)v["date_published"]!).ToLocalTime();
                r.Downloads = (long?)v["downloads"] ?? 0;
                r.Featured = (bool?)v["featured"] ?? false;
            }
          }
          catch (ApiException e) when (e.Status == 404)
          {
              main.Log(L.T($"Modrinth : projet « {_mod.ModrinthProject} » introuvable (pas encore créé, ou brouillon sans être connecté ?).", $"Modrinth: project “{_mod.ModrinthProject}” not found (not created yet, or a draft while signed out?)."));
          }
          catch (Exception e) { main.Log(L.T("Modrinth : ", "Modrinth: ") + e.Message.Split('\n')[0]); }
        }

        if (_mod.CurseForgeProjectId != 0)
        {
            try
            {
                foreach (var f in await S.CurseForge.GetFilesAsync(_mod.CurseForgeProjectId))
                {
                    var number = GuessNumber(f, rows.Keys);
                    var r = Row(number);
                    // Plusieurs fichiers pour le même numéro : chacun garde sa ligne (le plus récent sur la ligne principale)
                    if (r.CurseForge is not null) r = rows[$"{number}#{f.Id}"] = new VersionRow { Number = number };
                    r.CurseForge = f;
                    if (r.Modrinth is null)
                    {
                        r.Name = f.DisplayName;
                        r.Type = f.ReleaseType;
                        r.GameVersions = string.Join(", ", f.GameVersions);
                        r.Date = f.Date;
                    }
                    r.Downloads += f.Downloads;
                }
            }
            catch (Exception e) { main.Log(L.T("CurseForge : ", "CurseForge: ") + e.Message.Split('\n')[0]); }
        }

        Rows.Clear();
        foreach (var r in rows.Values.OrderByDescending(r => r.Date)) Rows.Add(r);
        main.Log(L.T($"{Rows.Count} versions trouvées.", $"{Rows.Count} versions found."));
    }

    /// <summary>CurseForge n'a pas de numéro de version : on le retrouve dans le nom affiché ou le nom du jar.</summary>
    private static string GuessNumber(CfFile f, IEnumerable<string> known)
    {
        foreach (var n in known.OrderByDescending(n => n.Length))
            if (f.DisplayName.Contains(n) || f.FileName.Contains(n)) return n;
        var m = System.Text.RegularExpressions.Regex.Match(f.FileName, @"-(\d[\w.+-]*?)\.jar$");
        return m.Success ? m.Groups[1].Value : f.DisplayName;
    }

    // ---------- Édition ----------

    partial void OnSelectedChanged(VersionRow? value)
    {
        if (value is null) return;
        EditChangelog = (string?)value.Modrinth?["changelog"] ?? value.CurseForge?.Changelog ?? "";
        EditType = value.Type;
        EditGameVersions = value.GameVersions;
        EditFeatured = value.Featured;
    }

    [RelayCommand]
    private void ChangelogFromFile()
    {
        if (_mod is null || Selected is null) return;
        try { EditChangelog = ChangelogReader.ExtractFile(_mod.ChangelogPath, Selected.Number); }
        catch (Exception e) { main.Log(e.Message); }
    }

    [RelayCommand]
    private Task SaveEdit() => main.RunAsync(L.T("Modification de la version", "Editing the version"), async () =>
    {
        if (_mod is null || Selected is not { } row) return;
        var sites = row.Sites;
        if (!main.Confirm(L.T("Modifier la version", "Edit the version"), L.T($"Modifier {row.Number} sur {sites} :\n• type : {EditType}\n• Minecraft : {EditGameVersions}\n• changelog : {EditChangelog.Split('\n').Length} lignes", $"Edit {row.Number} on {sites}:\n• type: {EditType}\n• Minecraft: {EditGameVersions}\n• changelog: {EditChangelog.Split('\n').Length} lines")))
            return;

        if (row.Modrinth is { } v)
        {
            BackupStore.Save(_mod.Name, "modrinth", $"changelog-{row.Number}", (string?)v["changelog"] ?? "");
            await S.Modrinth.ModifyVersionAsync((string)v["id"]!, new JsonObject
            {
                ["changelog"] = EditChangelog,
                ["version_type"] = EditType,
                ["game_versions"] = new JsonArray(Split(EditGameVersions).Select(x => (JsonNode)x).ToArray()),
                ["featured"] = EditFeatured,
            });
            main.Log(L.T($"Modrinth : version {row.Number} modifiée.", $"Modrinth: version {row.Number} updated."));
        }
        if (row.CurseForge is { } f)
        {
            BackupStore.Save(_mod.Name, "curseforge", $"changelog-{row.Number}",
                await S.CurseForge.GetFileChangelogAsync(_mod.CurseForgeProjectId, f.Id) ?? "");
            var cf = _mod.Section("curseforge");
            var ids = CurseForgeUploadClient.ResolveIds(await S.CurseForgeUpload.GameVersionsAsync(), Split(EditGameVersions),
                _mod.Loaders, _mod.StringList(cf["environments"]), _mod.StringList(cf["java_versions"]));
            await S.CurseForge.UpdateFileAsync(_mod.CurseForgeProjectId, f.Id, EditChangelog, EditType, ids);
            main.Log(L.T($"CurseForge : fichier {f.Id} modifié.", $"CurseForge: file {f.Id} updated."));
        }
        if (!main.Simulation) await RefreshAsync();
    });

    // ---------- Nouvelle version ----------

    [RelayCommand]
    private void PrepareNew()
    {
        if (_mod is null) return;
        _mod.Reload();
        NewVersion = _mod.GradleVersion() ?? "";
        NewName = _mod.VersionName(NewVersion);
        NewJar = _mod.JarPath(NewVersion);
        NewType = _mod.ReleaseType;
        NewGameVersions = string.Join(", ", _mod.GameVersions);
        NewLoaders = string.Join(", ", _mod.Loaders);
        try { NewChangelog = ChangelogReader.ExtractFile(_mod.ChangelogPath, NewVersion); }
        catch (Exception e) { NewChangelog = ""; CheckReport = e.Message; }
    }

    partial void OnNewVersionChanged(string value)
    {
        if (_mod is null) return;
        NewName = _mod.VersionName(value);
        NewJar = _mod.JarPath(value);
    }

    [RelayCommand]
    private Task Check() => main.RunAsync(L.T("Vérification", "Checking"), async () => CheckReport = string.Join("\n", await CheckAsync(build: false)));

    /// <summary>Les vérifications du --dry-run de publish.py. Lève une exception au premier problème bloquant.</summary>
    private async Task<List<string>> CheckAsync(bool build)
    {
        if (_mod is null) throw new InvalidOperationException(L.T("Aucun mod sélectionné", "No mod selected"));
        var report = new List<string>();
        if (NewVersion.Length == 0) throw new InvalidOperationException(L.T("Numéro de version vide", "Version number is empty"));
        if (!ToModrinth && !ToCurseForge) throw new InvalidOperationException(L.T("Choisis au moins un site", "Choose at least one site"));
        if (NewChangelog.Trim().Length == 0) throw new InvalidOperationException(L.T("Changelog vide", "Changelog is empty"));

        if (build)
        {
            await BuildAsync();
            NewJar = _mod.JarPath(NewVersion);
        }
        if (!File.Exists(NewJar)) throw new InvalidOperationException(L.T($"Jar introuvable : {NewJar} (coche « Compiler avant » ou lance gradlew build)", $"Jar not found: {NewJar} (check “Build first” or run gradlew build)"));
        report.Add(L.T($"✔ Jar : {Path.GetFileName(NewJar)} ({new FileInfo(NewJar).Length / 1024} Ko)", $"✔ Jar: {Path.GetFileName(NewJar)} ({new FileInfo(NewJar).Length / 1024} KB)"));

        if (ToModrinth)
        {
            if (string.IsNullOrEmpty(_mod.ModrinthProject)) throw new InvalidOperationException(L.T("Projet Modrinth non renseigné (Réglages)", "Modrinth project not set (Settings)"));
            if (!S.Modrinth.HasToken) throw new InvalidOperationException(L.T("Pas connecté à Modrinth (Comptes)", "Not signed in to Modrinth (Accounts)"));
            var tags = await S.Modrinth.GameVersionTagsAsync();
            var unknown = Split(NewGameVersions).Where(v => !tags.Contains(v)).ToList();
            if (unknown.Count > 0) throw new InvalidOperationException(L.T($"Versions inconnues de Modrinth : {string.Join(", ", unknown)}", $"Versions unknown to Modrinth: {string.Join(", ", unknown)}"));
            var loaders = await S.Modrinth.LoaderTagsAsync();
            unknown = Split(NewLoaders).Where(l => !loaders.Contains(l)).ToList();
            if (unknown.Count > 0) throw new InvalidOperationException(L.T($"Loaders inconnus de Modrinth : {string.Join(", ", unknown)}", $"Loaders unknown to Modrinth: {string.Join(", ", unknown)}"));
            var project = await S.Modrinth.GetProjectAsync(_mod.ModrinthProject);
            _modrinthProjectId = (string)project["id"]!;
            var versions = await S.Modrinth.GetVersionsAsync(_modrinthProjectId);
            if (versions.Any(v => (string?)v!["version_number"] == NewVersion))
                throw new InvalidOperationException(L.T($"Modrinth : la version {NewVersion} existe déjà", $"Modrinth: version {NewVersion} already exists"));
            report.Add(L.T($"✔ Modrinth : « {(string?)project["title"]} », version {NewVersion} libre", $"✔ Modrinth: “{(string?)project["title"]}”, version {NewVersion} is free"));
        }

        if (ToCurseForge)
        {
            if (_mod.CurseForgeProjectId == 0) throw new InvalidOperationException(L.T("ID CurseForge non renseigné (Réglages)", "CurseForge ID not set (Settings)"));
            if (!S.CurseForgeUpload.HasToken) throw new InvalidOperationException(L.T("Pas de jeton d'upload CurseForge (Comptes)", "No CurseForge upload token (Accounts)"));
            var ids = await ResolveCurseForgeIds();
            report.Add(L.T($"✔ CurseForge : projet {_mod.CurseForgeProjectId}, {ids.Count} versions de jeu reconnues", $"✔ CurseForge: project {_mod.CurseForgeProjectId}, {ids.Count} game versions recognized"));
        }
        return report;
    }

    private async Task<List<long>> ResolveCurseForgeIds()
    {
        var cf = _mod!.Section("curseforge");
        return CurseForgeUploadClient.ResolveIds(await S.CurseForgeUpload.GameVersionsAsync(),
            Split(NewGameVersions), Split(NewLoaders), _mod.StringList(cf["environments"]), _mod.StringList(cf["java_versions"]));
    }

    private async Task BuildAsync()
    {
        var gradlew = Path.Combine(_mod!.Folder, "gradlew.bat");
        if (!File.Exists(gradlew)) throw new InvalidOperationException(L.T("gradlew.bat introuvable dans le dossier du mod", "gradlew.bat not found in the mod folder"));
        main.Log("> gradlew build");
        var psi = new ProcessStartInfo("cmd.exe", "/c gradlew.bat build")
        {
            WorkingDirectory = _mod.Folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        process.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } l) System.Windows.Application.Current.Dispatcher.Invoke(() => main.Log(l)); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } l) System.Windows.Application.Current.Dispatcher.Invoke(() => main.Log(l)); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(L.T($"gradlew build a échoué (code {process.ExitCode}), voir le journal", $"gradlew build failed (code {process.ExitCode}), see the log"));
    }

    [RelayCommand]
    private Task Publish() => main.RunAsync(L.T("Publication", "Publishing"), async () =>
    {
        if (_mod is null) return;
        var report = await CheckAsync(BuildFirst);
        CheckReport = string.Join("\n", report);
        var sites = string.Join(L.T(" et ", " and "), new[] { ToModrinth ? "Modrinth" : null, ToCurseForge ? "CurseForge" : null }.Where(x => x is not null));
        if (!main.Confirm(L.T("Publier", "Publish"), L.T($"Publier {NewName} ({NewType}) sur {sites} ?\n\n{CheckReport}", $"Publish {NewName} ({NewType}) to {sites}?\n\n{CheckReport}"))) return;

        var errors = new List<string>();
        if (ToModrinth)
        {
            try
            {
                var deps = new JsonArray((_mod.Section("modrinth")["dependencies"] as JsonArray ?? new())
                    .Select(d => (JsonNode)new JsonObject { ["project_id"] = (string?)d!["project_id"], ["dependency_type"] = (string?)d["dependency_type"] ?? "required" })
                    .ToArray());
                var res = await S.Modrinth.CreateVersionAsync(_modrinthProjectId!, new JsonObject
                {
                    ["name"] = NewName,
                    ["version_number"] = NewVersion,
                    ["changelog"] = NewChangelog,
                    ["dependencies"] = deps,
                    ["game_versions"] = new JsonArray(Split(NewGameVersions).Select(x => (JsonNode)x).ToArray()),
                    ["loaders"] = new JsonArray(Split(NewLoaders).Select(x => (JsonNode)x).ToArray()),
                    ["version_type"] = NewType,
                    ["featured"] = (bool?)_mod.Section("modrinth")["featured"] ?? true,
                }, NewJar);
                if (res is not null) main.Log(L.T($"Modrinth : version publiée (id {(string?)res["id"]}).", $"Modrinth: version published (id {(string?)res["id"]})."));
            }
            catch (Exception e) { errors.Add(L.T("Modrinth : ", "Modrinth: ") + e.Message); }
        }
        if (ToCurseForge)
        {
            try
            {
                var relations = new JsonArray((_mod.Section("curseforge")["relations"] as JsonArray ?? new())
                    .Select(r => (JsonNode)new JsonObject { ["slug"] = (string?)r!["slug"], ["type"] = (string?)r["type"] ?? "requiredDependency" })
                    .ToArray());
                var id = await S.CurseForgeUpload.UploadAsync(_mod.CurseForgeProjectId, NewName, NewChangelog, NewType,
                    await ResolveCurseForgeIds(), relations, NewJar);
                if (id is not null) main.Log(L.T($"CurseForge : fichier envoyé (id {id}), visible après la modération.", $"CurseForge: file sent (id {id}), visible after moderation."));
            }
            catch (Exception e) { errors.Add(L.T("CurseForge : ", "CurseForge: ") + e.Message); }
        }
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n\n", errors));
        if (!main.Simulation) await RefreshAsync();
    });
}
