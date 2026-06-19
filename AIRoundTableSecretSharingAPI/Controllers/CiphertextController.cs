using AIRoundTableSecretSharingAPI.Models;
using AIRoundTableSecretSharingAPI.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Controllers;

/// <summary>
/// Aggregator relay for ML-KEM ciphertexts.
///
/// SECURITY NOTE: The aggregator stores opaque blobs it cannot interpret.
/// Only the intended recipient — who holds the decapsulation key — can recover the shared secret.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class CiphertextController : ControllerBase
{
    private readonly ICiphertextRepository _ciphertextRepo;
    private readonly ILogger<CiphertextController> _logger;

    public CiphertextController(ICiphertextRepository ciphertextRepo, ILogger<CiphertextController> logger)
    {
        _ciphertextRepo = ciphertextRepo;
        _logger = logger;
    }

    /// <summary>
    /// Store a KEM ciphertext. Called by the alphabetically larger partner after encapsulation.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(MessageResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<MessageResponse>> StoreCiphertext([FromBody] StoreCiphertextRequest request)
    {
        if (string.IsNullOrEmpty(request.SenderId) ||
            string.IsNullOrEmpty(request.RecipientId) ||
            string.IsNullOrEmpty(request.CiphertextBase64))
        {
            return BadRequest("SenderId, RecipientId, and CiphertextBase64 are required.");
        }

        if (request.SenderId == request.RecipientId)
            return BadRequest("SenderId and RecipientId must differ.");

        try
        {
            var bytes = Convert.FromBase64String(request.CiphertextBase64);
            // ML-KEM-768 ciphertext is exactly 1088 bytes
            if (bytes.Length != 1088)
                return BadRequest($"Invalid ciphertext length ({bytes.Length}); expected 1088 bytes for ML-KEM-768.");
        }
        catch (FormatException)
        {
            return BadRequest("CiphertextBase64 is not valid Base64.");
        }

        var ct = new PartnerCiphertext
        {
            SenderId = request.SenderId,
            RecipientId = request.RecipientId,
            CiphertextBase64 = request.CiphertextBase64,
            StoredAt = DateTime.UtcNow
        };

        await _ciphertextRepo.StoreAsync(ct);

        _logger.LogInformation(
            "Stored ML-KEM ciphertext from {Sender} to {Recipient}.",
            request.SenderId, request.RecipientId);

        return Ok(new MessageResponse { Message = "Ciphertext stored." });
    }

    /// <summary>
    /// Retrieve all ciphertexts addressed to a given partner. Called by the smaller partner to decapsulate.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(CiphertextResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<CiphertextResponse>> GetCiphertexts([FromQuery] string recipientId)
    {
        if (string.IsNullOrEmpty(recipientId))
            return BadRequest("recipientId query parameter is required.");

        var ciphertexts = await _ciphertextRepo.GetForRecipientAsync(recipientId);

        _logger.LogInformation(
            "Returning {Count} ciphertext(s) for {Recipient}.",
            ciphertexts.Count, recipientId);

        return Ok(new CiphertextResponse { Ciphertexts = ciphertexts });
    }
}
