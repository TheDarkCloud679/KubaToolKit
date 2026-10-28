namespace KubaToolKit.Modules.TableViewer.Decoding;

public class TableDecodeResult
{
    public required bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public string? Trigram { get; init; }

    public string? DefinitionsFile { get; init; }

    public IReadOnlyList<DecodedField> Fields { get; init; } = Array.Empty<DecodedField>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    public static TableDecodeResult
    Failure(
        string message) =>
        new() { Success = false, ErrorMessage = message };
}
