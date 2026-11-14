namespace GridShell.Core.Downloads;

/// <summary>Recognizes HTTPS Archive.org URLs for safely applying the user's Archive.org
/// site-session cookies. The suffix check intentionally requires a label boundary so lookalike
/// hosts such as archive.org.example.com never receive the session.</summary>
public static class ArchiveOrgSession
{
    public static bool IsArchiveOrgUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(uri.IdnHost, "archive.org", StringComparison.OrdinalIgnoreCase) ||
               uri.IdnHost.EndsWith(".archive.org", StringComparison.OrdinalIgnoreCase);
    }
}
