using NuGet.Versioning;

namespace NuGetLicenseEnricher.Models;

public sealed record PackageIdentity(
    string Id,
    string Version,
    string NormalizedVersion,
    NuGetVersion ParsedVersion);

public enum NuGetPurlParseStatus
{
    NotNuGet,
    MissingPackageId,
    MissingVersion,
    InvalidEncoding,
    InvalidVersion,
    IgnoredOperatingSystem,
    Success
}
