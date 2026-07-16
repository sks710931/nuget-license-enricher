using NuGetLicenseEnricher.Models;

namespace NuGetLicenseEnricher.Services;

public interface INuGetLicenseService
{
    Task<LicenseResult> GetLicenseAsync(
        PackageIdentity package,
        CancellationToken cancellationToken);
}
