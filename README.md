# NuGetLicenseEnricher

`NuGetLicenseEnricher` is a .NET 10 console application that enriches unlicensed NuGet components in a CycloneDX 1.6 JSON SBOM. It always queries the exact package ID and version encoded in the component PURL and preserves the rest of the JSON document.

## Requirements

- .NET 10 SDK
- Network access to `api.nuget.org`

## Build and test

```shell
dotnet restore NuGetLicenseEnricher.slnx
dotnet build NuGetLicenseEnricher.slnx --no-restore
dotnet test NuGetLicenseEnricher.slnx --no-build
```

## Usage

From the repository root:

```shell
dotnet run --project NuGetLicenseEnricher/NuGetLicenseEnricher.csproj -- \
  --input bom.json \
  --output bom.enriched.json
```

On PowerShell, the same command can be entered on one line:

```powershell
dotnet run --project NuGetLicenseEnricher/NuGetLicenseEnricher.csproj -- --input bom.json --output bom.enriched.json
```

The [`samples/bom.json`](samples/bom.json) and [`samples/bom.enriched.json`](samples/bom.enriched.json) files show an input and its enriched result.

## Lookup behavior

Only top-level `components` whose PURL starts with `pkg:nuget/` are considered. The package ID and version come from the PURL, including percent decoding; the component's `version` field is not substituted for a missing PURL version. Components are skipped when they are operating-system components, already licensed, or do not contain a usable exact NuGet version.

For each package, the application uses this order:

1. `licenseExpression` in the exact-version NuGet registration catalog entry.
2. An expression declared by `<license type="expression">` in the package `.nuspec`.
3. An existing package file declared by `<license type="file">`, represented as `Custom license`.
4. A legacy HTTP(S) `licenseUrl`, recorded as a named CycloneDX license with its URL.

No latest-version lookup is performed. Repository links and license URLs are never followed, and no license is inferred from package contents or source repositories. Results are cached case-insensitively by normalized package ID and NuGet version for the duration of the run.

## Reliability and safety

- At most five package lookups run concurrently.
- HTTP requests time out after 30 seconds.
- HTTP 408, 429, 5xx responses, timeouts, and network failures are retried up to three times.
- A failed package is reported and does not stop other components.
- Registration JSON is limited to 10 MiB and `.nupkg` downloads to 100 MiB.
- Packages are opened as ZIP archives in memory. Files are not extracted or executed.
- Unsafe absolute or `..` ZIP paths are ignored; XML DTD processing and external resolution are disabled.
- Ctrl+C cancels outstanding work without writing a partial enriched output.

Input parsing requires a JSON object with `bomFormat: "CycloneDX"`, `specVersion: "1.6"`, and an array of component objects when `components` is present. Unknown properties are deliberately retained, including extension data that the official schema may not recognize. The supplied samples are validated against the official CycloneDX 1.6 JSON schema.

## Exit codes

- `0`: processing completed, was cancelled, or only individual package lookups failed
- `2`: the command line or input could not be read
- `3`: the input is not a CycloneDX 1.6 JSON document
- `4`: the output could not be written

## Dependency-Track compatibility

The application emits the CycloneDX 1.6 license choices expected by Dependency-Track and does not alter `bom-ref` or dependency references. Dependency-Track added CycloneDX 1.6 ingestion support in version 4.11.4. Validate organization-specific extensions and upload limits in the target Dependency-Track deployment before production import.
