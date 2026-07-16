using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NuGetLicenseEnricher.Models;
using NuGetLicenseEnricher.Services;

namespace NuGetLicenseEnricher.Tests;

public sealed class NuGetLicenseServiceTests
{
    [Theory]
    [InlineData("MIT")]
    [InlineData("MIT OR Apache-2.0")]
    public async Task UsesRegistrationLicenseExpression(string expression)
    {
        var handler = new StubHttpMessageHandler((_, _) => JsonResponse(
            "{\"catalogEntry\":{\"version\":\"1.0.0\",\"licenseExpression\":\"" +
            expression +
            "\"}}"));
        using var service = CreateService(handler);

        LicenseResult result = await service.GetLicenseAsync(Package(), CancellationToken.None);

        Assert.Equal(LicenseResultKind.Expression, result.Kind);
        Assert.Equal(expression, result.Value);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task FollowsTrustedExactCatalogEntry()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.Contains("catalog0", StringComparison.Ordinal)
                ? JsonResponse("""
                    {"id":"Example","version":"1.0.0","licenseExpression":"MIT"}
                    """)
                : JsonResponse("""
                    {"catalogEntry":"https://api.nuget.org/v3/catalog0/data/example.json"}
                    """));
        using var service = CreateService(handler);

        LicenseResult result = await service.GetLicenseAsync(Package(), CancellationToken.None);

        Assert.Equal("MIT", result.Value);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task UsesEmbeddedNuspecLicenseExpression()
    {
        byte[] package = CreatePackage("<license type=\"expression\">BSD-3-Clause</license>");
        using var service = CreateService(PackageFallbackHandler(package));

        LicenseResult result = await service.GetLicenseAsync(Package(), CancellationToken.None);

        Assert.Equal(LicenseResultKind.Expression, result.Kind);
        Assert.Equal("BSD-3-Clause", result.Value);
    }

    [Fact]
    public async Task UsesEmbeddedCustomLicenseFile()
    {
        byte[] package = CreatePackage(
            "<license type=\"file\">licenses/LICENSE.txt</license>",
            ("licenses/LICENSE.txt", "Custom terms"));
        using var service = CreateService(PackageFallbackHandler(package));

        LicenseResult result = await service.GetLicenseAsync(Package(), CancellationToken.None);

        Assert.Equal(LicenseResultKind.CustomFile, result.Kind);
    }

    [Fact]
    public async Task IgnoresMissingDeclaredLicenseFile()
    {
        byte[] package = CreatePackage("<license type=\"file\">LICENSE.txt</license>");
        using var service = CreateService(PackageFallbackHandler(package));

        LicenseResult result = await service.GetLicenseAsync(Package(), CancellationToken.None);

        Assert.Equal(LicenseResultKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task FallsBackToLegacyLicenseUrlWithoutFollowingIt()
    {
        byte[] package = CreatePackage("<licenseUrl>https://example.test/license</licenseUrl>");
        StubHttpMessageHandler handler = PackageFallbackHandler(package);
        using var service = CreateService(handler);

        LicenseResult result = await service.GetLicenseAsync(Package(), CancellationToken.None);

        Assert.Equal(LicenseResultKind.LegacyUrl, result.Kind);
        Assert.Equal("https://example.test/license", result.Value);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Http404ReturnsLicenseNotFound()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.NotFound));
        using var service = CreateService(handler);

        LicenseResult result = await service.GetLicenseAsync(Package(), CancellationToken.None);

        Assert.Equal(LicenseResultKind.NotFound, result.Kind);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CachesDuplicatePackageLookups()
    {
        var handler = new StubHttpMessageHandler((_, _) => JsonResponse("""
            {"catalogEntry":{"version":"1.0.0","licenseExpression":"MIT"}}
            """));
        using var service = CreateService(handler);

        LicenseResult[] results = await Task.WhenAll(
            service.GetLicenseAsync(Package("Example"), CancellationToken.None),
            service.GetLicenseAsync(Package("example"), CancellationToken.None));

        Assert.All(results, result => Assert.Equal("MIT", result.Value));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RetriesTemporaryHttpFailure()
    {
        var handler = new StubHttpMessageHandler((_, call) =>
            call == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : JsonResponse("""
                    {"catalogEntry":{"version":"1.0.0","licenseExpression":"MIT"}}
                    """));
        using var service = CreateService(handler);

        LicenseResult result = await service.GetLicenseAsync(Package(), CancellationToken.None);

        Assert.Equal("MIT", result.Value);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task NuspecExpressionTakesPriorityOverLegacyRegistrationUrl()
    {
        byte[] package = CreatePackage("<license type=\"expression\">Apache-2.0</license>");
        var handler = new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.Contains("registration", StringComparison.Ordinal)
                ? JsonResponse("""
                    {"catalogEntry":{"version":"1.0.0","licenseUrl":"https://example.test/legacy"}}
                    """)
                : PackageResponse(package));
        using var service = CreateService(handler);

        LicenseResult result = await service.GetLicenseAsync(Package(), CancellationToken.None);

        Assert.Equal(LicenseResultKind.Expression, result.Kind);
        Assert.Equal("Apache-2.0", result.Value);
    }

    [Fact]
    public async Task DoesNotTreatTraversalEntryAsEmbeddedLicense()
    {
        byte[] package = CreatePackage(
            "<license type=\"file\">../LICENSE.txt</license>",
            ("../LICENSE.txt", "unsafe"));
        using var service = CreateService(PackageFallbackHandler(package));

        LicenseResult result = await service.GetLicenseAsync(Package(), CancellationToken.None);

        Assert.Equal(LicenseResultKind.NotFound, result.Kind);
    }

    private static NuGetLicenseService CreateService(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) }, new NuGetPackageInspector());

    private static PackageIdentity Package(string id = "Example")
    {
        NuGetPurlParser.TryParse($"pkg:nuget/{id}@1.0.0", out PackageIdentity? package);
        return package!;
    }

    private static StubHttpMessageHandler PackageFallbackHandler(byte[] package) =>
        new((request, _) =>
            request.RequestUri!.AbsolutePath.Contains("registration", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : PackageResponse(package));

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage PackageResponse(byte[] package)
    {
        var content = new ByteArrayContent(package);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static byte[] CreatePackage(
        string licenseElement,
        params (string Path, string Content)[] extraEntries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry nuspec = archive.CreateEntry("Example.nuspec");
            using (var writer = new StreamWriter(nuspec.Open(), Encoding.UTF8, leaveOpen: false))
            {
                writer.Write($$"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                      <metadata>
                        <id>Example</id><version>1.0.0</version><authors>Test</authors><description>Test</description>
                        {{licenseElement}}
                      </metadata>
                    </package>
                    """);
            }

            foreach ((string path, string content) in extraEntries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
                writer.Write(content);
            }
        }

        return output.ToArray();
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _callCount);
            return Task.FromResult(responseFactory(request, call));
        }
    }
}
