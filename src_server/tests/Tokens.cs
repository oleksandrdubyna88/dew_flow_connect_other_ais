// mirrored from dew_flow_creds_for_devs src_minimalapi_server/tests/Tokens.cs @ 2026-09-05
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CoaiServer.Tests;

/// <summary>
/// Mints tokens for the server's <c>Local</c> HMAC scheme — the offline issuer the tests use.
/// Also mints the forged ones, because "a forged token is refused" is only proven by presenting one.
/// </summary>
internal static class Tokens
{
    private const string Issuer = "coai-local";

    public static string For(string email, string signingKey, string? name = null) =>
        WithClaims(
            signingKey,
            name is null
                ? [new Claim("email", email)]
                : [new Claim("email", email), new Claim("name", name)]);

    public static string WithClaims(string signingKey, Claim[] claims, DateTime? expires = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: Issuer,
            claims: claims,
            expires: expires ?? DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    /// <summary>An unsigned <c>alg=none</c> token — the classic JWT bypass attempt.</summary>
    public static string ForgedNoneAlg(string email)
    {
        static string B64(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = B64("""{"alg":"none","typ":"JWT"}""");
        var payload = B64($$"""{"iss":"{{Issuer}}","email":"{{email}}","exp":9999999999}""");

        return $"{header}.{payload}.";
    }
}
