namespace NaverPropertyRanking_Blog.Models;

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string DownloadUrl,
    string Message);
