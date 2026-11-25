using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModPublisher.Services;
using ModPublisher.Views;

namespace ModPublisher.ViewModels;

public sealed partial class AccountsViewModel(MainViewModel main) : ObservableObject
{
    private AppServices S => main.S;

    [ObservableProperty] private string _modrinthState = L.T("Non vérifié", "Not checked");
    [ObservableProperty] private bool _modrinthOk;
    [ObservableProperty] private string _modrinthPat = "";
    [ObservableProperty] private string _curseForgeState = L.T("Non vérifié", "Not checked");
    [ObservableProperty] private bool _curseForgeOk;
    [ObservableProperty] private string _curseForgeUploadState = "";
    [ObservableProperty] private string _curseForgeUploadToken = "";

    public string NetLogPath => AppPaths.NetLog;

    public async Task RefreshAsync()
    {
        if (S.Modrinth.HasToken)
        {
            try
            {
                ModrinthState = L.T("Connecté : ", "Signed in: ") + await S.Modrinth.GetUsernameAsync();
                ModrinthOk = true;
            }
            catch (ApiException e) when (e.Status == 401)
            {
                ModrinthState = L.T("Session expirée : reconnecte-toi", "Session expired: sign in again");
                ModrinthOk = false;
            }
            catch (Exception e)
            {
                ModrinthState = L.T("Erreur : ", "Error: ") + e.Message.Split('\n')[0];
                ModrinthOk = false;
            }
        }
        else
        {
            ModrinthState = L.T("Non connecté", "Not signed in");
            ModrinthOk = false;
        }

        CurseForgeUploadState = S.CurseForgeUpload.HasToken ? L.T("Jeton d'upload enregistré", "Upload token saved") : L.T("Pas de jeton d'upload", "No upload token");
        try
        {
            var name = await S.CurseForge.GetUsernameAsync();
            CurseForgeOk = name is not null;
            CurseForgeState = name is null ? L.T("Non connecté", "Not signed in") : L.T($"Connecté : {name}", $"Signed in: {name}");
        }
        catch (ApiException e) when (e.Status == 401)
        {
            CurseForgeOk = false;
            CurseForgeState = L.T("Non connecté : clique « Se connecter »", "Not signed in: click “Sign in”");
        }
        catch (Exception e)
        {
            CurseForgeOk = false;
            CurseForgeState = L.T("Erreur : ", "Error: ") + e.Message.Split('\n')[0];
        }
    }

    [RelayCommand]
    private Task Refresh() => main.RunAsync(L.T("Vérification des comptes", "Checking accounts"), RefreshAsync);

    // ---------- Modrinth ----------

    [RelayCommand]
    private async Task LoginModrinth()
    {
        var window = new BrowserWindow(L.T("Connexion à Modrinth", "Sign in to Modrinth"), WebViewHost.ModrinthLogin,
            L.T("Connecte-toi normalement ; la fenêtre se ferme toute seule une fois connecté.", "Sign in as usual; the window closes by itself once you are signed in."),
            async w => await WebViewHost.ModrinthSessionTokenAsync(w.Core) is { } token && Save(token))
        { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        await main.RunAsync(L.T("Vérification de Modrinth", "Checking Modrinth"), RefreshAsync);

        bool Save(string token)
        {
            S.Secrets.Set(SecretStore.ModrinthToken, token);
            return true;
        }
    }

    [RelayCommand]
    private async Task SaveModrinthPat()
    {
        S.Secrets.Set(SecretStore.ModrinthToken, ModrinthPat);
        ModrinthPat = "";
        await main.RunAsync(L.T("Vérification de Modrinth", "Checking Modrinth"), RefreshAsync);
    }

    [RelayCommand]
    private async Task LogoutModrinth()
    {
        S.Secrets.Set(SecretStore.ModrinthToken, null);
        var env = await WebViewHost.EnvironmentAsync();
        var window = new BrowserWindow(L.T("Déconnexion", "Sign out"), "about:blank", "", async w =>
        {
            await WebViewHost.ClearCookiesAsync(w.Core, WebViewHost.ModrinthHome);
            return true;
        }) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        await RefreshAsync();
    }

    // ---------- CurseForge ----------

    [RelayCommand]
    private async Task LoginCurseForge()
    {
        var window = new BrowserWindow(L.T("Connexion à CurseForge", "Sign in to CurseForge"), WebViewHost.CurseForgeAuthors,
            L.T("Connecte-toi à authors.curseforge.com, puis clique Terminé.", "Sign in to authors.curseforge.com, then click Done."))
        { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        await main.RunAsync(L.T("Vérification de CurseForge", "Checking CurseForge"), async () =>
        {
            await S.CurseForgeSite.RefreshAsync();
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task LogoutCurseForge()
    {
        var window = new BrowserWindow(L.T("Déconnexion", "Sign out"), "about:blank", "", async w =>
        {
            await WebViewHost.ClearCookiesAsync(w.Core, "https://authors.curseforge.com", "https://sso.curseforge.com", "https://www.curseforge.com", "https://curseforge.com");
            return true;
        }) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        S.CurseForgeSite.ForgetSession();
        await S.CurseForgeSite.RefreshAsync();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SaveCurseForgeUploadToken()
    {
        S.Secrets.Set(SecretStore.CurseForgeUploadToken, CurseForgeUploadToken);
        CurseForgeUploadToken = "";
        await main.RunAsync(L.T("Vérification du jeton CurseForge", "Checking the CurseForge token"), async () =>
        {
            var versions = await S.CurseForgeUpload.GameVersionsAsync();
            main.Log(L.T($"Jeton CurseForge valide ({versions.Count} versions de jeu connues).", $"CurseForge token is valid ({versions.Count} known game versions)."));
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private void OpenCurseForgeTokens() =>
        Process.Start(new ProcessStartInfo("https://authors.curseforge.com/#/api-tokens") { UseShellExecute = true });

    /// <summary>Mise au point : navigation sur le site avec enregistrement des appels d'API dans netlog.jsonl.</summary>
    [RelayCommand]
    private async Task RecordCurseForge()
    {
        var window = new BrowserWindow(L.T("Enregistrement des appels CurseForge", "Recording CurseForge calls"), WebViewHost.CurseForgeAuthors,
            L.T("Clique « Enregistrer » puis fais à la main ce que l'app doit savoir faire (modifier la description, un fichier, la galerie…).", "Click “Record”, then do by hand what the app must learn to do (edit the description, a file, the gallery…)."),
            recordHost: "curseforge.com")
        { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        await S.CurseForgeSite.RefreshAsync();
        if (File.Exists(AppPaths.NetLog)) main.Log(L.T($"Appels enregistrés dans {AppPaths.NetLog}", $"Calls recorded in {AppPaths.NetLog}"));
    }
}
