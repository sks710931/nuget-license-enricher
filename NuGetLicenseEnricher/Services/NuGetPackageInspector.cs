using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace NuGetLicenseEnricher.Services;

public sealed record NuGetPackageInspection(
    string? LicenseExpression,
    bool HasEmbeddedLicenseFile,
    string? LicenseUrl);

public sealed class NuGetPackageInspector
{
    private const long MaxNuspecSize = 2 * 1024 * 1024;

    public NuGetPackageInspection Inspect(Stream packageStream)
    {
        ArgumentNullException.ThrowIfNull(packageStream);

        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        ZipArchiveEntry? nuspecEntry = archive.Entries.FirstOrDefault(entry =>
            IsSafeRelativePath(entry.FullName) &&
            entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

        if (nuspecEntry is null || nuspecEntry.Length > MaxNuspecSize)
        {
            return new NuGetPackageInspection(null, false, null);
        }

        XDocument nuspec;
        using (Stream stream = nuspecEntry.Open())
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxNuspecSize
        }))
        {
            nuspec = XDocument.Load(reader, LoadOptions.None);
        }

        XElement? metadata = nuspec.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "metadata");
        if (metadata is null)
        {
            return new NuGetPackageInspection(null, false, null);
        }

        XElement? license = metadata.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "license");
        string? licenseType = license?.Attribute("type")?.Value.Trim();
        string? licenseValue = Normalize(license?.Value);

        string? expression = licenseType?.Equals("expression", StringComparison.OrdinalIgnoreCase) is true
            ? licenseValue
            : null;

        bool hasEmbeddedFile = false;
        if (licenseType?.Equals("file", StringComparison.OrdinalIgnoreCase) is true &&
            licenseValue is not null &&
            IsSafeRelativePath(licenseValue))
        {
            string normalizedLicensePath = NormalizeZipPath(licenseValue);
            hasEmbeddedFile = archive.Entries.Any(entry =>
                IsSafeRelativePath(entry.FullName) &&
                string.Equals(
                    NormalizeZipPath(entry.FullName),
                    normalizedLicensePath,
                    StringComparison.OrdinalIgnoreCase));
        }

        string? licenseUrl = Normalize(metadata.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "licenseUrl")?.Value);

        return new NuGetPackageInspection(expression, hasEmbeddedFile, licenseUrl);
    }

    internal static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith('/') ||
            path.StartsWith('\\') ||
            Path.IsPathRooted(path))
        {
            return false;
        }

        return !path.Replace('\\', '/').Split('/')
            .Any(segment => segment is "..");
    }

    private static string NormalizeZipPath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
