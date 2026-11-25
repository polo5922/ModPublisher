using System.IO;
using System.Text.RegularExpressions;

namespace ModPublisher.Services;

public static class ChangelogReader
{
    /// <summary>
    /// Prend la section « # ... version ... » si le fichier en contient plusieurs, sinon le texte entier
    /// (même règle que modpage/publish.py).
    /// </summary>
    public static string Extract(string text, string version)
    {
        text = text.Replace("\r\n", "\n").Trim();
        var titled = Regex.Split(text, @"(?m)^(?=# )").Where(s => s.StartsWith("# ")).ToList();
        if (titled.Count <= 1) return text;

        var pattern = new Regex(@"(?<![\w.])" + Regex.Escape(version) + @"(?![\w.])");
        var section = titled.FirstOrDefault(s => pattern.IsMatch(s.Split('\n')[0]));
        return section?.Trim() ?? throw new InvalidOperationException(L.T($"Aucune section « # ... {version} » dans le changelog", $"No “# ... {version}” section in the changelog"));
    }

    public static string ExtractFile(string path, string version)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(L.T($"Changelog introuvable : {path}", $"Changelog not found: {path}"));
        return Extract(File.ReadAllText(path), version);
    }
}
