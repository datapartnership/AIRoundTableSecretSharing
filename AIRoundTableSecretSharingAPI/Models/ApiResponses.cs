using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Models;

public class MessageResponse
{
    public string Message { get; set; } = string.Empty;
}

public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
}

public class AddProducerResponse
{
    public string Message { get; set; } = string.Empty;
    public ProducerEpoch Epoch { get; set; } = null!;
}

public class KeyExchangeStatusResponse
{
    public bool IsComplete { get; set; }
    public int RegisteredCount { get; set; }
    public int ExpectedCount { get; set; }
    public List<string> RegisteredPartners { get; set; } = new();
    public List<string> MissingPartners { get; set; } = new();
}
