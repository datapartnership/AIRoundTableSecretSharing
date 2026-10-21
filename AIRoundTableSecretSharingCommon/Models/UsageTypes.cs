namespace AIRoundTableSecretSharingCommon.Models;

/// <summary>Submission streams. Each has its own epochs and Quorum Check periods.</summary>
public static class UsageTypes
{
    public const string Api = "api";
    public const string Chat = "chat";

    public static readonly string[] All = [Api, Chat];

    public static bool IsValid(string? usageType) => usageType is Api or Chat;
}
