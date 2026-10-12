namespace CopyTools.Domain;

public sealed record UpdateManifest(
    int FormatVersion,
    string Version,
    string DownloadUrl,
    string Sha256,
    long Size,
    string ReleaseNotes = "");

public sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    bool IsUpdateAvailable,
    UpdateManifest Manifest);
