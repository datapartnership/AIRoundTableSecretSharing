using AIRoundTableSecretSharingAPI.Models;
using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIRoundTableSecretSharingAPI.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IProducerRepository _producerRepo;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly IKeyRepository _keyRepo;
    private readonly ICiphertextRepository _ciphertextRepo;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IProducerRepository producerRepo,
        ISubmissionRepository submissionRepo,
        IKeyRepository keyRepo,
        ICiphertextRepository ciphertextRepo,
        ILogger<AdminController> logger)
    {
        _producerRepo = producerRepo;
        _submissionRepo = submissionRepo;
        _keyRepo = keyRepo;
        _ciphertextRepo = ciphertextRepo;
        _logger = logger;
    }

    /// <summary>
    /// Wipes all data and re-seeds the database to its initial state:
    /// partnerA, partnerB, partnerC with epoch 1 starting 2025-01-01.
    /// </summary>
    [HttpPost("reset")]
    [ProducesResponseType(typeof(MessageResponse), 200)]
    public async Task<ActionResult<MessageResponse>> ResetDatabase()
    {
        _logger.LogWarning("Admin database reset initiated by {User}.", User.Identity?.Name ?? "unknown");

        await _submissionRepo.ClearAllAsync();
        await _ciphertextRepo.ClearAsync();
        await _keyRepo.ClearAsync();
        await _producerRepo.ClearAllAsync();

        var startDate = new DateTime(2025, 1, 1);

        await _producerRepo.AddProducerAsync(new ProducerInfo { ProducerId = "partnerA", DisplayName = "Partner A", JoinedDate = startDate, IsActive = true });
        await _producerRepo.AddProducerAsync(new ProducerInfo { ProducerId = "partnerB", DisplayName = "Partner B", JoinedDate = startDate, IsActive = true });
        await _producerRepo.AddProducerAsync(new ProducerInfo { ProducerId = "partnerC", DisplayName = "Partner C", JoinedDate = startDate, IsActive = true });

        // CreateEpochAsync closes any open epoch first — since we just cleared, add directly
        var epoch = new ProducerEpoch
        {
            EpochId = 1,
            StartDate = startDate,
            EndDate = null,
            ProducerIds = new List<string> { "partnerA", "partnerB", "partnerC" },
            ProducerCount = 3
        };
        await _producerRepo.AddEpochAsync(epoch);

        _logger.LogInformation("Database reset complete. Re-seeded 3 producers and epoch 1.");

        return Ok(new MessageResponse { Message = "Database reset to initial state." });
    }
}
