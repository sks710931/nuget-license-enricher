using System.Text.Json.Nodes;
using NuGetLicenseEnricher.Services;

namespace NuGetLicenseEnricher.Tests;

public sealed class CycloneDxValidatorTests
{
    [Fact]
    public void AcceptsCycloneDx16AndPreservesExtensions()
    {
        JsonNode root = JsonNode.Parse("""
            {"bomFormat":"CycloneDX","specVersion":"1.6","x-extension":true}
            """)!;

        bool valid = CycloneDxValidator.TryValidate(root, out JsonObject? bom, out string? error);

        Assert.True(valid, error);
        Assert.True(bom!["x-extension"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("{\"bomFormat\":\"SPDX\",\"specVersion\":\"1.6\"}")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.5\"}")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.6\",\"components\":{}}")]
    public void RejectsInvalidCycloneDx16Envelope(string json)
    {
        bool valid = CycloneDxValidator.TryValidate(
            JsonNode.Parse(json),
            out _,
            out string? error);

        Assert.False(valid);
        Assert.NotNull(error);
    }
}
