using System.Buffers.Binary;

namespace SaveRescuer.Core;

/// <summary>Where one ChangeForm sits in the section, and what it points at.</summary>
public readonly record struct ChangeFormEntry(int Start, int End, int RefType, int RefValue)
{
    public int Length => End - Start;

    /// <summary>Type 0 means the reference is an index into the save's form ID array.</summary>
    public bool PointsAtFormIdArray => RefType == 0;
}

/// <summary>Which plugin a form ID belongs to.</summary>
public enum FormOwnerKind
{
    /// <summary>Created during play (0xFF); belongs to no plugin.</summary>
    Created,
    Regular,
    Light,
    /// <summary>The index is past the end of the save's own plugin list.</summary>
    OutOfRange
}

public readonly record struct FormOwner(FormOwnerKind Kind, int Index);

public static class ChangeForms
{
    /// <summary>
    /// Walks the ChangeForm section, yielding each record's bounds. The header is 3 bytes of
    /// reference ID, 4 flag bytes, a type byte, a version byte and then two length fields whose
    /// width the top two bits of the type select.
    /// </summary>
    public static IEnumerable<ChangeFormEntry> Walk(byte[] section, long expectedCount)
    {
        var p = 0;
        for (long i = 0; i < expectedCount; i++)
        {
            var start = p;
            if (p + 10 > section.Length) yield break;

            var (refType, refValue) = DecodeRefId(section, p);
            p += 3 + 4;                       // reference ID, change flags
            var type = section[p];
            p += 1 + 1;                       // type, version

            long dataLength;
            switch (type >> 6)
            {
                case 0:
                    dataLength = section[p];
                    p += 2;                   // length1 + length2, one byte each
                    break;
                case 1:
                    dataLength = BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(p, 2));
                    p += 4;
                    break;
                default:
                    dataLength = BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(p, 4));
                    p += 8;
                    break;
            }

            if (p + dataLength > section.Length) yield break;
            p += (int)dataLength;
            yield return new ChangeFormEntry(start, p, refType, refValue);
        }
    }

    /// <summary>The offset the ChangeForm's version byte sits at, relative to the section.</summary>
    public static int VersionByteOffset(ChangeFormEntry entry) => entry.Start + 3 + 4 + 1;

    /// <summary>Reference ID: three big-endian bytes whose top two bits carry the type.</summary>
    public static (int Type, int Value) DecodeRefId(byte[] section, int at)
    {
        var v = (section[at] << 16) | (section[at + 1] << 8) | section[at + 2];
        return (v >> 22, v & 0x3FFFFF);
    }

    /// <summary>
    /// The plugin a form ID belongs to. The top byte is the regular plugin index, 0xFE marks a
    /// light plugin (12-bit index in the next bits) and 0xFF marks a form created during play.
    /// </summary>
    public static FormOwner OwnerOf(uint formId, int regularCount, int lightCount)
    {
        var hi = formId >> 24;
        if (hi == 0xFF) return new FormOwner(FormOwnerKind.Created, -1);
        if (hi == 0xFE)
        {
            var sub = (int)((formId >> 12) & 0xFFF);
            return new FormOwner(sub < lightCount ? FormOwnerKind.Light : FormOwnerKind.OutOfRange, sub);
        }
        return new FormOwner(hi < (uint)regularCount ? FormOwnerKind.Regular : FormOwnerKind.OutOfRange, (int)hi);
    }
}
