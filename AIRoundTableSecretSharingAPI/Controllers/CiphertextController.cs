using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AIRoundTableSecretSharingAPI.Services;
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
    private readonly CiphertextStore _store;
    private readonly ILogger<CiphertextController> _logger;

    public CiphertextController(CiphertextStore store, ILogger<CiphertextController> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Store a KEM ciphertext. Called by the alphabetically larger partner after encapsulation.
    /// </summary>
    [HttpPost]
    public IActionResult StoreCiphertext([FromBody] StoreCiphertextRequest request)
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

        _store.Store(ct);

        _logger.LogInformation(
            "Stored ML-KEM ciphertext from {Sender} to {Recipient}.",
            request.SenderId, request.RecipientId);

        return Ok(new { message = "Ciphertext stored." });
    }

    /// <summary>
    /// Retrieve all ciphertexts addressed to a given partner. Called by the smaller partner to decapsulate.
    /// </summary>
    [HttpGet]
    public IActionResult GetCiphertexts([FromQuery] string recipientId)
    {
        if (string.IsNullOrEmpty(recipientId))
            return BadRequest("recipientId query parameter is required.");

        var ciphertexts = _store.GetForRecipient(recipientId);

        _logger.LogInformation(
            "Returning {Count} ciphertext(s) for {Recipient}.",
            ciphertexts.Count, recipientId);

        return Ok(new CiphertextResponse { Ciphertexts = ciphertexts });
    }
}
