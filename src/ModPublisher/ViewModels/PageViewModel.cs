using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ModPublisher.Services;
using ModPublisher.Views;

namespace ModPublisher.ViewModels;

/// <summary>Description unique, résumé, liens et icône, envoyés sur les deux sites.</summary>
public sealed partial class PageViewModel(MainViewModel main) : ObservableObject
{
    private AppServices S => main.S;
    private ModPage? _mod;
    private string? _remoteModrinthBody, _remoteCurseForgeBody, _modrinthProjectId;

    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _previewCurseForge;
    [ObservableProperty] private string _modrinthStatus = L.T("Pas encore comparé", "Not compared yet");
    [ObservableProperty] private string _curseForgeStatus = L.T("Pas encore comparé", "Not compared yet");
    [ObservableProperty] private string _warnings = "";
    [ObservableProperty] private bool _dirty;
    [ObservableProperty] private bool _canImportLegacy;
    [ObservableProperty] private string? _iconPath;

    public int SummaryLength => Summary.Length;

    public void Load(ModPage? mod)
    {
        _mod = mod;
        _remoteModrinthBody = _remoteCurseForgeBody = _modrinthProjectId = null;
        ModrinthStatus = CurseForgeStatus = L.T("Pas encore comparé", "Not compared yet");
        if (mod is null) return;
        Description = mod.ReadDescription();
        Summary = mod.ReadSummary();
        CanImportLegacy = Description.Length == 0 && File.Exists(mod.LegacyModrinthDescriptionPath);
        IconPath = mod.IconPath;
        Dirty = false;
        UpdateWarnings();
    }

    partial void OnDescriptionChanged(string value)
    {
        Dirty = true;
        UpdateWarnings();
    }

    partial void OnSummaryChanged(string value)
    {
        Dirty = true;
        OnPropertyChanged(nameof(SummaryLength));
        UpdateWarnings();
    }

    /// <summary>Texte envoyé à Modrinth : la description maître avec les URL des images.</summary>
    public string ForModrinth(ICollection<string>? missing = null) =>
        DescriptionConverter.ResolveImages(Description, _mod?.ImageUrls() ?? new(), missing);

    public string ForCurseForge() => DescriptionConverter.ToCurseForge(ForModrinth());

    public string PreviewHtml => MarkdownPreview.Render(PreviewCurseForge ? ForCurseForge() : ForModrinth(), PreviewCurseForge);

    partial void OnPreviewCurseForgeChanged(bool value) => OnPropertyChanged(nameof(PreviewHtml));

    private void UpdateWarnings()
    {
        var missing = new List<string>();
        ForModrinth(missing);
        var w = new List<string>();
        if (missing.Count > 0) w.Add(L.T($"Images sans URL dans image-urls.json : {string.Join(", ", missing.Distinct())}", $"Images without a URL in image-urls.json: {string.Join(", ", missing.Distinct())}"));
        if (Summary.Length > 256) w.Add(L.T($"Résumé trop long pour Modrinth ({Summary.Length}/256)", $"Summary too long for Modrinth ({Summary.Length}/256)"));
        Warnings = string.Join("\n", w);
    }

    // ---------- Local ----------

    [RelayCommand]
    private void SaveLocal()
    {
        if (_mod is null) return;
        _mod.WriteDescription(Description);
        _mod.WriteSummary(Summary);
        Dirty = false;
        main.Log(L.T($"Enregistré : {_mod.DescriptionPath} et summary.txt", $"Saved: {_mod.DescriptionPath} and summary.txt"));
    }

    [RelayCommand]
    private void ImportLegacy()
    {
        if (_mod is null) return;
        Description = File.ReadAllText(_mod.LegacyModrinthDescriptionPath).Replace("\r\n", "\n");
        CanImportLegacy = false;
        SaveLocal();
    }

    [RelayCommand]
    private void LoadBackup()
    {
        if (_mod is null) return;
        var dialog = new OpenFileDialog { InitialDirectory = BackupStore.DirFor(_mod.Name), Filter = L.T("Sauvegardes (*.md)|*.md", "Backups (*.md)|*.md") };
        if (dialog.ShowDialog() != true) return;
        var text = File.ReadAllText(dialog.FileName).Replace("\r\n", "\n");
        if (dialog.FileName.Contains("-summary")) Summary = text.Trim(); else Description = text;
        main.Log(L.T($"Sauvegarde chargée dans l'éditeur : {Path.GetFileName(dialog.FileName)} (pas encore envoyée)", $"Backup loaded in the editor: {Path.GetFileName(dialog.FileName)} (not sent yet)"));
    }

    // ---------- Comparaison ----------

    private long CurseForgeId => _mod?.CurseForgeProjectId ?? 0;
    private bool HasModrinth => !string.IsNullOrEmpty(_mod?.ModrinthProject);

    [RelayCommand]
    private Task Compare() => main.RunAsync(L.T("Comparaison avec les sites", "Comparing with the sites"), CompareAsync);

    private async Task CompareAsync()
    {
        if (_mod is null) return;
        if (HasModrinth)
        {
            var p = await S.Modrinth.GetProjectAsync(_mod.ModrinthProject);
            _modrinthProjectId = (string)p["id"]!;
            _remoteModrinthBody = (string?)p["body"] ?? "";
            var sameBody = DescriptionConverter.SameText(_remoteModrinthBody, ForModrinth());
            var sameSummary = DescriptionConverter.SameText((string?)p["description"], Summary);
            ModrinthStatus = Status(sameBody, sameSummary, _remoteModrinthBody.Length == 0);
        }
        else ModrinthStatus = L.T("Pas de projet Modrinth dans les réglages", "No Modrinth project in the settings");

        if (CurseForgeId != 0)
        {
            try
            {
                var p = await S.CurseForge.GetProjectAsync(CurseForgeId);
                // CurseForge enregistre < > & sous forme d'entités HTML : on compare le texte décodé
                _remoteCurseForgeBody = DescriptionConverter.DecodeEntities(p.Description);
                CurseForgeStatus = Status(DescriptionConverter.SameText(_remoteCurseForgeBody, DescriptionConverter.DecodeEntities(ForCurseForge())),
                    DescriptionConverter.SameText(DescriptionConverter.DecodeEntities(p.Summary), Summary), p.Description.Length == 0);
            }
            catch (NotSupportedException e) { CurseForgeStatus = e.Message; }
        }
        else CurseForgeStatus = L.T("Pas de projet CurseForge dans les réglages", "No CurseForge project in the settings");
    }

    private static string Status(bool sameBody, bool sameSummary, bool empty) =>
        empty ? L.T("Description jamais envoyée", "Description never sent")
        : sameBody && sameSummary ? L.T("✔ Identique", "✔ Identical")
        : sameBody ? L.T("Résumé différent", "Summary differs")
        : sameSummary ? L.T("Description différente", "Description differs") : L.T("Description et résumé différents", "Description and summary differ");

    [RelayCommand]
    private void DiffModrinth() => ShowDiff("Modrinth", _remoteModrinthBody, ForModrinth());

    [RelayCommand]
    private void DiffCurseForge() => ShowDiff("CurseForge", _remoteCurseForgeBody, DescriptionConverter.DecodeEntities(ForCurseForge()));

    private void ShowDiff(string site, string? remote, string local)
    {
        if (remote is null)
        {
            MessageBox.Show(L.T("Clique d'abord « Comparer ».", "Click “Compare” first."), site);
            return;
        }
        new DiffWindow(L.T($"{site} : en ligne (−) / local (+)", $"{site}: online (−) / local (+)"), LineDiff.Compute(remote, local)) { Owner = Application.Current.MainWindow }.Show();
    }

    [RelayCommand]
    private Task PullFromModrinth() => main.RunAsync(L.T("Récupération depuis Modrinth", "Pulling from Modrinth"), async () =>
    {
        if (_mod is null || !HasModrinth) return;
        var p = await S.Modrinth.GetProjectAsync(_mod.ModrinthProject);
        if (Dirty && !main.Confirm(L.T("Récupérer", "Pull"), L.T("Le texte de l'éditeur n'est pas enregistré. Le remplacer par celui de Modrinth ?", "The text in the editor is not saved. Replace it with the one from Modrinth?"))) return;
        Description = ((string?)p["body"] ?? "").Replace("\r\n", "\n");
        Summary = (string?)p["description"] ?? "";
        main.Log(L.T("Description et résumé récupérés depuis Modrinth (pas encore enregistrés sur le disque).", "Description and summary pulled from Modrinth (not saved to disk yet)."));
    });

    // ---------- Envoi ----------

    [RelayCommand]
    private Task PushBoth() => Push(true, true);

    [RelayCommand]
    private Task PushModrinth() => Push(true, false);

    [RelayCommand]
    private Task PushCurseForge() => Push(false, true);

    private Task Push(bool modrinth, bool curseForge) => main.RunAsync(L.T("Envoi de la page", "Sending the page"), async () =>
    {
        if (_mod is null) return;
        modrinth &= HasModrinth;
        curseForge &= CurseForgeId != 0;
        if (!modrinth && !curseForge) throw new InvalidOperationException(L.T("Renseigne d'abord le projet Modrinth et/ou CurseForge dans l'onglet Réglages.", "First fill in the Modrinth and/or CurseForge project in the Settings tab."));
        if (Summary.Length > 256 && modrinth) throw new InvalidOperationException(L.T($"Résumé trop long pour Modrinth ({Summary.Length}/256).", $"Summary too long for Modrinth ({Summary.Length}/256)."));

        var sites = string.Join(L.T(" et ", " and "), new[] { modrinth ? "Modrinth" : null, curseForge ? "CurseForge" : null }.Where(x => x is not null));
        var links = _mod.Section("links");
        if (!main.Confirm(L.T("Envoyer la page", "Send the page"),
                L.T($"Remplacer sur {sites} :\n• la description ({Description.Split('\n').Length} lignes)\n• le résumé : « {Summary} »\n• les liens des réglages\n\nLe contenu actuel en ligne est sauvegardé avant.", $"Replace on {sites}:\n• the description ({Description.Split('\n').Length} lines)\n• the summary: “{Summary}”\n• the links from the settings\n\nThe current online content is backed up first.")))
            return;

        SaveLocal();
        if (modrinth)
        {
            var p = await S.Modrinth.GetProjectAsync(_mod.ModrinthProject);
            BackupStore.Save(_mod.Name, "modrinth", "body", (string?)p["body"] ?? "");
            BackupStore.Save(_mod.Name, "modrinth", "summary", (string?)p["description"] ?? "");
            var patch = new JsonObject { ["body"] = ForModrinth(), ["description"] = Summary };
            foreach (var (key, field) in new[] { ("source", "source_url"), ("issues", "issues_url"), ("wiki", "wiki_url"), ("discord", "discord_url") })
                if ((string?)links[key] is { Length: > 0 } url) patch[field] = url;
            await S.Modrinth.ModifyProjectAsync((string)p["id"]!, patch);
            main.Log(L.T("Modrinth : page mise à jour.", "Modrinth: page updated."));
        }
        if (curseForge)
        {
            var p = await S.CurseForge.GetProjectAsync(CurseForgeId);
            BackupStore.Save(_mod.Name, "curseforge", "body", p.Description);
            BackupStore.Save(_mod.Name, "curseforge", "summary", p.Summary);
            await S.CurseForge.UpdateDescriptionAsync(CurseForgeId, ForCurseForge());
            await S.CurseForge.UpdateSummaryAsync(CurseForgeId, Summary);
            var cfLinks = links.Where(l => (string?)l.Value is { Length: > 0 }).ToDictionary(l => l.Key, l => (string)l.Value!);
            if (cfLinks.Count > 0) await S.CurseForge.UpdateLinksAsync(CurseForgeId, cfLinks);
            main.Log(L.T("CurseForge : page mise à jour.", "CurseForge: page updated."));
        }
        if (!main.Simulation) await CompareAsync();
    });

    [RelayCommand]
    private void ChooseIcon()
    {
        if (_mod is null) return;
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            SaveAsPng(dialog.FileName, _mod.PageIconPath);
        }
        catch (Exception e)
        {
            MessageBox.Show(L.T($"Image illisible : {e.Message}", $"Unreadable image: {e.Message}"), L.T("Icône", "Icon"));
            return;
        }
        // Force le rechargement de l'aperçu même si le chemin ne change pas
        IconPath = null;
        IconPath = _mod.PageIconPath;
        main.SelectedMod?.RefreshIcon();
        main.Log(L.T($"Icône enregistrée : {_mod.PageIconPath} (pas encore envoyée sur les sites)", $"Icon saved: {_mod.PageIconPath} (not sent to the sites yet)"));
    }

    /// <summary>Copie l'image en PNG dans le mod (les PNG sont copiés tels quels).</summary>
    private static void SaveAsPng(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return;
        if (Path.GetExtension(source).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source, target, true);
            return;
        }
        var frame = BitmapDecoder.Create(new Uri(source), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(frame);
        using var stream = File.Create(target);
        encoder.Save(stream);
    }

    [RelayCommand]
    private Task PushIcon() => main.RunAsync(L.T("Envoi de l'icône", "Sending the icon"), async () =>
    {
        if (_mod is null || IconPath is null || !File.Exists(IconPath)) throw new InvalidOperationException(L.T("Pas d'icône choisie.", "No icon chosen."));
        if (!main.Confirm(L.T("Icône", "Icon"), L.T($"Remplacer l'icône du projet par {Path.GetFileName(IconPath)} sur les deux sites ?", $"Replace the project icon with {Path.GetFileName(IconPath)} on both sites?"))) return;
        if (HasModrinth)
        {
            var id = _modrinthProjectId ?? (string)(await S.Modrinth.GetProjectAsync(_mod.ModrinthProject))["id"]!;
            await S.Modrinth.SetIconAsync(id, IconPath);
            main.Log(L.T("Modrinth : icône mise à jour.", "Modrinth: icon updated."));
        }
        if (CurseForgeId != 0)
        {
            await S.CurseForge.SetIconAsync(CurseForgeId, IconPath);
            main.Log(L.T("CurseForge : icône mise à jour.", "CurseForge: icon updated."));
        }
    });
}
