using System.Collections.Generic;
using System.Linq;
using Microsoft.Web.WebView2.Core;

namespace GridShell.Core.Downloads;

/// <summary>Utilities for validating and exporting Archive.org cookies harvested from a
/// temporary WebView2 login flow.</summary>
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

    public static string? ToHeaderValue(ICoreWebView2CookieList? cookieList)
    {
        if (cookieList is null)
        {
            return null;
        }

        var values = new List<string>();
        for (var i = 0; i < cookieList.GetCount(); i++)
        {
            var cookie = cookieList.GetCookie(i);
            if (cookie is null || !IsArchiveOrgDomain(cookie.Domain))
            {
                continue;
            }

            values.Add($"{cookie.Name}={cookie.Value}");
        }

        return values.Count == 0 ? null : string.Join("; ", values.Distinct(StringComparer.Ordinal));
    }

    public static bool IsArchiveOrgDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }

        var host = domain.TrimStart('.');
        return string.Equals(host, "archive.org", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".archive.org", StringComparison.OrdinalIgnoreCase);
    }
}
