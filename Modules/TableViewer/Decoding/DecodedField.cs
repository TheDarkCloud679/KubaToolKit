namespace KubaToolKit.Modules.TableViewer.Decoding;

// Mirrors the columns QTable itself shows (Nom/Valeur/Type/Taille/Offset
// BYTE/Offset bit) so the output reads the same way to someone who
// already knows that tool.
public class DecodedField
{
    public required string Name { get; init; }

    public required string Value { get; init; }

    public required string Type { get; init; }

    public long SizeBits { get; init; }

    public long OffsetBytes { get; init; }

    public long OffsetBits { get; init; }

    public int Depth { get; init; }
}
