using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ModPublisher.Services;

/// <summary>Jetons d'API, chiffrés avec DPAPI : seul ce compte Windows peut les relire.</summary>
public sealed class SecretStore
{
    public const string ModrinthToken = "modrinth_token";
    public const string CurseForgeUploadToken = "curseforge_token";

    private static readonly byte[] Entropy = "ModPublisher"u8.ToArray();
    private readonly string _path;
    private Dictionary<string, string> _values = new();

    public SecretStore(string? path = null)
    {
        _path = path ?? AppPaths.Secrets;
        if (!File.Exists(_path)) return;
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            _values = JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new();
        }
        catch (CryptographicException)
        {
            // Fichier d'un autre compte Windows ou abîmé : on repart de zéro
            _values = new();
        }
    }

    public string? Get(string key) => _values.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    public void Set(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) _values.Remove(key);
        else _values[key] = value.Trim();
        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_values));
        File.WriteAllBytes(_path, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
    }
}
