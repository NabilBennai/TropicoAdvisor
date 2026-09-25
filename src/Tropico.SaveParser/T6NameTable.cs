using System.Text;

namespace Tropico.SaveParser;

/// <summary>
/// Name table at the start of the decompressed data: u32 count, then count x (i32 length including NUL, ASCII bytes, NUL).
/// Property names, property types, enum values and struct names are referenced by index from the rest of the data.
/// </summary>
public sealed class T6NameTable
{
    private const int MaxNameLength = 4096;

    public required IReadOnlyList<string> Names { get; init; }

    /// <summary>Offset (in the decompressed data) of the first byte after the table.</summary>
    public required int EndOffset { get; init; }

    public static T6NameTable Parse(byte[] data)
    {
        if (data.Length < 4) throw new InvalidDataException("Decompressed data too short for a name table.");

        var count = BitConverter.ToUInt32(data, 0);
        if (count > data.Length / 5) throw new InvalidDataException($"Implausible name count: {count}.");

        var names = new List<string>((int)count);
        var offset = 4;

        for (var i = 0; i < count; i++)
        {
            if (offset + 4 > data.Length) throw new InvalidDataException($"Name #{i}: truncated length at 0x{offset:X}.");

            var length = BitConverter.ToInt32(data, offset);
            offset += 4;

            if (length < 1 || length > MaxNameLength || offset + length > data.Length)
                throw new InvalidDataException($"Name #{i}: invalid length {length} at 0x{offset - 4:X}.");
            if (data[offset + length - 1] != 0)
                throw new InvalidDataException($"Name #{i}: missing NUL terminator at 0x{offset + length - 1:X}.");

            names.Add(Encoding.Latin1.GetString(data, offset, length - 1));
            offset += length;
        }

        return new T6NameTable { Names = names, EndOffset = offset };
    }
}
