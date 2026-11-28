using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ModPublisher.Services;
using ModPublisher.ViewModels;

namespace ModPublisher.Views;

/// <summary>
/// Outil de mise au point : « ModPublisher.exe --screenshot dossier [--dark|--light] [--demo] » enregistre une capture
/// de chaque onglet puis quitte. « --demo » remplit l'app de données fictives, sans appel réseau (captures du README).
/// </summary>
public static class ScreenshotMode
{
    public static string? Folder { get; private set; }
    public static bool Demo { get; private set; }

    public static void Parse(string[] args)
    {
        var i = Array.IndexOf(args, "--screenshot");
        if (i < 0 || i + 1 >= args.Length) return;
        Folder = args[i + 1];
        Demo = args.Contains("--demo");
#pragma warning disable WPF0001
        if (args.Contains("--dark")) Application.Current.ThemeMode = ThemeMode.Dark;
        if (args.Contains("--light")) Application.Current.ThemeMode = ThemeMode.Light;
#pragma warning restore WPF0001
    }

    public static async Task RunAsync(Window window, TabControl tabs, WebView2 preview)
    {
        if (Folder is null) return;
        Directory.CreateDirectory(Folder);
        window.Width = 1440;
        window.Height = 920;
        await Task.Delay(2500);
        for (var i = 0; i < tabs.Items.Count; i++)
        {
            tabs.SelectedIndex = i;
            await Task.Delay(1500);
            window.UpdateLayout();
            var root = (FrameworkElement)window.Content;
            var dpi = VisualTreeHelper.GetDpi(window);
            var bitmap = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var dc = background.RenderOpen())
                dc.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            bitmap.Render(background);
            bitmap.Render(root);
            // Une WebView2 n'est pas dessinée par WPF : on colle son image à sa place
            if (preview.IsVisible && preview.CoreWebView2 is not null)
            {
                using var stream = new MemoryStream();
                await preview.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                stream.Position = 0;
                var page = new DrawingVisual();
                using (var dc = page.RenderOpen())
                    dc.DrawImage(BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad),
                        preview.TransformToAncestor(root).TransformBounds(new Rect(preview.RenderSize)));
                bitmap.Render(page);
            }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var header = (tabs.Items[i] as TabItem)?.Tag as string ?? $"tab{i}";
            await using var file = File.Create(Path.Combine(Folder, $"{i + 1}-{header}.png"));
            encoder.Save(file);
        }
        window.Close();
    }

    /// <summary>Comptes, versions et galerie fictifs (le mod vient du dossier de démonstration).</summary>
    public static void FillDemo(MainViewModel vm)
    {
        vm.Accounts.ModrinthOk = vm.Accounts.CurseForgeOk = true;
        vm.Accounts.ModrinthState = vm.Accounts.CurseForgeState = L.T("Connecté : ", "Signed in: ") + "demo-user";
        vm.Accounts.CurseForgeUploadState = L.T("Jeton d'upload enregistré", "Upload token saved");
        vm.Page.ModrinthStatus = L.T("✔ Identique", "✔ Identical");
        vm.Page.CurseForgeStatus = L.T("Description différente", "Description differs");

        (string Number, string Type, int Days, long Downloads)[] versions =
            [("1.3.0", "release", 6, 1840), ("1.2.1", "release", 27, 5210), ("1.2.0", "release", 41, 3975), ("1.1.0-beta", "beta", 70, 640), ("1.0.0", "release", 96, 2310)];
        var start = new DateTime(2026, 11, 20);
        foreach (var v in versions)
            vm.Versions.Rows.Add(new VersionRow
            {
                Number = v.Number, Name = "Crystal Caves " + v.Number, Type = v.Type, GameVersions = "26.3", Loaders = "fabric",
                Date = start.AddDays(-v.Days), Downloads = v.Downloads, Featured = v.Number == "1.3.0",
                Modrinth = new JsonObject { ["changelog"] = $"## {v.Number}\n\n- New glowing crystals\n- Bug fixes\n" },
                CurseForge = v.Type == "beta" ? null : new CfFile(0, v.Number, "", v.Type, ["26.3"], start, 0, null),
            });
        vm.Versions.Selected = vm.Versions.Rows[0];
        vm.Versions.CheckReport = L.T("✔ Jar : crystal-caves-1.4.0.jar (412 Ko)", "✔ Jar: crystal-caves-1.4.0.jar (412 KB)");

        var images = vm.SelectedMod is { } mod && Directory.Exists(mod.Page.GalleryDir) ? Directory.GetFiles(mod.Page.GalleryDir).Order().ToList() : [];
        foreach (var file in images)
        {
            var title = Path.GetFileNameWithoutExtension(file)[3..].Replace('_', ' ');
            vm.Gallery.ModrinthItems.Add(new GalleryItem { Site = "Modrinth", Url = file, Title = title, Description = "Crystal Caves" });
            if (file != images[^1]) vm.Gallery.CurseForgeItems.Add(new GalleryItem { Site = "CurseForge", Url = file, Title = title });
        }
        vm.Gallery.ModrinthStatus = $"{vm.Gallery.ModrinthItems.Count} images";
        vm.Gallery.CurseForgeStatus = $"{vm.Gallery.CurseForgeItems.Count} images";
        vm.Gallery.SelectedModrinth = vm.Gallery.ModrinthItems.FirstOrDefault();

        // Statistiques : mêmes versions, partagées entre les deux sites
        var mr = versions.Select(v => new SiteVersion(v.Number, start.AddDays(-v.Days), v.Downloads * 3 / 5, v.Number == "1.0.0" ? ["26.2", "26.3"] : ["26.3"])).ToList();
        var cf = versions.Where(v => v.Type != "beta").Select(v => new SiteVersion(v.Number, start.AddDays(-v.Days), v.Downloads * 2 / 5, v.Number == "1.0.0" ? ["26.2"] : ["26.3"])).ToList();
        vm.Stats.ModrinthDownloads = mr.Sum(v => v.Downloads);
        vm.Stats.CurseForgeDownloads = cf.Sum(v => v.Downloads);
        vm.Stats.Followers = 412;
        vm.Stats.ModrinthStatus = $"{mr.Count} versions";
        vm.Stats.CurseForgeStatus = L.T($"{cf.Count} fichiers", $"{cf.Count} files");
        vm.Stats.Show(mr, cf, start.AddDays(-96));
        vm.Stats.Updated = L.T("Mis à jour à 14:05", "Updated at 2:05 PM");
        vm.Stats.ShowAccount([
            new("Crystal Caves", "Modrinth", vm.Stats.ModrinthDownloads, 412, start.AddDays(-96), start.AddDays(-6), "", true),
            new("Crystal Caves", "CurseForge", vm.Stats.CurseForgeDownloads, null, start.AddDays(-95), start.AddDays(-6), "", true),
            new("Lantern Lane", "Modrinth", 8420, 133, start.AddDays(-300), start.AddDays(-40), "", false),
            new("Lantern Lane", "CurseForge", 5130, null, start.AddDays(-298), start.AddDays(-40), "", false),
            new("Tiny Tide", "Modrinth", 1275, 38, start.AddDays(-150), start.AddDays(-90), "", false),
        ]);
    }
}
