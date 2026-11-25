namespace ModPublisher.Services;

/// <summary>Diff ligne à ligne (plus longue sous-suite commune), assez pour comparer deux descriptions.</summary>
public static class LineDiff
{
    public enum Kind { Same, Added, Removed }

    public record Line(Kind Kind, string Text)
    {
        public string Prefix => Kind switch { Kind.Added => "+ ", Kind.Removed => "- ", _ => "  " };
    }

    /// <summary>Lignes de <paramref name="before"/> absentes de <paramref name="after"/> = Removed, l'inverse = Added.</summary>
    public static List<Line> Compute(string before, string after)
    {
        var a = DescriptionConverter.Normalize(before).Split('\n');
        var b = DescriptionConverter.Normalize(after).Split('\n');
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var result = new List<Line>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y]) { result.Add(new(Kind.Same, a[x])); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) result.Add(new(Kind.Removed, a[x++]));
            else result.Add(new(Kind.Added, b[y++]));
        }
        while (x < a.Length) result.Add(new(Kind.Removed, a[x++]));
        while (y < b.Length) result.Add(new(Kind.Added, b[y++]));
        return result;
    }
}
