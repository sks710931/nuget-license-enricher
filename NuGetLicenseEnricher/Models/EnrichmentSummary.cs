namespace NuGetLicenseEnricher.Models;

public sealed record EnrichmentSummary(
    int TotalNuGetComponents,
    int AlreadyLicensed,
    int SuccessfullyEnriched,
    int LicenseNotFound,
    int Errors);
