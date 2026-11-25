using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ModPublisher.Services;

namespace ModPublisher.ViewModels;

public sealed partial class GalleryItem : ObservableObject
{
    public required string Site { get; init; }
    public required string Url { get; init; }
    public long? CurseForgeId { get; init; }
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _featured;
    [ObservableProperty] private int _ordering;
}

public sealed partial class GalleryViewModel(MainViewModel main) : ObservableObject
{
    private AppServices S => main.S;
    private ModPage? _mod;
    private string? _modrinthProjectId;

    public ObservableCollection<GalleryItem> ModrinthItems { get; } = new();
    public ObservableCollection<GalleryItem> CurseForgeItems { get; } = new();
    [ObservableProperty] private string _modrinthStatus = "";
    [ObservableProperty] private string _curseForgeStatus = "";

    /// <summary>Image affichée dans le panneau de droite : la dernière choisie dans l'une des deux listes.</summary>
    [ObservableProperty] private GalleryItem? _selected;
    [ObservableProperty] private GalleryItem? _selectedModrinth;
    [ObservableProperty] private GalleryItem? _selectedCurseForge;

    /// <summary>Sites visés par les ajouts d'images.</summary>
    [ObservableProperty] private bool _toModrinth = true;
    [ObservableProperty] private bool _toCurseForge = true;

    partial void OnSelectedModrinthChanged(GalleryItem? value)
    {
        if (value is null) return;
        SelectedCurseForge = null;
        Selected = value;
    }

    partial void OnSelectedCurseForgeChanged(GalleryItem? value)
    {
        if (value is null) return;
        SelectedModrinth = null;
        Selected = value;
    }

    private void Clear()
    {
        ModrinthItems.Clear();
        CurseForgeItems.Clear();
        Selected = SelectedModrinth = SelectedCurseForge = null;
    }

    public void Load(ModPage? mod)
    {
        _mod = mod;
        _modrinthProjectId = null;
        Clear();
        ModrinthStatus = string.IsNullOrEmpty(mod?.ModrinthProject) ? L.T("Pas de projet Modrinth dans les réglages", "No Modrinth project in the settings") : "";
        CurseForgeStatus = mod is { CurseForgeProjectId: not 0 } ? "" : L.T("Pas de projet CurseForge dans les réglages", "No CurseForge project in the settings");
    }

    [RelayCommand]
    private Task Refresh() => main.RunAsync(L.T("Lecture de la galerie", "Reading the gallery"), RefreshAsync);

    private async Task RefreshAsync()
    {
        if (_mod is null) return;
        Clear();
        // Un site en erreur n'empêche pas d'afficher l'autre
        if (!string.IsNullOrEmpty(_mod.ModrinthProject))
        {
            try
            {
                var p = await S.Modrinth.GetProjectAsync(_mod.ModrinthProject);
                _modrinthProjectId = (string)p["id"]!;
                foreach (var g in (p["gallery"] as JsonArray ?? new()).Select(n => n!).OrderBy(n => (int?)n["ordering"] ?? 0))
                    ModrinthItems.Add(new GalleryItem
                    {
                        Site = "Modrinth", Url = (string)g["url"]!, Title = (string?)g["title"] ?? "",
                        Description = (string?)g["description"] ?? "", Featured = (bool?)g["featured"] ?? false, Ordering = (int?)g["ordering"] ?? 0,
                    });
                ModrinthStatus = Count(ModrinthItems.Count);
            }
            catch (Exception e)
            {
                ModrinthStatus = L.T("Erreur de lecture (voir le journal)", "Read error (see the log)");
                main.Log(L.T("Modrinth : galerie illisible : ", "Modrinth: cannot read the gallery: ") + e.Message);
            }
        }
        if (_mod.CurseForgeProjectId != 0)
        {
            try
            {
                foreach (var img in await S.CurseForge.GetImagesAsync(_mod.CurseForgeProjectId))
                    CurseForgeItems.Add(new GalleryItem { Site = "CurseForge", Url = img.Url, CurseForgeId = img.Id, Title = img.Title, Description = img.Description });
                CurseForgeStatus = Count(CurseForgeItems.Count);
            }
            catch (Exception e)
            {
                CurseForgeStatus = L.T("Erreur de lecture (voir le journal)", "Read error (see the log)");
                main.Log(L.T("CurseForge : galerie illisible : ", "CurseForge: cannot read the gallery: ") + e.Message);
            }
        }
    }

    private static string Count(int n) => n switch { 0 => L.T("Aucune image", "No images"), 1 => "1 image", _ => $"{n} images" };

    private ObservableCollection<GalleryItem> ListOf(GalleryItem item) => item.Site == "Modrinth" ? ModrinthItems : CurseForgeItems;

    private async Task<string> ModrinthId() =>
        _modrinthProjectId ??= (string)(await S.Modrinth.GetProjectAsync(_mod!.ModrinthProject))["id"]!;

    [RelayCommand]
    private Task SaveSelected() => main.RunAsync(L.T("Modification de l'image", "Editing the image"), async () =>
    {
        if (Selected is not { } item || _mod is null) return;
        if (item.Site == "Modrinth")
            await S.Modrinth.ModifyGalleryImageAsync(await ModrinthId(), item.Url, item.Featured, item.Title, item.Description, item.Ordering);
        else
            await S.CurseForge.UpdateImageAsync(_mod.CurseForgeProjectId, item.CurseForgeId!.Value, item.Title, item.Description);
        main.Log(L.T($"{item.Site} : image « {item.Title} » modifiée.", $"{item.Site}: image “{item.Title}” updated."));
    });

    [RelayCommand]
    private Task DeleteSelected() => main.RunAsync(L.T("Suppression de l'image", "Deleting the image"), async () =>
    {
        if (Selected is not { } item || _mod is null) return;
        if (!main.Confirm(L.T("Supprimer", "Delete"), L.T($"Supprimer définitivement l'image « {item.Title} » de la galerie {item.Site} ?", $"Permanently delete the image “{item.Title}” from the {item.Site} gallery?"))) return;
        if (item.Site == "Modrinth") await S.Modrinth.DeleteGalleryImageAsync(await ModrinthId(), item.Url);
        else await S.CurseForge.DeleteImageAsync(_mod.CurseForgeProjectId, item.CurseForgeId!.Value);
        main.Log(L.T($"{item.Site} : image « {item.Title} » supprimée.", $"{item.Site}: image “{item.Title}” deleted."));
        if (!main.Simulation)
        {
            ListOf(item).Remove(item);
            Selected = null;
        }
    });

    [RelayCommand]
    private Task AddFiles()
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.gif", InitialDirectory = _mod?.GalleryDir };
        if (dialog.ShowDialog() != true) return Task.CompletedTask;
        return Upload(dialog.FileNames.Select(f => (f, ToModrinth, ToCurseForge)).ToList());
    }

    /// <summary>Envoie les images de modpage/images/gallery absentes de chaque site (comparaison par titre, site par site).</summary>
    [RelayCommand]
    private Task AddMissingFromFolder()
    {
        if (_mod is null || !Directory.Exists(_mod.GalleryDir)) return Task.CompletedTask;
        var titles = ReadmeTitles();
        var onModrinth = ModrinthItems.Select(i => i.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var onCurseForge = CurseForgeItems.Select(i => i.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plan = Directory.GetFiles(_mod.GalleryDir).Where(f => Regex.IsMatch(f, @"\.(png|jpe?g|webp|gif)$", RegexOptions.IgnoreCase)).OrderBy(f => f)
            .Select(f => (File: f, Title: TitleFor(f, titles).Title))
            .Select(f => (f.File, Modrinth: ToModrinth && !onModrinth.Contains(f.Title), CurseForge: ToCurseForge && !onCurseForge.Contains(f.Title)))
            .Where(p => p.Modrinth || p.CurseForge).ToList();
        if (plan.Count == 0)
        {
            main.Log(L.T("Toutes les images du dossier sont déjà en ligne sur les sites cochés (d'après leur titre).", "All the images in the folder are already online on the checked sites (based on their titles)."));
            return Task.CompletedTask;
        }
        return Upload(plan);
    }

    /// <summary>Envoie chaque fichier sur les sites indiqués pour lui.</summary>
    private Task Upload(List<(string File, bool Modrinth, bool CurseForge)> plan) => main.RunAsync(L.T("Ajout d'images", "Adding images"), async () =>
    {
        if (_mod is null) return;
        var hasModrinth = !string.IsNullOrEmpty(_mod.ModrinthProject);
        var hasCurseForge = _mod.CurseForgeProjectId != 0;
        plan = plan.Select(p => (p.File, p.Modrinth && hasModrinth, p.CurseForge && hasCurseForge)).Where(p => p.Item2 || p.Item3).ToList();
        if (plan.Count == 0) throw new InvalidOperationException(L.T("Aucun site visé : coche Modrinth et/ou CurseForge, et renseigne leurs projets dans les Réglages.", "No target site: check Modrinth and/or CurseForge, and fill in their projects in the Settings."));

        var titles = ReadmeTitles();
        var list = string.Join("\n", plan.Select(p =>
            $"• {Path.GetFileName(p.File)} : {TitleFor(p.File, titles).Title} → {string.Join(" + ", new[] { p.Modrinth ? "Modrinth" : null, p.CurseForge ? "CurseForge" : null }.Where(s => s is not null))}"));
        if (!main.Confirm(L.T("Ajouter à la galerie", "Add to the gallery"), L.T($"Envoyer {plan.Count} image(s) ?\n\n{list}", $"Send {plan.Count} image(s)?\n\n{list}"))) return;

        var order = ModrinthItems.Select(i => i.Ordering).DefaultIfEmpty(-1).Max() + 1;
        foreach (var (file, modrinth, curseForge) in plan)
        {
            var (title, description) = TitleFor(file, titles);
            if (modrinth)
            {
                await S.Modrinth.AddGalleryImageAsync(await ModrinthId(), file, featured: false, title, description, order++);
                main.Log(L.T($"Modrinth : image envoyée : {title}", $"Modrinth: image sent: {title}"));
            }
            if (curseForge)
            {
                await S.CurseForge.AddImageAsync(_mod.CurseForgeProjectId, file, title, description);
                main.Log(L.T($"CurseForge : image envoyée : {title}", $"CurseForge: image sent: {title}"));
            }
        }
        if (!main.Simulation) await RefreshAsync();
    });

    /// <summary>Titres du tableau « Galerie : titres suggérés » de modpage/README.md : | `01_wheel` | Titre | Description |</summary>
    private Dictionary<string, (string, string)> ReadmeTitles()
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        var readme = Path.Combine(_mod!.Dir, "README.md");
        if (!File.Exists(readme)) return result;
        foreach (Match m in Regex.Matches(File.ReadAllText(readme), @"^\|\s*`([^`]+)`\s*\|\s*([^|]+?)\s*\|\s*([^|]*?)\s*\|", RegexOptions.Multiline))
            result[m.Groups[1].Value] = (m.Groups[2].Value, m.Groups[3].Value);
        return result;
    }

    private static (string Title, string Description) TitleFor(string file, Dictionary<string, (string, string)> titles)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        return titles.TryGetValue(name, out var t) ? t : (Regex.Replace(name, @"^\d+_", "").Replace('_', ' '), "");
    }
}
