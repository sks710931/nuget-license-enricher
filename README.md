# NuGetLicenseEnricher

`NuGetLicenseEnricher` enriches missing NuGet licence information in CycloneDX 1.6 JSON SBOMs while preserving components, dependencies, references, vulnerabilities, and unknown JSON properties.

## Requirements

- .NET 10 SDK or runtime
- Network access to `api.nuget.org`
- A CycloneDX 1.6 JSON SBOM

## Install from NuGet.org

Install the tool globally:

```shell
dotnet tool install --global NuGetLicenseEnricher.Tool --version 1.0.0
```

Update an existing installation:

```shell
dotnet tool update --global NuGetLicenseEnricher.Tool --version 1.0.0
```

After installation, the command is available as `nuget-license-enricher`.

## Usage

```shell
nuget-license-enricher --input bom.json --output bom.enriched.json
```

The input file is not modified. The enriched CycloneDX 1.6 JSON SBOM is written to the path supplied with `--output`.

### PowerShell example

```powershell
nuget-license-enricher `
  --input "C:\SBOM\bom.json" `
  --output "C:\SBOM\bom.enriched.json"
```

### Linux or macOS example

```shell
nuget-license-enricher \
  --input ./sbom/bom.json \
  --output ./sbom/bom.enriched.json
```

## Uninstall

```shell
dotnet tool uninstall --global NuGetLicenseEnricher.Tool
```

## Optional - .NET 9 LTS

This is not a multitarget build. You decide to build either .NET 10 or .NET 9. To checkout and build .NET 9 LTS you need the optional `UseNet9` parameter to the dotnet command:

```shell
dotnet restore -p:UseNet9=true
dotnet build -p:UseNet9=true
dotnet test -p:UseNet9=true
```
```shell
cd NuGetLicenseEnricher
dotnet run -p:UseNet9=true -- --input bom.json --output bom.enriched.
```
