using System.IO;
using System.Runtime.InteropServices;
using TCTV.Generated;

namespace TCTV.Core;

/// <summary>
/// Entpackt die im Original eingebetteten Ressourcen. Das AutoIt-Skript dekodiert
/// Base64 (CryptStringToBinaryA) und entpackt die Blobs anschliessend mit
/// RtlDecompressBuffer (COMPRESSION_FORMAT_LZNT1) - hier identisch nachgebildet.
/// </summary>
public static class ResourceProvider
{
    private const ushort CompressionFormatLznt1 = 0x0002;

    private static readonly Dictionary<string, byte[]> Cache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("ntdll.dll")]
    private static extern uint RtlDecompressBuffer(
        ushort compressionFormat,
        byte[] uncompressedBuffer,
        uint uncompressedBufferSize,
        byte[] compressedBuffer,
        uint compressedBufferSize,
        out uint finalUncompressedSize);

    /// <summary>Liefert die dekodierten Rohdaten einer eingebetteten Datei (z. B. "deutsch.bmp").</summary>
    public static byte[] GetBytes(string fileName)
    {
        if (Cache.TryGetValue(fileName, out byte[]? cached))
        {
            return cached;
        }

        EmbeddedResources.Resource? found = null;
        foreach (EmbeddedResources.Resource resource in EmbeddedResources.All)
        {
            if (string.Equals(resource.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                found = resource;
                break;
            }
        }

        if (found is not { } res)
        {
            throw new FileNotFoundException($"Eingebettete Ressource '{fileName}' ist nicht vorhanden.");
        }

        byte[] raw = Convert.FromBase64String(res.Base64);
        byte[] data = res.Compressed ? Lznt1Decompress(raw, res.UncompressedSize) : raw;
        Cache[fileName] = data;
        return data;
    }

    /// <summary>Schreibt alle eingebetteten Dateien in das Arbeitsverzeichnis (Temp).</summary>
    public static void ExtractToTemp()
    {
        foreach (EmbeddedResources.Resource resource in EmbeddedResources.All)
        {
            string target = AppPaths.Temp(resource.FileName);
            try
            {
                File.WriteAllBytes(target, GetBytes(resource.FileName));
            }
            catch (IOException)
            {
                // Nicht kritisch - Original ignoriert Schreibfehler ebenfalls.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Schreibt eine einzelne Ressource in das Arbeitsverzeichnis und liefert den Pfad.</summary>
    public static string Extract(string fileName)
    {
        string target = AppPaths.Temp(fileName);
        File.WriteAllBytes(target, GetBytes(fileName));
        return target;
    }

    private static byte[] Lznt1Decompress(byte[] compressed, int uncompressedSize)
    {
        if (uncompressedSize <= 0)
        {
            throw new InvalidDataException("Ungueltige Zielgroesse fuer die Kompression.");
        }

        byte[] buffer = new byte[uncompressedSize];
        uint status = RtlDecompressBuffer(
            CompressionFormatLznt1,
            buffer,
            (uint)buffer.Length,
            compressed,
            (uint)compressed.Length,
            out uint finalSize);

        if (status != 0)
        {
            throw new InvalidDataException($"RtlDecompressBuffer ist fehlgeschlagen (NTSTATUS 0x{status:X8}).");
        }

        if (finalSize != buffer.Length)
        {
            Array.Resize(ref buffer, (int)finalSize);
        }

        return buffer;
    }
}
