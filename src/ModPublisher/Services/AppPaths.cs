using System.IO;

namespace ModPublisher.Services;

/// <summary>Où l'app range ses données : %APPDATA%\ModPublisher (réglages) et %LOCALAPPDATA%\ModPublisher (sessions WebView2).
/// La variable d'environnement MODPUBLISHER_HOME met tout dans un autre dossier (essais, démonstration).</summary>
public static class AppPaths
{
    public static string Data { get; } = Dir(Environment.SpecialFolder.ApplicationData);
    public static string Local { get; } = Dir(Environment.SpecialFolder.LocalApplicationData);

    public static string Projects => Path.Combine(Data, "projects.json");
    public static string Secrets => Path.Combine(Data, "secrets.bin");
    public static string Backups => Path.Combine(Data, "backups");
    public static string NetLog => Path.Combine(Data, "netlog.jsonl");
    public static string WebViewProfile => Path.Combine(Local, "WebView2");

    private static string Dir(Environment.SpecialFolder root)
    {
        var home = Environment.GetEnvironmentVariable("MODPUBLISHER_HOME");
        var dir = string.IsNullOrEmpty(home) ? Path.Combine(Environment.GetFolderPath(root), "ModPublisher") : Path.Combine(home, root.ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }
}
