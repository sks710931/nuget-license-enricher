using NuGet.Versioning;
using NuGetLicenseEnricher.Models;

namespace NuGetLicenseEnricher.Services;

public static class NuGetPurlParser
{
    private const string Prefix = "pkg:nuget/";

    public static bool IsNuGetPurl(string? purl) =>
        purl?.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) is true;

    public static NuGetPurlParseStatus TryParse(string? purl, out PackageIdentity? package)
    {
        package = null;
        if (!IsNuGetPurl(purl))
        {
            return NuGetPurlParseStatus.NotNuGet;
        }

        string body = purl![Prefix.Length..];
        int suffixIndex = body.IndexOfAny(['?', '#']);
        if (suffixIndex >= 0)
        {
            body = body[..suffixIndex];
        }

        int atIndex = body.LastIndexOf('@');
        if (atIndex < 0 || atIndex == body.Length - 1)
        {
            return NuGetPurlParseStatus.MissingVersion;
        }

        if (atIndex == 0)
        {
            return NuGetPurlParseStatus.MissingPackageId;
        }

        string id;
        string version;
        try
        {
            id = Uri.UnescapeDataString(body[..atIndex]);
            version = Uri.UnescapeDataString(body[(atIndex + 1)..]);
        }
        catch (UriFormatException)
        {
            return NuGetPurlParseStatus.InvalidEncoding;
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return NuGetPurlParseStatus.MissingPackageId;
        }

        if (!NuGetVersion.TryParse(version, out NuGetVersion? parsedVersion))
        {
            return NuGetPurlParseStatus.InvalidVersion;
        }

        package = new PackageIdentity(id, version, parsedVersion.ToNormalizedString(), parsedVersion);
        return NuGetPurlParseStatus.Success;
    }
}
