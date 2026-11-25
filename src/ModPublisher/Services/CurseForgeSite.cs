using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace ModPublisher.Services;

/// <summary>
/// Appels à l'API interne de authors.curseforge.com, faits depuis une WebView2 invisible connectée avec la session
/// de l'utilisateur : les cookies, la protection anti-robot et les éventuels jetons du site sont gérés par le
/// navigateur lui-même. Non officiel : peut casser si CurseForge change son site.
/// </summary>
public sealed class CurseForgeSite(ApiHttp http, SecretStore secrets)
{
    private const string Bridge = """
        window.__mp = async (id, method, url, body, contentType, headers) => {
          try {
            const opts = { method, credentials: 'include', headers: Object.assign({ 'Accept': 'application/json' }, headers || {}) };
            if (body !== null) {
              if (contentType === 'base64') {
                const bin = atob(body.data), bytes = new Uint8Array(bin.length);
                for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
                const form = new FormData();
                for (const [k, v] of Object.entries(body.fields || {})) form.append(k, v);
                form.append(body.field, new Blob([bytes], { type: body.type }), body.name);
                opts.body = form;
              } else {
                opts.body = body;
                if (contentType) opts.headers['Content-Type'] = contentType;
              }
            }
            const r = await fetch(url, opts);
            chrome.webview.postMessage(JSON.stringify({ id, status: r.status, body: await r.text() }));
          } catch (e) {
            chrome.webview.postMessage(JSON.stringify({ id, status: 0, body: String(e) }));
          }
        };
        """;

    private CoreWebView2Controller? _controller;
    private CoreWebView2? _web;
    private Task? _ready;
    private int _nextId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<(int, string)>> _pending = new();

    /// <summary>À appeler une fois, avec la fenêtre principale (la WebView est attachée à elle mais reste invisible).</summary>
    public void Attach(IntPtr hwnd) => _ready ??= StartAsync(hwnd);

    private async Task StartAsync(IntPtr hwnd)
    {
        var env = await WebViewHost.EnvironmentAsync();
        _controller = await env.CreateCoreWebView2ControllerAsync(hwnd);
        _controller.IsVisible = false;
        _web = _controller.CoreWebView2;
        _web.WebMessageReceived += (_, e) =>
        {
            var msg = JsonNode.Parse(e.TryGetWebMessageAsString())!;
            if (_pending.TryRemove((int)msg["id"]!, out var tcs))
                tcs.SetResult(((int)msg["status"]!, (string?)msg["body"] ?? ""));
        };
        await _web.AddScriptToExecuteOnDocumentCreatedAsync(Bridge);
        await RestoreSessionAsync();
        await NavigateAsync(WebViewHost.CurseForgeAuthors);
    }

    // ---------- Session gardée entre deux lancements ----------

    private const string SessionKey = "curseforge_session";
    private static readonly string[] SessionSites = ["https://authors.curseforge.com", "https://sso.curseforge.com", "https://www.curseforge.com"];

    private record SavedCookie(string Name, string Value, string Domain, string Path, bool Secure, bool HttpOnly, int SameSite,
        DateTime? Expires);

    // Cookies qui prouvent une vraie connexion (sans eux, rien à garder)
    private static readonly string[] AuthCookies = ["cfrt", "cfat", "CobaltSession", "AuthorsUser"];

    /// <summary>
    /// Garde une copie des cookies de connexion CurseForge (chiffrée avec DPAPI, comme les jetons). Le navigateur
    /// intégré peut les perdre à la fermeture (cookies « de session », ou cookies pas encore écrits sur le disque) :
    /// ils sont remis au lancement suivant s'ils manquent et ne sont pas expirés.
    /// </summary>
    public async Task SaveSessionAsync()
    {
        await CoreAsync();
        var saved = new Dictionary<string, SavedCookie>();
        foreach (var site in SessionSites)
            foreach (var c in await _web!.CookieManager.GetCookiesAsync(site))
                saved[$"{c.Name}|{c.Domain}|{c.Path}"] = new(c.Name, c.Value, c.Domain, c.Path, c.IsSecure, c.IsHttpOnly,
                    (int)c.SameSite, c.IsSession ? null : c.Expires.ToUniversalTime());
        if (saved.Values.Any(c => AuthCookies.Contains(c.Name))) secrets.Set(SessionKey, JsonSerializer.Serialize(saved.Values));
    }

    public void ForgetSession() => secrets.Set(SessionKey, null);

    private async Task RestoreSessionAsync()
    {
        if (secrets.Get(SessionKey) is not { } json) return;
        List<SavedCookie>? saved;
        try { saved = JsonSerializer.Deserialize<List<SavedCookie>>(json); }
        catch (JsonException) { return; }
        var manager = _web!.CookieManager;
        var present = new HashSet<string>();
        foreach (var site in SessionSites)
            foreach (var c in await manager.GetCookiesAsync(site))
                present.Add($"{c.Name}|{c.Domain}|{c.Path}");
        foreach (var s in saved ?? [])
        {
            if (present.Contains($"{s.Name}|{s.Domain}|{s.Path}") || s.Expires < DateTime.UtcNow) continue;
            var cookie = manager.CreateCookie(s.Name, s.Value, s.Domain, s.Path);
            cookie.IsSecure = s.Secure;
            cookie.IsHttpOnly = s.HttpOnly;
            cookie.SameSite = (CoreWebView2CookieSameSiteKind)s.SameSite;
            if (s.Expires is { } expires) cookie.Expires = expires.ToLocalTime();
            manager.AddOrUpdateCookie(cookie);
        }
    }

    /// <summary>À la fermeture de l'app : garde la session puis ferme proprement le navigateur caché.</summary>
    public async Task ShutdownAsync()
    {
        if (_web is null) return;
        try { await SaveSessionAsync(); } catch { /* pas connecté : rien à garder */ }
        _controller?.Close();
        _web = null;
    }

    private Task NavigateAsync(string url)
    {
        var done = new TaskCompletionSource();
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            _web!.NavigationCompleted -= Handler;
            done.TrySetResult();
        }
        _web!.NavigationCompleted += Handler;
        _web.Navigate(url);
        return done.Task;
    }

    /// <summary>WebView invisible (mise au point).</summary>
    public async Task<CoreWebView2> CoreAsync()
    {
        await (_ready ?? throw new InvalidOperationException("CurseForgeSite.Attach n'a pas été appelé"));
        return _web!;
    }

    public async Task GoAsync(string url)
    {
        await CoreAsync();
        await NavigateAsync(url);
    }

    /// <summary>Recharge la page (après une connexion dans la fenêtre de login, pour prendre la nouvelle session).</summary>
    public async Task RefreshAsync()
    {
        if (_ready is null) throw new InvalidOperationException("CurseForgeSite.Attach n'a pas été appelé");
        await _ready;
        await NavigateAsync(WebViewHost.CurseForgeAuthors);
    }

    public async Task<IReadOnlyList<CoreWebView2Cookie>> CookiesAsync()
    {
        await (_ready ?? throw new InvalidOperationException("CurseForgeSite.Attach n'a pas été appelé"));
        return await _web!.CookieManager.GetCookiesAsync(WebViewHost.CurseForgeAuthors);
    }

    /// <summary>Requête depuis la page authors.curseforge.com. Renvoie le JSON de la réponse (ou null).</summary>
    public async Task<JsonNode?> FetchAsync(string method, string url, JsonNode? json = null, IDictionary<string, string>? headers = null)
    {
        if (http.Simulation && method != "GET")
        {
            http.Write($"[simulation] {method} {url}" + (json is null ? "" : "\n  " + Shorten(json.ToJsonString())));
            return null;
        }
        var text = await RawAsync(method, url, json?.ToJsonString(), json is null ? null : "application/json", headers);
        return string.IsNullOrWhiteSpace(text) ? null : TryParse(text);
    }

    /// <summary>Envoi d'un fichier en multipart (images de galerie, icône).</summary>
    public async Task<JsonNode?> UploadAsync(string url, string field, string fileName, byte[] data, string type,
        IDictionary<string, string>? fields = null, string method = "POST")
    {
        if (http.Simulation)
        {
            http.Write($"[simulation] {method} {url}\n  {field} = {fileName} <{data.Length} bytes>");
            return null;
        }
        var body = new JsonObject
        {
            ["field"] = field, ["name"] = fileName, ["type"] = type, ["data"] = Convert.ToBase64String(data),
            ["fields"] = JsonSerializer.SerializeToNode(fields ?? new Dictionary<string, string>()),
        };
        var text = await RawAsync(method, url, body, "base64", null);
        return string.IsNullOrWhiteSpace(text) ? null : TryParse(text);
    }

    private async Task<string> RawAsync(string method, string url, object? body, string? contentType, IDictionary<string, string>? headers)
    {
        await (_ready ?? throw new InvalidOperationException("CurseForgeSite.Attach n'a pas été appelé"));
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<(int, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        string Js(object? v) => JsonSerializer.Serialize(v);
        var bodyJs = body switch { null => "null", JsonNode n => n.ToJsonString(), _ => Js(body) };
        await _web!.ExecuteScriptAsync($"__mp({id}, {Js(method)}, {Js(url)}, {bodyJs}, {Js(contentType)}, {Js(headers)})");

        var finished = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(2)));
        if (finished != tcs.Task)
        {
            _pending.TryRemove(id, out _);
            throw new ApiException(L.T($"{method} {url} : pas de réponse de CurseForge", $"{method} {url}: no response from CurseForge"));
        }
        var (status, text) = tcs.Task.Result;
        // Session perdue : le site renvoie la page HTML (ou la page cachée a été redirigée vers la connexion)
        var host = Uri.TryCreate(_web.Source, UriKind.Absolute, out var source) ? source.Host : "";
        if (host != "authors.curseforge.com" || (status is >= 200 and < 300 && text.TrimStart().StartsWith('<')))
            throw new ApiException(L.T("CurseForge : pas connecté. Clique « Se connecter » dans l'onglet Comptes.", "CurseForge: not signed in. Click “Sign in” in the Accounts tab."), 401, text);
        if (status is 401 or 403)
            throw new ApiException(L.T($"CurseForge : session expirée ou refusée (HTTP {status}). Reconnecte-toi dans l'onglet Comptes.", $"CurseForge: session expired or refused (HTTP {status}). Sign in again in the Accounts tab."), status, text);
        if (status is < 200 or >= 300)
            throw new ApiException($"{method} {url} -> HTTP {status}\n{Shorten(text)}", status, text);
        if (method != "GET") http.Write($"{method} {url} -> {status}");
        return text;
    }

    private static JsonNode? TryParse(string text)
    {
        try { return JsonNode.Parse(text); }
        catch (JsonException) { return JsonValue.Create(text); }
    }

    private static string Shorten(string s) => s.Length > 600 ? s[..600] + "…" : s;
}
