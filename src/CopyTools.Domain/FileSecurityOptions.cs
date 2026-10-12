namespace CopyTools.Domain;

public sealed record FileSecurityOptions(
    bool PreserveNtfsPermissions = false);
