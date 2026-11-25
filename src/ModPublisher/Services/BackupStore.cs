using System.IO;

namespace ModPublisher.Services;

/// <summary>Copie du contenu distant avant chaque modification, pour pouvoir revenir en arrière.</summary>
public static class BackupStore
{
    public static string Save(string modName, string site, string kind, string content)
    {
        var dir = DirFor(modName);
        var file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{site}-{Safe(kind)}.md");
        File.WriteAllText(file, content);
        return file;
    }

    public static string DirFor(string modName)
    {
        var dir = Path.Combine(AppPaths.Backups, Safe(modName));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Safe(string s) =>
        string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
}
