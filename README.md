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

## Install from Azure Artifacts

Authenticate with the Azure Artifacts feed, then install using its NuGet V3 URL:

```shell
dotnet tool install --global NuGetLicenseEnricher.Tool --version 1.0.0 --add-source "https://pkgs.dev.azure.com/ORGANIZATION/PROJECT/_packaging/FEED/nuget/v3/index.json"
```

Do not place feed credentials or personal access tokens in scripts or source control.

### Azure Pipeline

Install the tool into the agent's temporary directory so each pipeline run uses a clean, pinned installation:

```yaml
- task: UseDotNet@2
  displayName: Install .NET 10 SDK
  inputs:
    packageType: sdk
    version: '10.x'

- task: NuGetAuthenticate@1
  displayName: Authenticate with Azure Artifacts

- bash: |
    dotnet tool install \
      --tool-path "$(Agent.TempDirectory)/nuget-license-tools" \
      --add-source "https://pkgs.dev.azure.com/ORGANIZATION/PROJECT/_packaging/FEED/nuget/v3/index.json" \
      NuGetLicenseEnricher.Tool \
      --version 1.0.0
  displayName: Install NuGetLicenseEnricher
```

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

### Azure Pipeline usage

When installed with `--tool-path` as shown above:

```yaml
- bash: |
    "$(Agent.TempDirectory)/nuget-license-tools/nuget-license-enricher" \
      --input "$(Build.SourcesDirectory)/bom.json" \
      --output "$(Build.ArtifactStagingDirectory)/bom.enriched.json"
  displayName: Enrich CycloneDX SBOM

- publish: $(Build.ArtifactStagingDirectory)/bom.enriched.json
  artifact: enriched-sbom
  displayName: Publish enriched SBOM
```

## Uninstall

```shell
dotnet tool uninstall --global NuGetLicenseEnricher.Tool
```
