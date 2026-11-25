using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace ModPublisher.Services;

/// <summary>
/// Outil de mise au point : note dans netlog.jsonl les appels d'API faits par le site pendant qu'on s'en sert à la
/// main, pour découvrir l'API interne de CurseForge. Jamais les en-têtes (cookies) ; les champs qui ressemblent à
/// des mots de passe ou des jetons sont masqués, et les pages de connexion ignorées.
/// </summary>
public sealed class NetRecorder
{
    private static readonly Regex Static = new(@"\.(js|mjs|css|png|jpe?g|gif|webp|svg|ico|woff2?|ttf|map)(\?|$)", RegexOptions.IgnoreCase);
    private static readonly Regex Sensitive = new(@"login|signin|sign-in|oauth|password|token|auth", RegexOptions.IgnoreCase);
    private static readonly Regex SecretField = new(@"(""[^""]*(password|token|secret|auth)[^""]*""\s*:\s*)""[^""]*""", RegexOptions.IgnoreCase);

    private readonly CoreWebView2 _web;
    private readonly string _hostFilter;
    public int Count { get; private set; }
    public event Action<string>? Recorded;

    private readonly string _path;

    public NetRecorder(CoreWebView2 web, string hostFilter, string? path = null)
    {
        _web = web;
        _hostFilter = hostFilter;
        _path = path ?? AppPaths.NetLog;
        _web.WebResourceResponseReceived += OnResponse;
    }

    public void Stop() => _web.WebResourceResponseReceived -= OnResponse;

    private async void OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            var uri = new Uri(e.Request.Uri);
            if (!uri.Host.EndsWith(_hostFilter) || Static.IsMatch(uri.AbsolutePath)) return;
            var contentType = e.Response.Headers.Contains("content-type") ? e.Response.Headers.GetHeader("content-type") : "";
            if (e.Request.Method == "GET" && !contentType.Contains("json")) return;

            var sensitive = Sensitive.IsMatch(uri.AbsolutePath);
            string? requestBody = null;
            if (!sensitive && e.Request.Content is { } stream)
            {
                using var reader = new StreamReader(stream);
                requestBody = Redact(Limit(await reader.ReadToEndAsync(), 20000));
            }

            string? responseBody = null;
            if (!sensitive)
            {
                try
                {
                    using var content = await e.Response.GetContentAsync();
                    if (content is not null)
                    {
                        using var reader = new StreamReader(content);
                        responseBody = Redact(Limit(await reader.ReadToEndAsync(), 20000));
                    }
                }
                catch (Exception) { /* contenu indisponible (redirection, annulation) */ }
            }

            var requestType = e.Request.Headers.Contains("content-type") ? e.Request.Headers.GetHeader("content-type") : null;
            var line = JsonSerializer.Serialize(new
            {
                time = DateTime.Now.ToString("HH:mm:ss"),
                method = e.Request.Method,
                url = sensitive ? uri.GetLeftPart(UriPartial.Path) : uri.ToString(),
                requestType,
                status = e.Response.StatusCode,
                contentType,
                request = requestBody,
                response = responseBody,
            });
            await File.AppendAllTextAsync(_path, line + "\n");
            Count++;
            Recorded?.Invoke($"{e.Request.Method} {uri.AbsolutePath} -> {e.Response.StatusCode}");
        }
        catch (Exception)
        {
            // Un enregistrement raté ne doit jamais gêner la navigation
        }
    }

    private static string Limit(string s, int max) => s.Length > max ? s[..max] + "…" : s;
    private static string Redact(string s) => SecretField.Replace(s, "$1\"***\"");
}
