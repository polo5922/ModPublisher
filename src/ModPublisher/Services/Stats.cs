namespace ModPublisher.Services;

/// <summary>Une version publiée sur un site, réduite à ce qui compte pour les statistiques.</summary>
public record SiteVersion(string Number, DateTime Date, long Downloads, IReadOnlyList<string> GameVersions);

/// <summary>Téléchargements d'une entrée (version du mod, version de Minecraft…) sur chaque site.</summary>
public record SiteSplit(string Label, DateTime Date, long Modrinth, long CurseForge)
{
    public long Total => Modrinth + CurseForge;
}

/// <summary>Calculs des statistiques, sans accès réseau (testés à part).</summary>
public static class StatsMath
{
    /// <summary>Une ligne par numéro de version, les deux sites additionnés ; la plus récente en premier.</summary>
    public static List<SiteSplit> ByVersion(IEnumerable<SiteVersion> modrinth, IEnumerable<SiteVersion> curseForge)
    {
        var rows = new Dictionary<string, SiteSplit>();
        foreach (var v in modrinth)
            rows[v.Number] = rows.TryGetValue(v.Number, out var r)
                ? r with { Modrinth = r.Modrinth + v.Downloads, Date = Max(r.Date, v.Date) }
                : new SiteSplit(v.Number, v.Date, v.Downloads, 0);
        foreach (var v in curseForge)
            rows[v.Number] = rows.TryGetValue(v.Number, out var r)
                ? r with { CurseForge = r.CurseForge + v.Downloads, Date = Max(r.Date, v.Date) }
                : new SiteSplit(v.Number, v.Date, 0, v.Downloads);
        return rows.Values.OrderByDescending(r => r.Date).ToList();
    }

    /// <summary>
    /// Téléchargements par version de Minecraft. Les sites ne disent pas pour quelle version du jeu un fichier a été
    /// pris : un fichier compatible avec plusieurs versions est réparti à parts égales entre elles.
    /// </summary>
    public static List<SiteSplit> ByGameVersion(IEnumerable<SiteVersion> modrinth, IEnumerable<SiteVersion> curseForge)
    {
        var rows = new Dictionary<string, (long Mr, long Cf)>();
        void Add(SiteVersion v, bool isModrinth)
        {
            var games = v.GameVersions.Count > 0 ? v.GameVersions : ["?"];
            for (var i = 0; i < games.Count; i++)
            {
                // Le reste de la division va aux premières, pour que le total reste exact
                var share = v.Downloads / games.Count + (i < v.Downloads % games.Count ? 1 : 0);
                var cur = rows.GetValueOrDefault(games[i]);
                rows[games[i]] = isModrinth ? (cur.Mr + share, cur.Cf) : (cur.Mr, cur.Cf + share);
            }
        }
        foreach (var v in modrinth) Add(v, true);
        foreach (var v in curseForge) Add(v, false);
        return rows.Select(r => new SiteSplit(r.Key, default, r.Value.Mr, r.Value.Cf))
            .OrderByDescending(r => VersionKey(r.Label)).ToList();
    }

    /// <summary>Tri naturel des versions du jeu (26.3 après 1.21.10, 1.21.10 après 1.21.9).</summary>
    public static string VersionKey(string version) =>
        string.Join(".", version.Split('.', '-', '+').Select(p => int.TryParse(p, out var n) ? n.ToString("D6") : p));

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
