using AIRoundTableSecretSharingAPI.Data;
using AIRoundTableSecretSharingAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace AIRoundTableSecretSharingAPI.Services;

public class DbClientCredentialService : IClientCredentialService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly string _adminUser;

    public DbClientCredentialService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
        _adminUser = _config["AdminUser"] ?? "admin";
    }

    public async Task<bool> ValidateClientAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default)
    {
        var record = await _db.ClientCredentials.FindAsync(new object[] { clientId }, cancellationToken);
        return record != null && string.Equals(record.ClientSecret, clientSecret, StringComparison.Ordinal);
    }

    public async Task ReplaceProducerCredentialsAsync(
        IEnumerable<(string ProducerId, string ClientSecret)> producerCredentials,
        CancellationToken cancellationToken = default)
    {
        var nonAdminCredentials = await _db.ClientCredentials
            .Where(c => c.ClientId != _adminUser)
            .ToListAsync(cancellationToken);

        _db.ClientCredentials.RemoveRange(nonAdminCredentials);

        foreach (var (producerId, clientSecret) in producerCredentials)
        {
            _db.ClientCredentials.Add(new ClientCredential
            {
                ClientId = producerId,
                ClientSecret = clientSecret
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetToConfiguredCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var configured = _config.GetSection("ClientCredentials").Get<Dictionary<string, string>>()
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        var existing = await _db.ClientCredentials.ToListAsync(cancellationToken);
        _db.ClientCredentials.RemoveRange(existing);

        foreach (var pair in configured)
        {
            _db.ClientCredentials.Add(new ClientCredential
            {
                ClientId = pair.Key,
                ClientSecret = pair.Value
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
