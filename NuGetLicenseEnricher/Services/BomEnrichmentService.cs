using System.Text.Json.Nodes;
using NuGetLicenseEnricher.Models;

namespace NuGetLicenseEnricher.Services;

public sealed class BomEnrichmentService
{
    private readonly INuGetLicenseService _licenseService;

    public BomEnrichmentService(INuGetLicenseService licenseService)
    {
        ArgumentNullException.ThrowIfNull(licenseService);
        _licenseService = licenseService;
    }

    public async Task<EnrichmentSummary> EnrichAsync(
        JsonObject bom,
        TextWriter progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bom);
        ArgumentNullException.ThrowIfNull(progress);

        var workItems = GetWorkItems(bom);
        int alreadyLicensed = 0;
        int enriched = 0;
        int notFound = 0;
        int errors = 0;

        foreach (ComponentWorkItem item in workItems)
        {
            if (!item.AlreadyLicensed && item.ParseStatus == NuGetPurlParseStatus.Success)
            {
                item.LookupTask = _licenseService.GetLicenseAsync(item.Package!, cancellationToken);
            }
        }

        for (int index = 0; index < workItems.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ComponentWorkItem item = workItems[index];
            string prefix = $"[{index + 1}/{workItems.Count}] {item.DisplayName}";

            if (item.AlreadyLicensed)
            {
                alreadyLicensed++;
                await progress.WriteLineAsync($"{prefix} -> skipped: already licensed")
                    .ConfigureAwait(false);
                continue;
            }

            if (item.ParseStatus != NuGetPurlParseStatus.Success)
            {
                string reason = item.ParseStatus switch
                {
                    NuGetPurlParseStatus.MissingVersion => "missing version",
                    NuGetPurlParseStatus.MissingPackageId => "missing package ID",
                    NuGetPurlParseStatus.InvalidEncoding => "invalid PURL encoding",
                    NuGetPurlParseStatus.IgnoredOperatingSystem => "operating-system component",
                    _ => "invalid version"
                };
                await progress.WriteLineAsync($"{prefix} -> skipped: {reason}").ConfigureAwait(false);
                continue;
            }

            string packageLabel = $"{prefix} {item.Package!.Version}";
            try
            {
                LicenseResult result = await item.LookupTask!.ConfigureAwait(false);
                if (!result.Found)
                {
                    notFound++;
                    await progress.WriteLineAsync($"{packageLabel} -> licence not found")
                        .ConfigureAwait(false);
                    continue;
                }

                AddLicense(item.Component, result);
                enriched++;
                await progress.WriteLineAsync($"{packageLabel} -> {result.DisplayValue}")
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors++;
                await progress.WriteLineAsync($"{packageLabel} -> error: {ex.Message}")
                    .ConfigureAwait(false);
            }
        }

        return new EnrichmentSummary(workItems.Count, alreadyLicensed, enriched, notFound, errors);
    }

    private static List<ComponentWorkItem> GetWorkItems(JsonObject bom)
    {
        var result = new List<ComponentWorkItem>();
        if (bom["components"] is not JsonArray components)
        {
            return result;
        }

        foreach (JsonObject component in components.OfType<JsonObject>())
        {
            string? purl = GetString(component["purl"]);
            if (!NuGetPurlParser.IsNuGetPurl(purl))
            {
                continue;
            }

            NuGetPurlParseStatus status = NuGetPurlParser.TryParse(purl, out PackageIdentity? package);
            if (GetString(component["type"]) == "operating-system")
            {
                status = NuGetPurlParseStatus.IgnoredOperatingSystem;
            }
            string displayName = GetString(component["name"])
                ?? package?.Id
                ?? GetUnversionedPurlName(purl!);
            result.Add(new ComponentWorkItem(
                component,
                displayName,
                status,
                package,
                HasExistingLicense(component)));
        }

        return result;
    }

    private static bool HasExistingLicense(JsonObject component) =>
        component["licenses"] is JsonArray { Count: > 0 };

    private static void AddLicense(JsonObject component, LicenseResult result)
    {
        JsonObject licenseChoice = result.Kind switch
        {
            LicenseResultKind.Expression => new JsonObject
            {
                ["expression"] = result.Value
            },
            LicenseResultKind.CustomFile => new JsonObject
            {
                ["license"] = new JsonObject
                {
                    ["name"] = "Custom license"
                }
            },
            LicenseResultKind.LegacyUrl => new JsonObject
            {
                ["license"] = new JsonObject
                {
                    ["name"] = "License",
                    ["url"] = result.Value
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };

        component["licenses"] = new JsonArray(licenseChoice);
    }

    private static string GetUnversionedPurlName(string purl)
    {
        string value = purl["pkg:nuget/".Length..];
        int delimiter = value.IndexOfAny(['@', '?', '#']);
        if (delimiter >= 0)
        {
            value = value[..delimiter];
        }

        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static string? GetString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private sealed class ComponentWorkItem(
        JsonObject component,
        string displayName,
        NuGetPurlParseStatus parseStatus,
        PackageIdentity? package,
        bool alreadyLicensed)
    {
        public JsonObject Component { get; } = component;
        public string DisplayName { get; } = displayName;
        public NuGetPurlParseStatus ParseStatus { get; } = parseStatus;
        public PackageIdentity? Package { get; } = package;
        public bool AlreadyLicensed { get; } = alreadyLicensed;
        public Task<LicenseResult>? LookupTask { get; set; }
    }
}
