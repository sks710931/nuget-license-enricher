using System.Text.Json.Nodes;

namespace NuGetLicenseEnricher.Services;

public static class CycloneDxValidator
{
    public static bool TryValidate(JsonNode? root, out JsonObject? bom, out string? error)
    {
        bom = root as JsonObject;
        if (bom is null)
        {
            error = "The JSON root must be an object.";
            return false;
        }

        if (!TryGetString(bom["bomFormat"], out string? bomFormat) ||
            bomFormat != "CycloneDX")
        {
            error = "bomFormat must be 'CycloneDX'.";
            return false;
        }

        if (!TryGetString(bom["specVersion"], out string? specVersion) ||
            specVersion != "1.6")
        {
            error = "specVersion must be '1.6'.";
            return false;
        }

        if (bom["components"] is not null && bom["components"] is not JsonArray)
        {
            error = "components must be an array when present.";
            return false;
        }

        if (bom["components"] is JsonArray components &&
            components.Any(component => component is not JsonObject))
        {
            error = "Every component must be a JSON object.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryGetString(JsonNode? node, out string? value)
    {
        value = null;
        return node is JsonValue jsonValue && jsonValue.TryGetValue(out value);
    }
}
