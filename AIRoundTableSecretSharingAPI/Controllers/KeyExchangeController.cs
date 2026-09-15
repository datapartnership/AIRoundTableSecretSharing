using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AIRoundTableSecretSharingAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AIRoundTableSecretSharingAPI.Repositories;
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
[Authorize(Policy = "Partner")]
[Route("api/[controller]")]
public class KeyExchangeController : ControllerBase
{
    private readonly IKeyRepository _keyRepo;
    private readonly IProducerRepository _producerRepo;
    private readonly ICiphertextRepository _ciphertextRepo;
    private readonly ILogger<KeyExchangeController> _logger;

    public KeyExchangeController(
        IKeyRepository keyRepo,
        IProducerRepository producerRepo,
        ICiphertextRepository ciphertextRepo,
        ILogger<KeyExchangeController> logger)
    {
        _keyRepo = keyRepo;
        _producerRepo = producerRepo;
        _ciphertextRepo = ciphertextRepo;
        _logger = logger;
    }

    /// <summary>
    /// Register a partner's public key for key exchange.
    /// This is called once when a partner joins the system.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(MessageResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<MessageResponse>> RegisterPublicKey([FromBody] RegisterKeyRequest request)
    {
        // Identity comes from the token; body field is ignored
        var producerId = User.GetOid();
        if (string.IsNullOrWhiteSpace(producerId))
            return Unauthorized();
        if (request.EpochId <= 0 || string.IsNullOrWhiteSpace(request.DeviceId))
            return BadRequest("EpochId and DeviceId are required.");
        var epoch = await _producerRepo.GetEpochByIdAsync(request.EpochId);
        if (epoch == null || !epoch.ProducerIds.Contains(producerId))
            return Forbid();

        if (string.IsNullOrEmpty(request.PublicKeyBase64))
            return BadRequest("PublicKeyBase64 is required");

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

        var existing = await _keyRepo.GetKeyAsync(request.EpochId, producerId, request.DeviceId);
        if (existing != null && existing.PublicKeyBase64 != request.PublicKeyBase64)
        {
            return Conflict(new
            {
                error = "A different public key is already registered for this partner. Recreate the epoch to start a new key exchange."
            });
        }

        var partnerKey = new PartnerPublicKey
        {
            EpochId = request.EpochId,
            ProducerId = producerId,
            DeviceId = request.DeviceId,
            PublicKeyBase64 = request.PublicKeyBase64,
            RegisteredAt = DateTime.UtcNow
        };

        await _keyRepo.RegisterKeyAsync(partnerKey);

        var allKeys = await _keyRepo.GetAllKeysAsync(request.EpochId);
        _logger.LogInformation(
            "Registered public key for {ProducerId} in epoch {EpochId}. Key exchange possible with {Count} other partners.",
            producerId, request.EpochId,
            allKeys.Count - 1);

        return Ok(new MessageResponse { Message = "Public key registered successfully" });
    }

    /// <summary>
    /// Get all partners' public keys for computing shared secrets.
    /// Each partner calls this to get other partners' keys.
    /// </summary>
    [HttpGet("keys")]
    [ProducesResponseType(typeof(KeyExchangeResponse), 200)]
    public async Task<ActionResult<KeyExchangeResponse>> GetAllPublicKeys([FromQuery] int epochId, [FromQuery] string deviceId)
    {
        var callerId = User.GetOid();
        var epoch = await _producerRepo.GetEpochByIdAsync(epochId);
        if (callerId == null || epoch == null || !epoch.ProducerIds.Contains(callerId))
            return Forbid();
        var allKeys = await _keyRepo.GetAllKeysAsync(epochId);
        var epochIds = epoch.ProducerIds.ToHashSet();

        var keys = allKeys.Where(k => epochIds.Contains(k.ProducerId) && k.ProducerId != callerId);

        var partnerKeys = keys
            .GroupBy(k => k.ProducerId)
            .Select(g => g.OrderByDescending(k => k.RegisteredAt).First())
            .ToList();
        _logger.LogInformation(
            "Returning {Count} public keys for epoch {EpochId} (excluding: {Excluded})",
            partnerKeys.Count, epochId, callerId);

        return Ok(new KeyExchangeResponse
        {
            PartnerKeys = partnerKeys,
            TotalPartners = epochIds.Count
        });
    }

    /// <summary>
    /// Get a specific partner's public key.
    /// </summary>
    [HttpGet("keys/{producerId}")]
    [ProducesResponseType(typeof(PartnerPublicKey), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<PartnerPublicKey>> GetPublicKey(string producerId)
    {
        var epochId = int.TryParse(Request.Query["epochId"], out var parsedEpochId) ? parsedEpochId : 0;
        var deviceId = Request.Query["deviceId"].ToString();
        var key = await _keyRepo.GetKeyAsync(epochId, producerId, deviceId);

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
    [ProducesResponseType(typeof(KeyExchangeStatusResponse), 200)]
    public async Task<ActionResult<KeyExchangeStatusResponse>> GetKeyExchangeStatus([FromQuery] int epochId, [FromQuery] string deviceId)
    {
        var callerId = User.GetOid();
        if (callerId == null || string.IsNullOrWhiteSpace(deviceId))
            return Unauthorized();
        var epoch = await _producerRepo.GetEpochByIdAsync(epochId);
        if (epoch == null || !epoch.ProducerIds.Contains(callerId))
            return Forbid();
        var registeredKeys = await _keyRepo.GetAllKeysAsync(epochId);
        var registeredIds = registeredKeys.Select(k => k.ProducerId).ToHashSet();
        var myKey = callerId != null
            ? registeredKeys.FirstOrDefault(k => k.ProducerId == callerId && k.DeviceId == deviceId)
            : null;

        var expectedPartners = epoch?.ProducerIds ?? [];
        var epochRegistered = expectedPartners.Where(p => registeredIds.Contains(p)).ToList();
        var missingKeys = expectedPartners.Where(p => !registeredIds.Contains(p)).ToList();

        var n = expectedPartners.Count;
        var expectedCiphertexts = n * (n - 1) / 2;
        var actualCiphertexts = n > 0
            ? await _ciphertextRepo.CountForPartnersAsync(epochId, expectedPartners)
            : 0;
        var postedSenders = n > 0
            ? await _ciphertextRepo.GetSenderIdsForPartnersAsync(epochId, expectedPartners)
            : [];
        // Partners who must send at least one ciphertext: those who are NOT the alphabetical minimum
        var sortedPartners = expectedPartners.Order().ToList();
        var expectedSenders = sortedPartners.Skip(1).ToList(); // everyone except the smallest
        var missingSenders = expectedSenders.Except(postedSenders).ToList();

        return Ok(new KeyExchangeStatusResponse
        {
            IsComplete = expectedPartners.Count >= 2 && missingKeys.Count == 0,
            RegisteredCount = epochRegistered.Count,
            ExpectedCount = expectedPartners.Count,
            RegisteredPartners = epochRegistered,
            MissingPartners = missingKeys,
            ActualCiphertexts = actualCiphertexts,
            ExpectedCiphertexts = expectedCiphertexts,
            IsCiphertextExchangeComplete = expectedPartners.Count >= 2
                && actualCiphertexts >= expectedCiphertexts,
            MissingCiphertextSenders = missingSenders,
            MyPublicKeyBase64 = myKey?.PublicKeyBase64
        });
    }
}
