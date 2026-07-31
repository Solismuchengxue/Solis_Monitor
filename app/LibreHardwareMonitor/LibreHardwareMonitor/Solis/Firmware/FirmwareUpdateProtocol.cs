#nullable enable

using System;
using System.Text.Json;

namespace LibreHardwareMonitor.Solis.Firmware;

public sealed record FirmwareDeviceStatus(
    string Version,
    long MaxImageSize,
    bool RollbackEnabled);

public static class FirmwareUpdateProtocol
{
    public static bool TryParseStatus(
        string json,
        out FirmwareDeviceStatus? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!TryString(root, "product", out string? product) ||
                !string.Equals(product, "Solis Monitor", StringComparison.Ordinal) ||
                !TryString(root, "chip", out string? chip) ||
                !string.Equals(chip, "esp32s3", StringComparison.Ordinal) ||
                !TryString(root, "project", out string? project) ||
                !string.Equals(
                    project,
                    FirmwareImageValidator.ExpectedProjectName,
                    StringComparison.Ordinal) ||
                !TryString(root, "version", out string? version) ||
                !root.TryGetProperty(
                    "max_image_size",
                    out JsonElement maxImageSizeElement) ||
                !maxImageSizeElement.TryGetInt64(out long maxImageSize) ||
                maxImageSize <= 0 ||
                !root.TryGetProperty("rollback", out JsonElement rollback) ||
                rollback.ValueKind is not (
                    JsonValueKind.True or JsonValueKind.False)) {
                return false;
            }

            status = new FirmwareDeviceStatus(
                version!,
                maxImageSize,
                rollback.GetBoolean());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryString(
        JsonElement root,
        string name,
        out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement element) ||
            element.ValueKind != JsonValueKind.String) {
            return false;
        }
        value = element.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }
}
