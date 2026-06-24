namespace AIRoundTableSecretSharingAPI.Services;

public interface IClientCredentialService
{
    Task<bool> ValidateClientAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default);
    Task ReplaceProducerCredentialsAsync(IEnumerable<(string ProducerId, string ClientSecret)> producerCredentials, CancellationToken cancellationToken = default);
    Task ResetToConfiguredCredentialsAsync(CancellationToken cancellationToken = default);
}
