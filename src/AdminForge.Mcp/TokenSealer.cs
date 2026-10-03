using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace AdminForge.Mcp;

internal sealed record ClientRegistration(string Name, string[] RedirectUris);

internal sealed record AuthCode(
    string Cid,
    string ClientId,
    string RedirectUri,
    string Challenge,
    DateTime Exp
);

internal sealed record AccessToken(string Cid, DateTime Exp);

internal sealed record RefreshToken(string Cid, int Counter);

/// <summary>Opaque tokens: JSON sealed by Data Protection, so there is no key to configure and nothing to store.</summary>
internal sealed class TokenSealer(IDataProtectionProvider provider)
{
    private const string Purpose = "AdminForge.Mcp";

    public string Seal<T>(T payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return WebEncoders.Base64UrlEncode(
            provider.CreateProtector(Purpose, typeof(T).Name).Protect(bytes)
        );
    }

    public T? Unseal<T>(string? token)
        where T : class
    {
        if (string.IsNullOrEmpty(token))
            return null;
        try
        {
            var bytes = provider
                .CreateProtector(Purpose, typeof(T).Name)
                .Unprotect(WebEncoders.Base64UrlDecode(token));
            return JsonSerializer.Deserialize<T>(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    public static string Challenge(string verifier) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}
