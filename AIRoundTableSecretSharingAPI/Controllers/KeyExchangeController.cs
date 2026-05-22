using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AIRoundTableSecretSharingAPI.Services;
using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Controllers;

/// <summary>
/// Handles ML-KEM public key registration and retrieval.
///
/// SECURITY NOTE: The aggregator only stores ML-KEM encapsulation (public) keys.
/// Decapsulation (private) keys never leave partners' systems.
/// The aggregator cannot recover any shared secret from the keys it stores.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class KeyExchangeController : ControllerBase
{
    private readonly KeyStore _keyStore;
    private readonly ILogger<KeyExchangeController> _logger;
    
    public KeyExchangeController(KeyStore keyStore, ILogger<KeyExchangeController> logger)
    {
        _keyStore = keyStore;
        _logger = logger;
    }
    
    /// <summary>
    /// Register a partner's public key for key exchange.
    /// This is called once when a partner joins the system.
    /// </summary>
    [HttpPost("register")]
    public IActionResult RegisterPublicKey([FromBody] RegisterKeyRequest request)
    {
        if (string.IsNullOrEmpty(request.ProducerId) || string.IsNullOrEmpty(request.PublicKeyBase64))
        {
            return BadRequest("ProducerId and PublicKeyBase64 are required");
        }
        
        // Validate the public key format — ML-KEM-768 encapsulation keys are exactly 1184 bytes
        try
        {
            var keyBytes = Convert.FromBase64String(request.PublicKeyBase64);
            if (keyBytes.Length != 1184)
            {
                return BadRequest($"Invalid public key length ({keyBytes.Length}); expected 1184 bytes for ML-KEM-768.");
            }
        }
        catch (FormatException)
        {
            return BadRequest("Invalid Base64 encoding for public key");
        }
        
        var partnerKey = new PartnerPublicKey
        {
            ProducerId = request.ProducerId,
            PublicKeyBase64 = request.PublicKeyBase64,
            RegisteredAt = DateTime.UtcNow
        };
        
        _keyStore.RegisterKey(partnerKey);
        
        _logger.LogInformation(
            "Registered public key for {ProducerId}. Key exchange possible with {Count} other partners.",
            request.ProducerId,
            _keyStore.GetAllKeys().Count - 1);
        
        return Ok(new { message = "Public key registered successfully" });
    }
    
    /// <summary>
    /// Get all partners' public keys for computing shared secrets.
    /// Each partner calls this to get other partners' keys.
    /// </summary>
    [HttpGet("keys")]
    public IActionResult GetAllPublicKeys([FromQuery] string? excludeProducerId = null)
    {
        var allKeys = _keyStore.GetAllKeys();
        
        // Optionally exclude the requesting partner's own key
        var keys = excludeProducerId != null
            ? allKeys.Where(k => k.ProducerId != excludeProducerId).ToList()
            : allKeys;
        
        _logger.LogInformation(
            "Returning {Count} public keys (excluding: {Excluded})",
            keys.Count,
            excludeProducerId ?? "none");
        
        return Ok(new KeyExchangeResponse
        {
            PartnerKeys = keys,
            TotalPartners = allKeys.Count
        });
    }
    
    /// <summary>
    /// Get a specific partner's public key.
    /// </summary>
    [HttpGet("keys/{producerId}")]
    public IActionResult GetPublicKey(string producerId)
    {
        var key = _keyStore.GetKey(producerId);
        
        if (key == null)
        {
            return NotFound($"No public key registered for {producerId}");
        }
        
        return Ok(key);
    }
    
    /// <summary>
    /// Check if all partners have registered their keys.
    /// Submissions should only start after key exchange is complete.
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetKeyExchangeStatus()
    {
        var registeredKeys = _keyStore.GetAllKeys();
        var registeredIds = registeredKeys.Select(k => k.ProducerId).ToHashSet();
        
        // Get expected partners from the data store
        // In a real system, this would check against the current epoch
        var expectedPartners = new[] { "partnerA", "partnerB", "partnerC" };
        var missingKeys = expectedPartners.Where(p => !registeredIds.Contains(p)).ToList();
        
        return Ok(new
        {
            isComplete = !missingKeys.Any(),
            registeredCount = registeredKeys.Count,
            expectedCount = expectedPartners.Length,
            registeredPartners = registeredIds.ToList(),
            missingPartners = missingKeys
        });
    }
}
