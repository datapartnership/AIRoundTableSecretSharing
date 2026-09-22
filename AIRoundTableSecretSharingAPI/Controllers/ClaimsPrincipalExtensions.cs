using System.Security.Claims;

namespace AIRoundTableSecretSharingAPI.Controllers;

internal static class ClaimsPrincipalExtensions
{
    // Azure AD OID claim name varies with MapInboundClaims setting
    internal static string? GetOid(this ClaimsPrincipal user) =>
        user.FindFirstValue("oid")
        ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

    internal static bool IsAdmin(this ClaimsPrincipal user, IConfiguration configuration) =>
        user.HasClaim("groups", configuration["AzureAd:AdminGroupId"]!);
}
