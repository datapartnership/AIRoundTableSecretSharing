using AIRoundTableSecretSharingAPI.Services;
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
    private readonly ISubmissionRepository _submissionRepo;
    private readonly IQuorumRepository _quorumRepo;
    private readonly ILogger<KeyExchangeController> _logger;

    public KeyExchangeController(
        IKeyRepository keyRepo,
        IProducerRepository producerRepo,
        ICiphertextRepository ciphertextRepo,
        ISubmissionRepository submissionRepo,
        IQuorumRepository quorumRepo,
        ILogger<KeyExchangeController> logger)
    {
        _keyRepo = keyRepo;
        _producerRepo = producerRepo;
        _ciphertextRepo = ciphertextRepo;
        _submissionRepo = submissionRepo;
        _quorumRepo = quorumRepo;
        _logger = logger;
    }

    private Task<KeyScope?> ResolveScopeAsync(int? epochId, int? periodId) =>
        KeyScope.ResolveAsync(epochId, periodId, _producerRepo, _quorumRepo);

    private static string? ValidatePublicKey(string? publicKeyBase64)
    {
        if (string.IsNullOrEmpty(publicKeyBase64))
            return "PublicKeyBase64 is required";

        // ML-KEM-768 encapsulation keys are exactly 1184 bytes
        try
        {
            var keyBytes = Convert.FromBase64String(publicKeyBase64);
            if (keyBytes.Length != 1184)
                return $"Invalid public key length ({keyBytes.Length}); expected 1184 bytes for ML-KEM-768.";
        }
        catch (FormatException)
        {
            return "Invalid Base64 encoding for public key";
        }
        return null;
    }

    /// <summary>
    /// Register a partner's public key for key exchange.
    /// This is called once when a partner joins the system.
    /// </summary>
    [HttpPost("register")]
    [Authorize(Policy = "Participant")]
    [ProducesResponseType(typeof(MessageResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<MessageResponse>> RegisterPublicKey([FromBody] RegisterKeyRequest request)
    {
        // Identity comes from the token; body field is ignored
        var producerId = User.GetOid();
        if (string.IsNullOrWhiteSpace(producerId))
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.DeviceId))
            return BadRequest("DeviceId is required.");
        var scope = await ResolveScopeAsync(request.EpochId, request.PeriodId);
        if (scope == null)
            return BadRequest("Exactly one of EpochId and PeriodId is required.");
        if (!scope.ProducerIds.Contains(producerId))
            return Forbid();
        if (scope.CancelledError is { } cancelled)
            return Conflict(cancelled);

        var keyError = ValidatePublicKey(request.PublicKeyBase64);
        if (keyError != null)
            return BadRequest(keyError);

        var existing = await _keyRepo.GetKeyAsync(scope.Id, producerId, request.DeviceId);
        if (existing != null && existing.PublicKeyBase64 != request.PublicKeyBase64)
        {
            return Conflict(new
            {
                error = $"A different public key is already registered for this partner. Contact {EpochLifecycle.SupportEmail} to ask an admin to recreate the epoch."
            });
        }

        var partnerKey = new PartnerPublicKey
        {
            EpochId = scope.Id,
            ProducerId = producerId,
            DeviceId = request.DeviceId,
            PublicKeyBase64 = request.PublicKeyBase64,
            RegisteredAt = DateTime.UtcNow
        };

        await _keyRepo.RegisterKeyAsync(partnerKey);

        var allKeys = await _keyRepo.GetAllKeysAsync(scope.Id);
        _logger.LogInformation(
            "Registered public key for {ProducerId} in {Scope}. Key exchange possible with {Count} other partners.",
            producerId, scope.Label,
            allKeys.Count - 1);

        return Ok(new MessageResponse { Message = "Public key registered successfully" });
    }

    /// <summary>
    /// Replace the caller's key for an epoch or period (on any device) with a new one.
    /// Only allowed while no ciphertext, submission or Quorum Check answer depends on the old key.
    /// </summary>
    [HttpPost("rotate")]
    [Authorize(Policy = "Participant")]
    [ProducesResponseType(typeof(MessageResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<MessageResponse>> RotatePublicKey([FromBody] RegisterKeyRequest request)
    {
        var producerId = User.GetOid();
        if (string.IsNullOrWhiteSpace(producerId))
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.DeviceId))
            return BadRequest("DeviceId is required.");
        var scope = await ResolveScopeAsync(request.EpochId, request.PeriodId);
        if (scope == null)
            return BadRequest("Exactly one of EpochId and PeriodId is required.");
        if (!scope.ProducerIds.Contains(producerId))
            return Forbid();
        if (scope.CancelledError is { } cancelled)
            return Conflict(cancelled);

        var keyError = ValidatePublicKey(request.PublicKeyBase64);
        if (keyError != null)
            return BadRequest(keyError);

        var hasCiphertexts = await _ciphertextRepo.AnyInvolvingAsync(scope.Id, producerId);
        var hasMaskedData = !hasCiphertexts && (request.PeriodId is > 0
            ? (await _quorumRepo.GetAnswersAsync(request.PeriodId.Value)).Count > 0
            : (await _submissionRepo.GetSubmissionsByEpochAsync(scope.Id)).Count > 0);
        if (hasCiphertexts || hasMaskedData)
        {
            return Conflict(new
            {
                error = $"The key exchange has already started, so your key can't be replaced. Contact {EpochLifecycle.SupportEmail} to ask an admin to recreate the epoch.",
                code = "exchange-started"
            });
        }

        await _keyRepo.ReplaceProducerKeyAsync(new PartnerPublicKey
        {
            EpochId = scope.Id,
            ProducerId = producerId,
            DeviceId = request.DeviceId,
            PublicKeyBase64 = request.PublicKeyBase64,
            RegisteredAt = DateTime.UtcNow
        });

        _logger.LogWarning(
            "Rotated public key for {ProducerId} in {Scope} (device {DeviceId}).",
            producerId, scope.Label, request.DeviceId);

        return Ok(new MessageResponse { Message = "Public key replaced successfully" });
    }

    /// <summary>
    /// Get all partners' public keys for computing shared secrets.
    /// Each partner calls this to get other partners' keys.
    /// </summary>
    [HttpGet("keys")]
    [ProducesResponseType(typeof(KeyExchangeResponse), 200)]
    public async Task<ActionResult<KeyExchangeResponse>> GetAllPublicKeys(
        [FromQuery] int? epochId, [FromQuery] int? periodId, [FromQuery] string deviceId)
    {
        var callerId = User.GetOid();
        var scope = await ResolveScopeAsync(epochId, periodId);
        if (callerId == null || scope == null || !scope.ProducerIds.Contains(callerId))
            return Forbid();
        var allKeys = await _keyRepo.GetAllKeysAsync(scope.Id);
        var epochIds = scope.ProducerIds.ToHashSet();

        var keys = allKeys.Where(k => epochIds.Contains(k.ProducerId) && k.ProducerId != callerId);

        var partnerKeys = keys
            .GroupBy(k => k.ProducerId)
            .Select(g => g.OrderByDescending(k => k.RegisteredAt).First())
            .ToList();
        _logger.LogInformation(
            "Returning {Count} public keys for {Scope} (excluding: {Excluded})",
            partnerKeys.Count, scope.Label, callerId);

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
    public async Task<ActionResult<KeyExchangeStatusResponse>> GetKeyExchangeStatus(
        [FromQuery] int? epochId, [FromQuery] int? periodId, [FromQuery] string deviceId)
    {
        var callerId = User.GetOid();
        if (callerId == null || string.IsNullOrWhiteSpace(deviceId))
            return Unauthorized();
        var scope = await ResolveScopeAsync(epochId, periodId);
        if (scope == null || !scope.ProducerIds.Contains(callerId))
            return Forbid();
        var scopeId = scope.Id;
        var registeredKeys = await _keyRepo.GetAllKeysAsync(scopeId);
        var registeredIds = registeredKeys.Select(k => k.ProducerId).ToHashSet();
        var myKey = callerId != null
            ? registeredKeys.FirstOrDefault(k => k.ProducerId == callerId && k.DeviceId == deviceId)
            : null;

        var expectedPartners = scope.ProducerIds;
        var epochRegistered = expectedPartners.Where(p => registeredIds.Contains(p)).ToList();
        var missingKeys = expectedPartners.Where(p => !registeredIds.Contains(p)).ToList();

        var n = expectedPartners.Count;
        var expectedCiphertexts = n * (n - 1) / 2;
        var actualCiphertexts = n > 0
            ? await _ciphertextRepo.CountForPartnersAsync(scopeId, expectedPartners)
            : 0;
        var postedSenders = n > 0
            ? await _ciphertextRepo.GetSenderIdsForPartnersAsync(scopeId, expectedPartners)
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
