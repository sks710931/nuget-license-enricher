using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using NuGet.Versioning;
using NuGetLicenseEnricher.Models;

namespace NuGetLicenseEnricher.Services;

public sealed class NuGetLicenseService : INuGetLicenseService, IDisposable
{
    private const string RegistrationBaseUrl =
        "https://api.nuget.org/v3/registration5-gz-semver2";
    private const string FlatContainerBaseUrl =
        "https://api.nuget.org/v3-flatcontainer";
    private const int MaximumRetries = 3;
    private const long MaximumJsonBytes = 10 * 1024 * 1024;
    private const long MaximumPackageBytes = 100 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly NuGetPackageInspector _packageInspector;
    private readonly SemaphoreSlim _lookupGate;
    private readonly ConcurrentDictionary<string, Lazy<Task<LicenseResult>>> _cache =
        new(StringComparer.Ordinal);

    public NuGetLicenseService(
        HttpClient httpClient,
        NuGetPackageInspector packageInspector,
        int maximumConcurrency = 5)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(packageInspector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumConcurrency);

        _httpClient = httpClient;
        _packageInspector = packageInspector;
        _lookupGate = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
    }

    public Task<LicenseResult> GetLicenseAsync(
        PackageIdentity package,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);

        string cacheKey = string.Concat(
            package.Id.ToLowerInvariant(),
            "\n",
            package.NormalizedVersion.ToLowerInvariant());

        Lazy<Task<LicenseResult>> lookup = _cache.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<LicenseResult>>(
                () => LookupWithConcurrencyLimitAsync(package, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lookup.Value;
    }

    private async Task<LicenseResult> LookupWithConcurrencyLimitAsync(
        PackageIdentity package,
        CancellationToken cancellationToken)
    {
        await _lookupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LookupAsync(package, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lookupGate.Release();
        }
    }

    private async Task<LicenseResult> LookupAsync(
        PackageIdentity package,
        CancellationToken cancellationToken)
    {
        RegistrationMetadata? registration =
            await GetRegistrationMetadataAsync(package, cancellationToken).ConfigureAwait(false);

        if (registration?.LicenseExpression is { } registrationExpression)
        {
            return LicenseResult.FromExpression(registrationExpression);
        }

        NuGetPackageInspection? inspection =
            await InspectPackageAsync(package, cancellationToken).ConfigureAwait(false);

        if (inspection?.LicenseExpression is { } nuspecExpression)
        {
            return LicenseResult.FromExpression(nuspecExpression);
        }

        if (inspection?.HasEmbeddedLicenseFile is true)
        {
            return LicenseResult.FromCustomFile();
        }

        string? legacyUrl = inspection?.LicenseUrl ?? registration?.LicenseUrl;
        if (IsUsableLicenseUrl(legacyUrl))
        {
            return LicenseResult.FromLegacyUrl(legacyUrl!);
        }

        return LicenseResult.NotFound;
    }

    private async Task<RegistrationMetadata?> GetRegistrationMetadataAsync(
        PackageIdentity package,
        CancellationToken cancellationToken)
    {
        string id = Uri.EscapeDataString(package.Id.ToLowerInvariant());
        string version = Uri.EscapeDataString(package.NormalizedVersion.ToLowerInvariant());
        string url = $"{RegistrationBaseUrl}/{id}/{version}.json";

        using HttpResponseMessage response =
            await GetWithRetriesAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using JsonDocument document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        JsonElement root = document.RootElement;

        if (root.TryGetProperty("catalogEntry", out JsonElement catalogEntry))
        {
            if (catalogEntry.ValueKind == JsonValueKind.Object)
            {
                return ParseRegistrationMetadata(catalogEntry, package.ParsedVersion);
            }

            if (catalogEntry.ValueKind == JsonValueKind.String &&
                TryGetTrustedCatalogUrl(catalogEntry.GetString(), out Uri? catalogUri))
            {
                using HttpResponseMessage catalogResponse =
                    await GetWithRetriesAsync(catalogUri!.AbsoluteUri, cancellationToken).ConfigureAwait(false);
                if (catalogResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                catalogResponse.EnsureSuccessStatusCode();
                using JsonDocument catalogDocument =
                    await ReadJsonAsync(catalogResponse, cancellationToken).ConfigureAwait(false);
                return ParseRegistrationMetadata(catalogDocument.RootElement, package.ParsedVersion);
            }
        }

        return ParseRegistrationMetadata(root, package.ParsedVersion);
    }

    private async Task<NuGetPackageInspection?> InspectPackageAsync(
        PackageIdentity package,
        CancellationToken cancellationToken)
    {
        string id = Uri.EscapeDataString(package.Id.ToLowerInvariant());
        string version = Uri.EscapeDataString(package.NormalizedVersion.ToLowerInvariant());
        string url = $"{FlatContainerBaseUrl}/{id}/{version}/{id}.{version}.nupkg";

        using HttpResponseMessage response =
            await GetWithRetriesAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var packageStream = await ReadBoundedAsync(
            response,
            MaximumPackageBytes,
            cancellationToken).ConfigureAwait(false);
        return _packageInspector.Inspect(packageStream);
    }

    private async Task<HttpResponseMessage> GetWithRetriesAsync(
        string url,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                if (!IsTemporary(response.StatusCode) || attempt >= MaximumRetries)
                {
                    return response;
                }

                response.Dispose();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                                                     attempt < MaximumRetries)
            {
                // HttpClient timeout. Retry below.
            }
            catch (HttpRequestException) when (attempt < MaximumRetries)
            {
                // Temporary network failure. Retry below.
            }

            TimeSpan delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt));
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using MemoryStream stream = await ReadBoundedAsync(
            response,
            MaximumJsonBytes,
            cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<MemoryStream> ReadBoundedAsync(
        HttpResponseMessage response,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 0 and var contentLength &&
            contentLength > maximumBytes)
        {
            throw new InvalidDataException($"Response exceeded the {maximumBytes}-byte safety limit.");
        }

        await using Stream source = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var destination = new MemoryStream();
        byte[] buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                destination.Position = 0;
                return destination;
            }

            total += read;
            if (total > maximumBytes)
            {
                destination.Dispose();
                throw new InvalidDataException($"Response exceeded the {maximumBytes}-byte safety limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static RegistrationMetadata ParseRegistrationMetadata(
        JsonElement element,
        NuGetVersion requestedVersion)
    {
        if (TryGetString(element, "version") is { } returnedVersion &&
            NuGetVersion.TryParse(returnedVersion, out NuGetVersion? parsedReturnedVersion) &&
            parsedReturnedVersion != requestedVersion)
        {
            throw new InvalidDataException(
                $"NuGet returned version '{returnedVersion}' for exact version '{requestedVersion}'.");
        }

        return new RegistrationMetadata(
            Normalize(TryGetString(element, "licenseExpression")),
            Normalize(TryGetString(element, "licenseUrl")));
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool TryGetTrustedCatalogUrl(string? value, out Uri? catalogUri)
    {
        bool valid = Uri.TryCreate(value, UriKind.Absolute, out catalogUri) &&
                     catalogUri.Scheme == Uri.UriSchemeHttps &&
                     catalogUri.Host.Equals("api.nuget.org", StringComparison.OrdinalIgnoreCase);
        if (!valid)
        {
            catalogUri = null;
        }

        return valid;
    }

    private static bool IsUsableLicenseUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme is "http" or "https";

    private static bool IsTemporary(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose() => _lookupGate.Dispose();

    private sealed record RegistrationMetadata(string? LicenseExpression, string? LicenseUrl);
}
