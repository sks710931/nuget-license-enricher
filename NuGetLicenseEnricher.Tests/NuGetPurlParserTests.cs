using NuGetLicenseEnricher.Models;
using NuGetLicenseEnricher.Services;

namespace NuGetLicenseEnricher.Tests;

public sealed class NuGetPurlParserTests
{
    [Fact]
    public void ExtractsPackageIdAndExactVersion()
    {
        NuGetPurlParseStatus status = NuGetPurlParser.TryParse(
            "pkg:nuget/Newtonsoft.Json@13.0.3",
            out PackageIdentity? package);

        Assert.Equal(NuGetPurlParseStatus.Success, status);
        Assert.NotNull(package);
        Assert.Equal("Newtonsoft.Json", package.Id);
        Assert.Equal("13.0.3", package.Version);
        Assert.Equal("13.0.3", package.NormalizedVersion);
    }

    [Fact]
    public void DecodesPackageIdAndVersion()
    {
        NuGetPurlParseStatus status = NuGetPurlParser.TryParse(
            "pkg:nuget/Contoso%2EPackage@1.2.3%2Dbeta.1",
            out PackageIdentity? package);

        Assert.Equal(NuGetPurlParseStatus.Success, status);
        Assert.Equal("Contoso.Package", package!.Id);
        Assert.Equal("1.2.3-beta.1", package.Version);
    }

    [Fact]
    public void IgnoresQualifiersAndFragmentAfterExactVersion()
    {
        NuGetPurlParseStatus status = NuGetPurlParser.TryParse(
            "pkg:nuget/Example@2.0.0?repository_url=x#runtime",
            out PackageIdentity? package);

        Assert.Equal(NuGetPurlParseStatus.Success, status);
        Assert.Equal("2.0.0", package!.Version);
    }

    [Fact]
    public void RejectsNonNuGetPurl()
    {
        NuGetPurlParseStatus status = NuGetPurlParser.TryParse(
            "pkg:npm/example@1.0.0",
            out PackageIdentity? package);

        Assert.Equal(NuGetPurlParseStatus.NotNuGet, status);
        Assert.Null(package);
    }

    [Fact]
    public void ReportsMissingVersion()
    {
        NuGetPurlParseStatus status = NuGetPurlParser.TryParse(
            "pkg:nuget/Example",
            out PackageIdentity? package);

        Assert.Equal(NuGetPurlParseStatus.MissingVersion, status);
        Assert.Null(package);
    }
}
