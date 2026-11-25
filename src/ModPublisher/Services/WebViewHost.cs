using Microsoft.Web.WebView2.Core;

namespace ModPublisher.Services;

/// <summary>
/// Un seul profil WebView2 pour toute l'app : la connexion faite dans la fenêtre de login reste valable pour la
/// WebView cachée qui appelle le site CurseForge, et d'un lancement à l'autre.
/// </summary>
public static class WebViewHost
{
    private static Task<CoreWebView2Environment>? _env;

    public static Task<CoreWebView2Environment> EnvironmentAsync() =>
        _env ??= CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewProfile);

    public const string ModrinthHome = "https://modrinth.com";
    public const string ModrinthLogin = "https://modrinth.com/auth/sign-in";
    public const string CurseForgeAuthors = "https://authors.curseforge.com/";

    /// <summary>Cookie de session du site Modrinth : il sert directement d'en-tête Authorization pour l'API.</summary>
    public static async Task<string?> ModrinthSessionTokenAsync(CoreWebView2 web)
    {
        var cookies = await web.CookieManager.GetCookiesAsync(ModrinthHome);
        return cookies.FirstOrDefault(c => c.Name == "auth-token" && c.Value.Length > 0)?.Value;
    }

    public static async Task ClearCookiesAsync(CoreWebView2 web, params string[] hosts)
    {
        foreach (var host in hosts)
            foreach (var c in await web.CookieManager.GetCookiesAsync(host))
                web.CookieManager.DeleteCookie(c);
    }
}
