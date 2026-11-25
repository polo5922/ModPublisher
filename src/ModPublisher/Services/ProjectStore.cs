using System.IO;
using System.Text.Json;

namespace ModPublisher.Services;

/// <summary>La liste des dossiers de mods gérés par l'app. Tout le reste vit dans le modpage/ de chaque mod.</summary>
public sealed class ProjectStore
{
    private readonly string _path;
    public List<string> Folders { get; private set; } = new();

    public ProjectStore(string? path = null)
    {
        _path = path ?? AppPaths.Projects;
        if (File.Exists(_path))
            Folders = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? new();
    }

    public void Add(string folder)
    {
        folder = Path.GetFullPath(folder);
        if (Folders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))) return;
        Folders.Add(folder);
        Save();
    }

    public void Remove(string folder)
    {
        Folders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    private void Save() => File.WriteAllText(_path, JsonSerializer.Serialize(Folders, new JsonSerializerOptions { WriteIndented = true }));
}
