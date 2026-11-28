using System.IO;
using ModPublisher.Services;

namespace ModPublisher.Tests;

public class DescriptionConverterTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)).Replace("\r\n", "\n");

    [Fact]
    public void ModrinthDescriptionBecomesTheCurseForgeOne()
    {
        var converted = DescriptionConverter.ToCurseForge(Fixture("description-modrinth.md"));
        var expected = Fixture("description-curseforge.md");
        Assert.Equal(DescriptionConverter.Normalize(expected), DescriptionConverter.Normalize(converted));
    }

    [Fact]
    public void DetailsBecomeHeadings()
    {
        var md = "Intro\n\n<details>\n<summary><b>📜 Liste</b></summary>\n\n- a\n\n</details>\n\nFin";
        Assert.Equal("Intro\n\n### 📜 Liste\n\n- a\n\nFin", DescriptionConverter.ToCurseForge(md));
    }

    [Fact]
    public void CodeWithAngleBracketsIsUntouched()
    {
        const string md = "Use `/locate biome mod:<biome>` here";
        Assert.Equal(md, DescriptionConverter.ToCurseForge(md));
    }

    [Fact]
    public void CurseForgeEntitiesDoNotCountAsDifferences()
    {
        const string local = "Find them with `/locate biome crystalcaves:<biome>`";
        const string online = "Find them with `/locate biome crystalcaves:&lt;biome&gt;`";
        Assert.True(DescriptionConverter.SameText(DescriptionConverter.DecodeEntities(online), DescriptionConverter.DecodeEntities(local)));
    }

    [Fact]
    public void LocalImagesAreReplacedByTheirUrl()
    {
        var urls = new Dictionary<string, string> { ["images/banner.png"] = "https://cdn/banner.png" };
        var missing = new List<string>();
        var md = DescriptionConverter.ResolveImages("![b](images/banner.png) ![x](images/x.png) <img src=\"./images/banner.png\">", urls, missing);
        Assert.Equal("![b](https://cdn/banner.png) ![x](images/x.png) <img src=\"https://cdn/banner.png\">", md);
        Assert.Equal(new[] { "images/x.png" }, missing);
    }
}

public class ChangelogReaderTests
{
    private const string Multi = "# Mod 1.0.1 — fix\n- a\n\n# Mod 1.0.0 — init\n- b\n";

    [Fact]
    public void PicksTheSectionOfTheVersion()
    {
        Assert.Equal("# Mod 1.0.0 — init\n- b", ChangelogReader.Extract(Multi, "1.0.0"));
        Assert.Equal("# Mod 1.0.1 — fix\n- a", ChangelogReader.Extract(Multi, "1.0.1"));
    }

    [Fact]
    public void PartialVersionDoesNotMatch() =>
        Assert.Throws<InvalidOperationException>(() => ChangelogReader.Extract(Multi, "1.0"));

    [Fact]
    public void SingleSectionIsTakenWhole() =>
        Assert.Equal("# Title\n\n## Added\n- x", ChangelogReader.Extract("# Title\r\n\r\n## Added\r\n- x\r\n", "9.9.9"));
}

public class CurseForgeIdsTests
{
    private static readonly List<CurseForgeUploadClient.GameVersion> All =
    [
        new(1, "26.3", "minecraft-26-3"),
        new(2, "26.3", "some-other-game"),
        new(3, "Fabric", "modloader"),
        new(4, "Client", "environment"),
        new(5, "Server", "environment"),
        new(6, "Java 25", "java"),
    ];

    [Fact]
    public void NamesAreResolvedWithinTheirType() =>
        Assert.Equal(new long[] { 1, 3, 4, 5, 6 },
            CurseForgeUploadClient.ResolveIds(All, ["26.3"], ["fabric"], ["Client", "Server"], ["Java 25"]));

    [Fact]
    public void UnknownVersionFails() =>
        Assert.Throws<ApiException>(() => CurseForgeUploadClient.ResolveIds(All, ["99.1"], [], [], []));
}

public class LanguageTests
{
    [Theory]
    [InlineData("fr-FR", true)]
    [InlineData("fr-CA", true)]
    [InlineData("en-US", false)]
    [InlineData("de-DE", false)]
    public void FrenchOnlyOnAFrenchSystem(string culture, bool french) =>
        Assert.Equal(french, L.IsFrench(System.Globalization.CultureInfo.GetCultureInfo(culture)));
}

public class LineDiffTests
{
    [Fact]
    public void FindsAddedAndRemovedLines()
    {
        var diff = LineDiff.Compute("a\nb\nc", "a\nc\nd");
        Assert.Equal(["  a", "- b", "  c", "+ d"], diff.Select(l => l.Prefix + l.Text));
    }
}

public class ModPageTests
{
    [Fact]
    public void ReadsGradleAndDefaults()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "gradle.properties"), "minecraft_version=26.3\nversion=1.2.3\n");
            File.WriteAllText(Path.Combine(dir, "settings.gradle"), "rootProject.name = 'coolmod'\n");
            Directory.CreateDirectory(Path.Combine(dir, "src", "main", "resources"));
            File.WriteAllText(Path.Combine(dir, "src", "main", "resources", "fabric.mod.json"), "{\"name\": \"Cool Mod\", \"icon\": \"assets/coolmod/icon.png\"}");

            var mod = new ModPage(dir);
            // Icône : aucune, puis celle de fabric.mod.json, puis celle de la page qui a priorité
            Assert.Null(mod.IconPath);
            var fabricIcon = Path.Combine(dir, "src", "main", "resources", "assets", "coolmod", "icon.png");
            Directory.CreateDirectory(Path.GetDirectoryName(fabricIcon)!);
            File.WriteAllBytes(fabricIcon, [1]);
            Assert.Equal(fabricIcon, mod.IconPath);
            Directory.CreateDirectory(Path.GetDirectoryName(mod.PageIconPath)!);
            File.WriteAllBytes(mod.PageIconPath, [2]);
            Assert.Equal(mod.PageIconPath, mod.IconPath);

            Assert.Equal("Cool Mod", mod.Name);
            Assert.Equal("1.2.3", mod.GradleVersion());
            Assert.Equal(["26.3"], mod.GameVersions);
            Assert.Equal(["fabric"], mod.Loaders);
            Assert.EndsWith(Path.Combine("build", "libs", "coolmod-1.2.3.jar"), mod.JarPath("1.2.3"));
            Assert.Equal("Cool Mod 1.2.3", mod.VersionName("1.2.3"));
            // Réglages par défaut d'un nouveau mod : l'ID CurseForge vide se lit sans erreur
            Assert.Equal(0, mod.CurseForgeProjectId);
            mod.Section("curseforge")["project_id"] = 42;
            Assert.Equal(42, mod.CurseForgeProjectId);

            mod.ModrinthProject = "cool-mod";
            mod.CurseForgeProjectId = 123456;
            mod.SaveSettings();
            var again = new ModPage(dir);
            Assert.Equal("cool-mod", again.ModrinthProject);
            Assert.Equal(123456, again.CurseForgeProjectId);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class StatsMathTests
{
    private static SiteVersion V(string n, int day, long dl, params string[] games) => new(n, new DateTime(2026, 1, day), dl, games);

    [Fact]
    public void VersionsOfBothSitesAreAddedUp()
    {
        var rows = StatsMath.ByVersion([V("1.0", 1, 10, "26.3"), V("1.1", 5, 4, "26.3")], [V("1.0", 2, 6, "26.3"), V("0.9", 1, 3)]);
        Assert.Equal(["1.1", "1.0", "0.9"], rows.Select(r => r.Label));
        var v10 = rows.Single(r => r.Label == "1.0");
        Assert.Equal((10, 6, 16), (v10.Modrinth, v10.CurseForge, v10.Total));
        Assert.Equal(new DateTime(2026, 1, 2), v10.Date);
    }

    [Fact]
    public void GameVersionsShareDownloadsWithoutLosingAny()
    {
        var rows = StatsMath.ByGameVersion([V("1.0", 1, 7, "1.21.9", "1.21.10")], [V("1.0", 1, 5, "26.3")]);
        Assert.Equal(["26.3", "1.21.10", "1.21.9"], rows.Select(r => r.Label));
        Assert.Equal(12, rows.Sum(r => r.Total));
        Assert.Equal(4, rows.Single(r => r.Label == "1.21.9").Modrinth);
    }
}
