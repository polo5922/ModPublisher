using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace ModPublisher.Services;

public sealed class ApiException(string message, int status = 0, string? body = null) : Exception(message)
{
    public int Status { get; } = status;
    public string? Body { get; } = body;
}

/// <summary>
/// HttpClient commun. En mode simulation, toute requête qui écrit (POST, PATCH, PUT, DELETE) est seulement
/// journalisée et renvoie null.
/// </summary>
public sealed class ApiHttp
{
    public const string UserAgent = "ModPublisher/1.0 (GameAndDev)";

    private readonly HttpClient _client;
    public bool Simulation { get; set; }
    public event Action<string>? Log;

    public ApiHttp(HttpMessageHandler? handler = null)
    {
        _client = handler is null ? new HttpClient() : new HttpClient(handler);
        _client.Timeout = TimeSpan.FromMinutes(5);
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public void Write(string line) => Log?.Invoke(line);

    public async Task<JsonNode?> SendAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        if (Simulation && request.Method != HttpMethod.Get)
        {
            Log?.Invoke($"[simulation] {request.Method} {request.RequestUri}{await Describe(request.Content)}");
            return null;
        }

        using var response = await _client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = text.Length > 2000 ? text[..2000] + "…" : text;
            throw new ApiException($"{request.Method} {request.RequestUri} -> HTTP {(int)response.StatusCode}\n{detail}", (int)response.StatusCode, text);
        }
        if (request.Method != HttpMethod.Get) Log?.Invoke($"{request.Method} {request.RequestUri} -> {(int)response.StatusCode}");
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private static async Task<string> Describe(HttpContent? content)
    {
        switch (content)
        {
            case null:
                return "";
            case MultipartFormDataContent multi:
                var parts = new List<string>();
                foreach (var part in multi)
                {
                    var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                    parts.Add(part is ByteArrayContent and not StringContent
                        ? $"{name} = <{part.Headers.ContentLength ?? 0} bytes>"
                        : $"{name} = {Shorten(await part.ReadAsStringAsync())}");
                }
                return "\n  " + string.Join("\n  ", parts);
            case StringContent s:
                return "\n  " + Shorten(await s.ReadAsStringAsync());
            default:
                return $"\n  <{content.Headers.ContentLength ?? 0} bytes>";
        }
    }

    private static string Shorten(string s) => s.Length > 600 ? s[..600] + "…" : s;

    public static StringContent Json(JsonNode node) =>
        new(node.ToJsonString(), Encoding.UTF8, "application/json");

    public static MultipartFormDataContent Multipart(string jsonName, JsonNode json, string fileName, byte[] file, string fileField = "file")
    {
        var multi = new MultipartFormDataContent();
        multi.Add(Json(json), jsonName);
        var bytes = new ByteArrayContent(file);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("application/java-archive");
        multi.Add(bytes, fileField, fileName);
        return multi;
    }
}
