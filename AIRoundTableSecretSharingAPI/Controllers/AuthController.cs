using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace AIRoundTableSecretSharingAPI.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IConfiguration config, ILogger<AuthController> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// OAuth 2.0 Client Credentials token endpoint.
    /// POST /auth/token with grant_type=client_credentials, client_id, client_secret.
    /// </summary>
    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    public IActionResult Token(
        [FromForm(Name = "grant_type")] string grantType,
        [FromForm(Name = "client_id")] string clientId,
        [FromForm(Name = "client_secret")] string clientSecret)
    {
        if (grantType != "client_credentials")
            return BadRequest(new { error = "unsupported_grant_type" });

        var clients = _config.GetSection("ClientCredentials").Get<Dictionary<string, string>>();

        if (clients == null || !clients.TryGetValue(clientId, out var expectedSecret)
            || expectedSecret != clientSecret)
        {
            _logger.LogWarning("Failed token request for client_id={ClientId} from {RemoteIp}",
                clientId, HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "invalid_client" });
        }

        var expiryMinutes = _config.GetValue<int>("Jwt:ExpiryMinutes", 60);
        var token = GenerateToken(clientId, expiryMinutes);

        _logger.LogInformation("Issued token for client_id={ClientId}", clientId);

        return Ok(new
        {
            access_token = token,
            token_type = "Bearer",
            expires_in = expiryMinutes * 60
        });
    }

    private string GenerateToken(string clientId, int expiryMinutes)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, clientId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

