using System.IO;
using System.Windows;
using ModPublisher.Services;
using ModPublisher.ViewModels;

namespace ModPublisher.Views;

/// <summary>
/// Mise au point : « ModPublisher.exe --probe dossier [url1|url2…] » ouvre ces pages de authors.curseforge.com dans
/// la WebView invisible (session de l'utilisateur), note les appels JSON du site dans dossier/probe.jsonl, vérifie
/// le jeton d'upload, puis quitte. Lecture seule : l'app n'envoie aucune requête d'écriture.
/// </summary>
public static class ProbeMode
{
    public static string? Folder { get; private set; }
    private static string[] _urls = [WebViewHost.CurseForgeAuthors];
    private static string? _eval;

    public static void Parse(string[] args)
    {
        var i = Array.IndexOf(args, "--probe");
        if (i < 0 || i + 1 >= args.Length) return;
        Folder = args[i + 1];
        if (i + 2 < args.Length && !args[i + 2].StartsWith("--")) _urls = args[i + 2].Split('|');
        // --eval fichier.js : script exécuté dans la page, qui renvoie son résultat par postMessage({id:-1, body})
        var j = Array.IndexOf(args, "--eval");
        if (j >= 0 && j + 1 < args.Length) _eval = File.ReadAllText(args[j + 1]);
    }

    public static async Task RunAsync(Window window, MainViewModel vm)
    {
        if (Folder is null) return;
        Directory.CreateDirectory(Folder);
        var report = new List<string>();
        try
        {
            // Même appel qu'au démarrage normal, tout de suite
            await vm.Accounts.RefreshAsync();
            report.Add($"État au démarrage : {vm.Accounts.CurseForgeState}");
            await vm.Versions.RefreshCommand.ExecuteAsync(null);
            foreach (var r in vm.Versions.Rows) report.Add($"  version {r.Number} [{r.Sites}] {r.Name} {r.Date:g}");
            var cookies = await vm.S.CurseForgeSite.CookiesAsync();
            report.Add($"Cookies authors.curseforge.com : {string.Join(", ", cookies.Select(c => c.Name))}");
            report.Add($"Jeton d'upload : {(vm.S.CurseForgeUpload.HasToken ? "présent" : "absent")}");
            if (vm.S.CurseForgeUpload.HasToken)
            {
                try { report.Add($"API d'upload : OK, {(await vm.S.CurseForgeUpload.GameVersionsAsync()).Count} versions de jeu"); }
                catch (Exception e) { report.Add("API d'upload : ÉCHEC " + e.Message.Split('\n')[0]); }
            }

            // Lectures de l'API interne pour le premier mod qui a un ID CurseForge
            try
            {
                report.Add($"Compte : {await vm.S.CurseForge.GetUsernameAsync()}");
                if (vm.Mods.FirstOrDefault(m => m.Page.CurseForgeProjectId != 0)?.Page.CurseForgeProjectId is { } pid)
                {
                    var p = await vm.S.CurseForge.GetProjectAsync(pid);
                    report.Add($"Projet {pid} : résumé « {p.Summary} », description {p.Description.Length} car., " +
                               $"liens [{string.Join(", ", p.Links.Keys)}], logo {p.IconUrl}");
                    foreach (var f in await vm.S.CurseForge.GetFilesAsync(pid))
                        report.Add($"  fichier {f.Id} {f.DisplayName} {f.ReleaseType} [{string.Join(", ", f.GameVersions)}] {f.Date:g} {f.Downloads} tél.");
                    foreach (var i in await vm.S.CurseForge.GetImagesAsync(pid))
                        report.Add($"  image {i.Id} « {i.Title} » {i.Url}");
                }
            }
            catch (Exception e) { report.Add("API interne : ÉCHEC " + e.Message.Split('\n')[0]); }

            var core = await vm.S.CurseForgeSite.CoreAsync();
            var recorder = new NetRecorder(core, "curseforge.com", Path.Combine(Folder, "probe.jsonl"));
            foreach (var url in _urls)
            {
                if (url.Contains('#') && core.Source.StartsWith(WebViewHost.CurseForgeAuthors))
                    await core.ExecuteScriptAsync($"location.href = {System.Text.Json.JsonSerializer.Serialize(url)}");
                else
                    await Task.WhenAny(vm.S.CurseForgeSite.GoAsync(url), Task.Delay(20000));
                await Task.Delay(8000);
                report.Add($"{url} -> {core.Source} : {recorder.Count} appels notés");
            }
            recorder.Stop();

            if (_eval is not null)
            {
                var result = new TaskCompletionSource<string>();
                core.WebMessageReceived += (_, e) =>
                {
                    var msg = System.Text.Json.Nodes.JsonNode.Parse(e.TryGetWebMessageAsString());
                    if ((int?)msg?["id"] == -1) result.TrySetResult((string?)msg!["body"] ?? "");
                };
                await core.ExecuteScriptAsync(_eval);
                await Task.WhenAny(result.Task, Task.Delay(60000));
                await File.WriteAllTextAsync(Path.Combine(Folder, "eval.txt"), result.Task.IsCompleted ? result.Task.Result : "(pas de réponse)");
            }
        }
        catch (Exception e) { report.Add("ERREUR " + e); }
        await File.WriteAllLinesAsync(Path.Combine(Folder, "probe.txt"), report);
        window.Close();
    }
}
