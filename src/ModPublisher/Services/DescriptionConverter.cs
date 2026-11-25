using System.Text.RegularExpressions;

namespace ModPublisher.Services;

/// <summary>
/// La description maître est écrite pour Modrinth (Markdown + un peu de HTML). CurseForge, en mode Markdown,
/// n'affiche pas &lt;details&gt; ni le centrage : on les traduit en Markdown simple.
/// </summary>
public static class DescriptionConverter
{
    /// <summary>Remplace les chemins locaux « images/... » par leur URL hébergée (image-urls.json).</summary>
    public static string ResolveImages(string markdown, IReadOnlyDictionary<string, string> urls, ICollection<string>? missing = null)
    {
        string Map(string path)
        {
            var key = path.TrimStart('.', '/');
            if (urls.TryGetValue(key, out var url)) return url;
            missing?.Add(key);
            return path;
        }

        markdown = Regex.Replace(markdown, @"\]\(((?:\./)?images/[^)\s]+)", m => "](" + Map(m.Groups[1].Value));
        return Regex.Replace(markdown, @"src=""((?:\./)?images/[^""]+)""", m => $"src=\"{Map(m.Groups[1].Value)}\"");
    }

    public static string ToCurseForge(string markdown)
    {
        var md = markdown.Replace("\r\n", "\n");

        // <details><summary>Titre</summary> -> ### Titre ; </details> disparaît
        md = Regex.Replace(md, @"<details>\s*<summary>(.*?)</summary>",
            m => "### " + StripTags(InlineToMarkdown(m.Groups[1].Value)).Trim(), RegexOptions.Singleline);
        md = Regex.Replace(md, @"^[ \t]*</details>[ \t]*\n?", "", RegexOptions.Multiline);

        // Blocs centrés -> paragraphes normaux
        md = Regex.Replace(md, @"<(p|div)\s+align=""center""\s*>(.*?)</\1>",
            m => InlineToMarkdown(m.Groups[2].Value).Trim(), RegexOptions.Singleline);
        md = Regex.Replace(md, @"</?center>", "");

        md = Regex.Replace(md, @"\n{3,}", "\n\n");
        return md;
    }

    private static string InlineToMarkdown(string html)
    {
        html = Regex.Replace(html, @"</?(b|strong)>", "**");
        html = Regex.Replace(html, @"</?(i|em)>", "*");
        html = Regex.Replace(html, @"<img\s+[^>]*?src=""([^""]+)""[^>]*?(?:alt=""([^""]*)"")?[^>]*>", m => $"![{m.Groups[2].Value}]({m.Groups[1].Value})");
        html = Regex.Replace(html, @"[ \t]*<br\s*/?>[ \t]*\n?", "\n\n");
        return html;
    }

    private static string StripTags(string s) => s.Replace("**", "").Replace("*", "");

    /// <summary>Comparaison tolérante : fins de ligne, espaces de fin et lignes vides en trop ignorés.</summary>
    public static bool SameText(string? a, string? b) => Normalize(a) == Normalize(b);

    /// <summary>&amp;lt; → &lt; etc. (CurseForge renvoie le Markdown avec ces caractères encodés).</summary>
    public static string DecodeEntities(string? s) => System.Net.WebUtility.HtmlDecode(s ?? "");

    public static string Normalize(string? s) => Regex.Replace(
        string.Join("\n", (s ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())), @"\n{3,}", "\n\n").Trim();
}
