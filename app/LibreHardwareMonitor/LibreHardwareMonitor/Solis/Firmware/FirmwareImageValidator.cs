#nullable enable

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LibreHardwareMonitor.Solis.Firmware;

public sealed record FirmwareImageInfo(
    string ProjectName,
    string Version,
    ushort ChipId,
    long Size,
    string Sha256);

public sealed record FirmwareImageValidationResult(
    bool Success,
    FirmwareImageInfo? Image,
    string? ErrorMessage);

public static class FirmwareImageValidator
{
    public const string ExpectedProjectName = "solis_monitor";
    public const ushort Esp32S3ChipId = 0x0009;
    private const uint AppDescriptionMagic = 0xABCD5432;
    private const int MinimumHeaderSize = 112;
    private const int AppendedHashSize = 32;
    private const int HashAppendedOffset = 23;

    public static FirmwareImageValidationResult ValidateFile(
        string path,
        long maxImageSize)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Failure("固件文件不存在。");
        try
        {
            return Validate(File.ReadAllBytes(path), maxImageSize);
        }
        catch (IOException)
        {
            return Failure("无法读取固件文件。");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure("没有权限读取固件文件。");
        }
    }

    public static FirmwareImageValidationResult Validate(
        ReadOnlySpan<byte> image,
        long maxImageSize)
    {
        if (maxImageSize <= 0)
            return Failure("设备没有可用的 OTA 分区。");
        if (image.Length < MinimumHeaderSize + AppendedHashSize)
            return Failure("固件文件不完整。");
        if (image.Length > maxImageSize)
            return Failure("固件文件超过设备 OTA 分区容量。");
        if (image[0] != 0xE9)
            return Failure("不是有效的 ESP32 应用固件。");

        ushort chipId = BinaryPrimitives.ReadUInt16LittleEndian(
            image.Slice(12, 2));
        if (chipId != Esp32S3ChipId)
            return Failure("固件不是为 ESP32-S3 构建的。");
        if (image[HashAppendedOffset] != 1)
            return Failure("固件没有 ESP-IDF 完整性摘要。");

        byte[] calculatedHash = ComputeSha256(
            image.Slice(0, image.Length - AppendedHashSize));
        if (!FixedTimeEquals(
                calculatedHash,
                image.Slice(image.Length - AppendedHashSize))) {
            return Failure("固件 SHA-256 完整性校验失败。");
        }

        uint descriptionMagic = BinaryPrimitives.ReadUInt32LittleEndian(
            image.Slice(32, 4));
        if (descriptionMagic != AppDescriptionMagic)
            return Failure("固件应用描述无效。");

        string version = ReadFixedAscii(image.Slice(48, 32));
        string project = ReadFixedAscii(image.Slice(80, 32));
        if (!string.Equals(
                project,
                ExpectedProjectName,
                StringComparison.Ordinal)) {
            return Failure("固件不属于 Solis Monitor 项目。");
        }
        if (string.IsNullOrWhiteSpace(version))
            return Failure("固件版本信息为空。");

        string sha256 = ToHexString(ComputeSha256(image));
        return new FirmwareImageValidationResult(
            true,
            new FirmwareImageInfo(
                project,
                version,
                chipId,
                image.Length,
                sha256),
            null);
    }

    private static string ReadFixedAscii(ReadOnlySpan<byte> value)
    {
        int length = value.IndexOf((byte)0);
        if (length < 0)
            length = value.Length;
        return Encoding.ASCII.GetString(
            value.Slice(0, length).ToArray()).Trim();
    }

    private static byte[] ComputeSha256(ReadOnlySpan<byte> value)
    {
        using SHA256 sha256 = SHA256.Create();
        return sha256.ComputeHash(value.ToArray());
    }

    private static bool FixedTimeEquals(
        ReadOnlySpan<byte> left,
        ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length)
            return false;
        int difference = 0;
        for (int index = 0; index < left.Length; index++)
            difference |= left[index] ^ right[index];
        return difference == 0;
    }

    private static string ToHexString(ReadOnlySpan<byte> value)
    {
        var builder = new StringBuilder(value.Length * 2);
        for (int index = 0; index < value.Length; index++)
            builder.Append(value[index].ToString("X2"));
        return builder.ToString();
    }

    private static FirmwareImageValidationResult Failure(string message) =>
        new(false, null, message);
}
