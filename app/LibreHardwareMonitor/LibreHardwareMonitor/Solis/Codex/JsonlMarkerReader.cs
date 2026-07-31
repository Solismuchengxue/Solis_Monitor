#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LibreHardwareMonitor.Solis.Codex;

internal static class JsonlMarkerReader
{
    private const int ScanBufferSize = 64 * 1024;
    private const int MaxMatchingLineBytes = 4 * 1024 * 1024;

    public static long ReadMatchingLines(
        string path,
        long startOffset,
        Action<string> onMatch,
        params string[] markers)
    {
        ArgumentNullException.ThrowIfNull(onMatch);
        ArgumentNullException.ThrowIfNull(markers);
        if (markers.Length == 0)
            throw new ArgumentException("At least one marker is required.", nameof(markers));

        using FileStream scanner = OpenShared(path, FileOptions.SequentialScan);
        long scanLength = scanner.Length;
        long offset = Math.Clamp(startOffset, 0, scanLength);
        scanner.Seek(offset, SeekOrigin.Begin);
        using FileStream lineReader = OpenShared(path, FileOptions.RandomAccess);

        byte[][] markerBytes = new byte[markers.Length][];
        int[] matchedBytes = new int[markers.Length];
        for (int i = 0; i < markers.Length; i++)
        {
            if (string.IsNullOrEmpty(markers[i]))
                throw new ArgumentException("Markers cannot be empty.", nameof(markers));
            markerBytes[i] = Encoding.UTF8.GetBytes(markers[i]);
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(ScanBufferSize);
        try
        {
            long absoluteOffset = offset;
            long lineStart = offset;
            bool lineMatches = false;
            while (absoluteOffset < scanLength)
            {
                int requested = (int)Math.Min(buffer.Length, scanLength - absoluteOffset);
                int read = scanner.Read(buffer, 0, requested);
                if (read == 0)
                    break;

                for (int index = 0; index < read; index++, absoluteOffset++)
                {
                    byte value = buffer[index];
                    if (value == (byte)'\n')
                    {
                        if (lineMatches)
                            PublishLine(lineReader, lineStart, absoluteOffset, onMatch);

                        lineStart = absoluteOffset + 1;
                        lineMatches = false;
                        Array.Clear(matchedBytes);
                        continue;
                    }

                    if (lineMatches)
                        continue;

                    for (int markerIndex = 0; markerIndex < markerBytes.Length; markerIndex++)
                    {
                        byte[] marker = markerBytes[markerIndex];
                        int matched = matchedBytes[markerIndex];
                        if (value == marker[matched])
                        {
                            matched++;
                            if (matched == marker.Length)
                            {
                                lineMatches = true;
                                break;
                            }
                        }
                        else
                        {
                            matched = value == marker[0] ? 1 : 0;
                        }

                        matchedBytes[markerIndex] = matched;
                    }
                }
            }

            if (lineMatches && lineStart < scanLength)
                PublishLine(lineReader, lineStart, scanLength, onMatch);

            return scanLength;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static FileStream OpenShared(string path, FileOptions options) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            ScanBufferSize,
            options);

    private static void PublishLine(
        FileStream reader,
        long start,
        long end,
        Action<string> onMatch)
    {
        long byteLength = end - start;
        if (byteLength <= 0 || byteLength > MaxMatchingLineBytes)
            return;

        int length = checked((int)byteLength);
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            reader.Seek(start, SeekOrigin.Begin);
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = reader.Read(bytes, totalRead, length - totalRead);
                if (read == 0)
                    return;
                totalRead += read;
            }

            int textLength = length > 0 && bytes[length - 1] == (byte)'\r'
                ? length - 1
                : length;
            onMatch(Encoding.UTF8.GetString(bytes, 0, textLength));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }
}
