namespace NuGetLicenseEnricher.Models;

public enum LicenseResultKind
{
    NotFound,
    Expression,
    CustomFile,
    LegacyUrl
}

public sealed record LicenseResult(LicenseResultKind Kind, string? Value = null)
{
    public bool Found => Kind is not LicenseResultKind.NotFound;

    public string DisplayValue => Kind switch
    {
        LicenseResultKind.Expression => Value ?? "licence not found",
        LicenseResultKind.CustomFile => "Custom license",
        LicenseResultKind.LegacyUrl => Value ?? "License URL",
        _ => "licence not found"
    };

    public static LicenseResult NotFound { get; } = new(LicenseResultKind.NotFound);

    public static LicenseResult FromExpression(string expression) =>
        new(LicenseResultKind.Expression, expression);

    public static LicenseResult FromCustomFile() =>
        new(LicenseResultKind.CustomFile, "Custom license");

    public static LicenseResult FromLegacyUrl(string url) =>
        new(LicenseResultKind.LegacyUrl, url);
}
