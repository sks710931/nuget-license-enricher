# Versioning and releases

NuGetLicenseEnricher uses Semantic Versioning and publishes stable releases to NuGet.org from GitHub release tags. The publishing workflow is defined in `.github/workflows/Publish.yml`.

## Version format

Release versions use three numeric parts:

```text
MAJOR.MINOR.PATCH
```

- Increment `MAJOR` for incompatible command-line or behavior changes.
- Increment `MINOR` for backward-compatible features.
- Increment `PATCH` for backward-compatible fixes.

Examples:

```text
1.0.0
1.1.0
1.1.1
2.0.0
```

The release workflow accepts stable tags only. Prerelease tags such as `v1.1.0-beta.1` are not accepted.

## Version source of truth

The project version is stored in `NuGetLicenseEnricher/NuGetLicenseEnricher.csproj`:

```xml
<VersionPrefix>1.0.0</VersionPrefix>
```

A release tag must contain exactly the same version with a leading `v`. For example:

| `VersionPrefix` | Required tag |
| --- | --- |
| `1.0.0` | `v1.0.0` |
| `1.2.3` | `v1.2.3` |

The publish workflow stops before packing or publishing when the versions do not match.

## Configure NuGet.org publishing

Publishing requires a NuGet.org API key stored as a GitHub Actions repository secret.

1. Sign in to [NuGet.org](https://www.nuget.org/).
2. Open the account menu and select **API Keys**.
3. Create a key with the **Push** scope.
4. Restrict its package glob to `NuGetLicenseEnricher.Tool`.
5. Select an appropriate expiration date and copy the key.
6. In GitHub, open **Settings → Secrets and variables → Actions**.
7. Create a repository secret named `NUGET_KEY` and paste the API key as its value.

Never place the API key in source files, workflow YAML, command history, issues, or build output. Rotate the key before it expires and immediately if it may have been exposed.

## Prepare a release

Start from an up-to-date `master` branch with a passing CI workflow:

```shell
git switch master
git pull --ff-only origin master
```

Update `VersionPrefix` in the project file. Then run the release checks locally:

```shell
dotnet restore NuGetLicenseEnricher.slnx
dotnet build NuGetLicenseEnricher.slnx --configuration Release --no-restore
dotnet test NuGetLicenseEnricher.slnx --configuration Release --no-build
dotnet pack NuGetLicenseEnricher/NuGetLicenseEnricher.csproj --configuration Release --no-build --no-restore --output artifacts/packages
```

Review the README, license, package metadata, and generated `.nupkg`. Commit the version change:

```shell
git add NuGetLicenseEnricher/NuGetLicenseEnricher.csproj
git commit -m "Release 1.1.0"
git push origin master
```

Replace `1.1.0` with the version being released.

## Create and push the release tag

Create an annotated tag on the release commit:

```shell
git tag -a v1.1.0 -m "NuGetLicenseEnricher 1.1.0"
git push origin v1.1.0
```

Pushing the tag starts the **Publish NuGet Package** workflow. It performs these operations in order:

1. Validates the tag format.
2. Confirms the tag matches `VersionPrefix`.
3. Restores, builds, and tests the solution.
4. Packs and installs the `.NET` tool.
5. Runs an installed-package smoke test.
6. Pushes the package to NuGet.org.

## Verify the release

1. Open the repository's **Actions** page and confirm the publishing workflow succeeded.
2. Wait for NuGet.org validation and indexing to complete.
3. Check `https://www.nuget.org/packages/NuGetLicenseEnricher.Tool/<version>`.
4. Test the published package from a clean directory:

```shell
dotnet tool install --tool-path .tools NuGetLicenseEnricher.Tool --version 1.1.0
.tools/nuget-license-enricher --input bom.json --output bom.enriched.json
```

On Windows, invoke `.tools\nuget-license-enricher.exe`.

## Failed releases and corrections

Package versions are immutable. Never move or recreate a release tag after its package has been published.

- If the workflow fails before publishing, correct the problem and rerun the failed workflow job.
- If a tag points to the wrong commit and no package was published, delete the remote and local tag, fix the release commit, and create the tag again:

  ```shell
  git push origin --delete v1.1.0
  git tag --delete v1.1.0
  ```

- If the package was published with a defect, update `VersionPrefix`, create a new patch release, and publish a new tag. For example, replace `1.1.0` with `1.1.1`.
- Do not attempt to replace an existing `.nupkg` with different contents under the same version.

The workflow uses `--skip-duplicate`, so rerunning a successful publication does not overwrite the existing package.
