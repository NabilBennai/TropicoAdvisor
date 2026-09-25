using System.IO.Compression;
using System.Text;

namespace Tropico.SaveParser;

/// <summary>
/// Reads the outer container of a Tropico 6 .t6sav file:
/// "Lama" | u32 | build string (zero padded up to 0x38) | u32 headerLength at 0x38 (zlib offset = headerLength + 8) | ... | zlib stream to EOF.
/// The name and object tables are parsed; object blobs are not.
/// </summary>
public static class T6SaveReader
{
    private const string Signature = "Lama";
    private const int BuildFieldStart = 8;
    private const int HeaderLengthFieldOffset = 0x38;
    private const int HeaderLengthBias = 8;
    private const int FirstMetadataOffset = 0x3C;

    public static T6SaveFile Read(string path) => Read(File.ReadAllBytes(path));

    public static T6SaveFile Read(byte[] file)
    {
        if (file.Length < FirstMetadataOffset ||
            Encoding.ASCII.GetString(file, 0, 4) != Signature)
        {
            throw new InvalidDataException("Not a Tropico 6 save: missing 'Lama' signature.");
        }

        var buildEnd = Array.IndexOf(file, (byte)0, BuildFieldStart, HeaderLengthFieldOffset - BuildFieldStart);
        if (buildEnd < 0) buildEnd = HeaderLengthFieldOffset;
        var build = Encoding.ASCII.GetString(file, BuildFieldStart, buildEnd - BuildFieldStart);

        var (offset, data) = FindCompressedData(file);
        var nameTable = T6NameTable.Parse(data);

        return new T6SaveFile
        {
            Header = ParseHeader(file, build, offset),
            DecompressedData = data,
            NameTable = nameTable,
            ObjectTable = T6ObjectTable.Parse(data, nameTable.EndOffset),
        };
    }

    // Preferred: the header stores its own length. Fallback: scan for a zlib signature that inflates cleanly.
    private static (int Offset, byte[] Data) FindCompressedData(byte[] file)
    {
        var declared = BitConverter.ToInt32(file, HeaderLengthFieldOffset) + HeaderLengthBias;
        if (declared > FirstMetadataOffset && declared < file.Length - 2 &&
            IsZlibHeader(file, declared) && TryInflate(file, declared, out var declaredData))
        {
            return (declared, declaredData);
        }

        for (var i = FirstMetadataOffset; i < file.Length - 2; i++)
        {
            if (IsZlibHeader(file, i) && TryInflate(file, i, out var data)) return (i, data);
        }

        throw new InvalidDataException("No valid zlib stream found in save.");
    }

    private static bool IsZlibHeader(byte[] f, int i) =>
        f[i] == 0x78 && f[i + 1] is 0x01 or 0x5E or 0x9C or 0xDA;

    private static bool TryInflate(byte[] file, int offset, out byte[] data)
    {
        try
        {
            using var input = new MemoryStream(file, offset, file.Length - offset, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            data = output.ToArray();
            return data.Length > 0;
        }
        catch (InvalidDataException)
        {
            data = [];
            return false;
        }
    }

    // After the fixed fields the header carries length-prefixed (i32, NUL-terminated) strings:
    // map id, save name, ..., Steam id. Found by scanning, so the numeric fields in between need no decoding.
    private static T6SaveHeader ParseHeader(byte[] file, string build, int headerLength)
    {
        var strings = new List<string>();
        var i = FirstMetadataOffset;
        while (i + 5 < headerLength)
        {
            var len = BitConverter.ToInt32(file, i);
            if (len is >= 2 and <= 256 &&
                i + 4 + len <= headerLength &&
                file[i + 4 + len - 1] == 0 &&
                IsPrintable(file, i + 4, len - 1))
            {
                strings.Add(Encoding.ASCII.GetString(file, i + 4, len - 1));
                i += 4 + len;
            }
            else
            {
                i++;
            }
        }

        return new T6SaveHeader
        {
            Build = build,
            MapId = strings.ElementAtOrDefault(0),
            SaveName = strings.ElementAtOrDefault(1),
            SteamId = strings.FirstOrDefault(s => s.StartsWith("Steam_", StringComparison.Ordinal)),
            CompressedDataOffset = headerLength,
        };
    }

    private static bool IsPrintable(byte[] f, int start, int count)
    {
        for (var k = start; k < start + count; k++)
        {
            if (f[k] is < 0x20 or > 0x7E) return false;
        }

        return true;
    }
}
