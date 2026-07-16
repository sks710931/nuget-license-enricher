using System.Text.Json.Nodes;
using NuGetLicenseEnricher.Models;
using NuGetLicenseEnricher.Services;

namespace NuGetLicenseEnricher.Tests;

public sealed class BomEnrichmentServiceTests
{
    [Fact]
    public async Task ComponentsWithoutPurlsAreIgnored()
    {
        JsonObject bom = ParseBom("""
            {"bomFormat":"CycloneDX","specVersion":"1.6","components":[
              {"type":"file","name":"library.dll","version":"1.0.0"}
            ]}
            """);
        var fake = new FakeLicenseService(LicenseResult.FromExpression("MIT"));

        EnrichmentSummary summary = await EnrichAsync(bom, fake);

        Assert.Equal(0, summary.TotalNuGetComponents);
        Assert.Equal(0, fake.CallCount);
        Assert.Null(bom["components"]![0]!["licenses"]);
    }

    [Fact]
    public async Task ComponentsWithoutVersionsAreSkipped()
    {
        JsonObject bom = ParseBom("""
            {"bomFormat":"CycloneDX","specVersion":"1.6","components":[
              {"type":"library","name":"Example","purl":"pkg:nuget/Example"}
            ]}
            """);
        var fake = new FakeLicenseService(LicenseResult.FromExpression("MIT"));
        using var progress = new StringWriter();

        EnrichmentSummary summary = await new BomEnrichmentService(fake)
            .EnrichAsync(bom, progress, CancellationToken.None);

        Assert.Equal(1, summary.TotalNuGetComponents);
        Assert.Equal(0, fake.CallCount);
        Assert.Contains("skipped: missing version", progress.ToString());
    }

    [Fact]
    public async Task OperatingSystemComponentsAreSkippedEvenWithNuGetPurl()
    {
        JsonObject bom = ParseBom("""
            {"bomFormat":"CycloneDX","specVersion":"1.6","components":[
              {"type":"operating-system","name":"Example OS","version":"1.0.0",
               "purl":"pkg:nuget/Example.OS@1.0.0"}
            ]}
            """);
        var fake = new FakeLicenseService(LicenseResult.FromExpression("MIT"));

        EnrichmentSummary summary = await EnrichAsync(bom, fake);

        Assert.Equal(1, summary.TotalNuGetComponents);
        Assert.Equal(0, fake.CallCount);
        Assert.Null(bom["components"]![0]!["licenses"]);
    }

    [Fact]
    public async Task ExistingLicensesAreNotChanged()
    {
        JsonObject bom = ParseBom("""
            {"bomFormat":"CycloneDX","specVersion":"1.6","components":[
              {"type":"library","name":"Example","version":"1.0.0",
               "purl":"pkg:nuget/Example@1.0.0","licenses":[{"expression":"BSD-3-Clause"}]}
            ]}
            """);
        var fake = new FakeLicenseService(LicenseResult.FromExpression("MIT"));

        EnrichmentSummary summary = await EnrichAsync(bom, fake);

        Assert.Equal(1, summary.AlreadyLicensed);
        Assert.Equal(0, fake.CallCount);
        Assert.Equal("BSD-3-Clause", bom["components"]![0]!["licenses"]![0]!["expression"]!.GetValue<string>());
    }

    [Fact]
    public async Task AddsCompoundLicenseExpression()
    {
        JsonObject bom = OneComponentBom();
        var fake = new FakeLicenseService(LicenseResult.FromExpression("MIT OR Apache-2.0"));

        EnrichmentSummary summary = await EnrichAsync(bom, fake);

        Assert.Equal(1, summary.SuccessfullyEnriched);
        Assert.Equal(
            "MIT OR Apache-2.0",
            bom["components"]![0]!["licenses"]![0]!["expression"]!.GetValue<string>());
    }

    [Fact]
    public async Task AddsNamedLicenseForEmbeddedCustomFile()
    {
        JsonObject bom = OneComponentBom();
        var fake = new FakeLicenseService(LicenseResult.FromCustomFile());

        await EnrichAsync(bom, fake);

        Assert.Equal(
            "Custom license",
            bom["components"]![0]!["licenses"]![0]!["license"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task MissingLicenseDoesNotModifyComponent()
    {
        JsonObject bom = OneComponentBom();
        var fake = new FakeLicenseService(LicenseResult.NotFound);

        EnrichmentSummary summary = await EnrichAsync(bom, fake);

        Assert.Equal(1, summary.LicenseNotFound);
        Assert.Null(bom["components"]![0]!["licenses"]);
    }

    [Fact]
    public async Task PreservesBomRefDependenciesAndUnknownProperties()
    {
        JsonObject bom = ParseBom("""
            {"bomFormat":"CycloneDX","specVersion":"1.6","serialNumber":"urn:uuid:00000000-0000-4000-8000-000000000001",
             "x-extension":{"keep":true},
             "components":[{"type":"library","bom-ref":"pkg-ref","name":"Example","version":"1.0.0",
               "purl":"pkg:nuget/Example@1.0.0","properties":[{"name":"x","value":"y"}]}],
             "dependencies":[{"ref":"pkg-ref","dependsOn":[]}]}
            """);
        JsonNode dependenciesBefore = bom["dependencies"]!.DeepClone();

        await EnrichAsync(bom, new FakeLicenseService(LicenseResult.FromExpression("MIT")));

        Assert.Equal("pkg-ref", bom["components"]![0]!["bom-ref"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(dependenciesBefore, bom["dependencies"]));
        Assert.True(bom["x-extension"]!["keep"]!.GetValue<bool>());
    }

    [Fact]
    public async Task DuplicateComponentsAreEnrichedWithoutCreatingMoreComponents()
    {
        JsonObject bom = ParseBom("""
            {"bomFormat":"CycloneDX","specVersion":"1.6","components":[
              {"type":"library","name":"Example","version":"1.0.0","purl":"pkg:nuget/Example@1.0.0"},
              {"type":"library","name":"Example","version":"1.0.0","purl":"pkg:nuget/Example@1.0.0"}
            ]}
            """);

        await EnrichAsync(bom, new FakeLicenseService(LicenseResult.FromExpression("MIT")));

        Assert.Equal(2, bom["components"]!.AsArray().Count);
        Assert.All(bom["components"]!.AsArray(), component =>
            Assert.Single(component!["licenses"]!.AsArray()));
    }

    [Fact]
    public async Task RunningTwiceDoesNotDuplicateLicenses()
    {
        JsonObject bom = OneComponentBom();
        var first = new FakeLicenseService(LicenseResult.FromExpression("MIT"));
        var second = new FakeLicenseService(LicenseResult.FromExpression("Apache-2.0"));

        await EnrichAsync(bom, first);
        EnrichmentSummary secondSummary = await EnrichAsync(bom, second);

        Assert.Equal(1, secondSummary.AlreadyLicensed);
        Assert.Equal(0, second.CallCount);
        Assert.Single(bom["components"]![0]!["licenses"]!.AsArray());
        Assert.Equal("MIT", bom["components"]![0]!["licenses"]![0]!["expression"]!.GetValue<string>());
    }

    private static async Task<EnrichmentSummary> EnrichAsync(
        JsonObject bom,
        INuGetLicenseService service)
    {
        using var progress = new StringWriter();
        return await new BomEnrichmentService(service)
            .EnrichAsync(bom, progress, CancellationToken.None);
    }

    private static JsonObject OneComponentBom() => ParseBom("""
        {"bomFormat":"CycloneDX","specVersion":"1.6","components":[
          {"type":"library","bom-ref":"pkg-ref","name":"Example","version":"1.0.0",
           "purl":"pkg:nuget/Example@1.0.0"}
        ]}
        """);

    private static JsonObject ParseBom(string json) =>
        JsonNode.Parse(json)!.AsObject();

    private sealed class FakeLicenseService(LicenseResult result) : INuGetLicenseService
    {
        public int CallCount { get; private set; }

        public Task<LicenseResult> GetLicenseAsync(
            PackageIdentity package,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }
}
