using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace KubaToolKit.Modules.TableViewer.Decoding;

// Walks a <table> definition against a binary file, the same way QTable
// itself does: field-by-field, left to right, keeping a flat table of
// named variables that later <if>/<switch>/size expressions read back.
// <write>/<seek-for-repair>/<fix>/<delete> (QTable's own repair tools,
// which mutate the file) are deliberately not implemented -- this is a
// read-only viewer.
public class TableInterpreter
{
    // The real runaway-loop risk (an unbounded array that never advances)
    // is caught directly by HandleArray's own stall guard now, so this is
    // just a last-resort ceiling against a pathologically huge file (e.g.
    // a multi-megabyte "raw data" array, one field per byte) freezing the
    // UI thread while it's rendered -- not a bound real files should
    // realistically hit.
    private const int MaxFields = 200_000;

    private static readonly HashSet<string> PlainIntegerTypes =
        new(StringComparer.OrdinalIgnoreCase) { "u8", "u16", "u24", "u32", "u40", "u48", "u56", "u64" };

    private readonly BitReader _reader;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<string, object> _vars = new();
    private readonly List<DecodedField> _fields = new();
    private readonly List<string> _notes = new();
    private readonly HashSet<string> _warnedTags = new(StringComparer.OrdinalIgnoreCase);

    public TableInterpreter(byte[] data, CancellationToken cancellationToken = default)
    {
        _reader = new BitReader(data);
        _cancellationToken = cancellationToken;
    }

    public (IReadOnlyList<DecodedField> Fields, IReadOnlyList<string> Notes)
    Run(
        XElement table)
    {
        try
        {
            Walk(table, 0);
        }
        catch (OperationCanceledException)
        {
            // Absorbed here rather than propagated: whatever decoded
            // before cancellation is still a useful, real partial result.
            _notes.Add("Decoding was cancelled.");
        }
        catch (EndOfStreamException ex)
        {
            _notes.Add($"Stopped reading: {ex.Message}.");
        }
        catch (Exception ex)
        {
            _notes.Add($"Stopped due to an error: {ex.Message}");
        }

        return (_fields, _notes);
    }

    private void
    Walk(
        XElement node,
        int depth)
    {
        foreach (var child in node.Elements())
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (_fields.Count >= MaxFields)
            {
                AddNoteOnce("Stopped: reached the display limit of 200,000 decoded values.");

                return;
            }

            switch (child.Name.LocalName)
            {
                case "field": HandleField(child, depth); break;
                case "struct": HandleStruct(child, depth); break;
                case "array": HandleArray(child, depth); break;
                case "if": HandleIf(child, depth); break;
                case "switch": HandleSwitch(child, depth); break;
                case "oper": HandleOper(child); break;
                case "define": HandleDefine(child); break;
                case "include": break; // already spliced in by TableXmlLoader
                default:
                    AddNoteOnce($"Skipped unsupported <{child.Name.LocalName}> (not implemented yet).", child.Name.LocalName);
                    break;
            }
        }
    }

    private void
    HandleField(
        XElement field,
        int depth)
    {
        var name = (string?)field.Attribute("name") ?? "(unnamed)";
        var type = (string?)field.Attribute("type") ?? "u8";
        var sizeExpr = (string?)field.Attribute("size") ?? "0";
        var varName = (string?)field.Attribute("var");

        long sizeBits;

        try
        {
            sizeBits = ExpressionEvaluator.EvalNumber(sizeExpr, _vars);
        }
        catch (Exception ex)
        {
            _notes.Add($"Couldn't size field '{name}' ({sizeExpr}): {ex.Message}");

            return;
        }

        if (sizeBits <= 0)
        {
            if (varName != null)
            {
                _vars[varName] = 0L;
            }

            return;
        }

        if (sizeBits > _reader.RemainingBits)
        {
            throw new EndOfStreamException($"'{name}' needs {sizeBits} bits but only {_reader.RemainingBits} remain");
        }

        var offsetBytes = _reader.ByteOffset;
        var offsetBits = _reader.BitPosition;

        object value;
        string displayValue;

        if (string.Equals(type, "hexa", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = ReadBytesForBits(sizeBits);

            value = bytes;
            displayValue = "[" + string.Join(" ", bytes.Select(b => b.ToString("X2"))) + "]";
        }
        else if (string.Equals(type, "string", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = ReadBytesForBits(sizeBits);
            var text = Encoding.Latin1.GetString(bytes).TrimEnd('\0', ' ');

            value = text;
            displayValue = text;
        }
        else if (string.Equals(type, "string_u", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = ReadBytesForBits(sizeBits);
            var text = Encoding.BigEndianUnicode.GetString(bytes).TrimEnd('\0', ' ');

            value = text;
            displayValue = text;
        }
        else if (sizeBits <= 64)
        {
            var number = _reader.ReadBits((int)sizeBits);

            value = (long)number;
            displayValue = number.ToString();

            if (!PlainIntegerTypes.Contains(type))
            {
                AddNoteOnce(
                    $"Type '{type}' isn't decoded semantically yet -- shown as a raw number.",
                    "type:" + type);
            }
        }
        else
        {
            var bytes = ReadBytesForBits(sizeBits);

            value = bytes;
            displayValue = "[" + string.Join(" ", bytes.Select(b => b.ToString("X2"))) + "]";

            AddNoteOnce(
                $"Type '{type}' isn't decoded semantically yet -- shown as raw bytes.",
                "type:" + type);
        }

        if (varName != null)
        {
            _vars[varName] = value is byte[] ? 0L : value;
        }

        _fields.Add(
            new DecodedField
            {
                Name = name,
                Value = displayValue,
                Type = type.ToUpperInvariant(),
                SizeBits = sizeBits,
                OffsetBytes = offsetBytes,
                OffsetBits = offsetBits,
                Depth = depth
            });
    }

    private void
    HandleStruct(
        XElement structNode,
        int depth)
    {
        var name = (string?)structNode.Attribute("name");

        if (!string.IsNullOrWhiteSpace(name))
        {
            _fields.Add(
                new DecodedField
                {
                    Name = name,
                    Value = "",
                    Type = "STRUCT",
                    OffsetBytes = _reader.ByteOffset,
                    OffsetBits = _reader.BitPosition,
                    Depth = depth
                });

            Walk(structNode, depth + 1);
        }
        else
        {
            Walk(structNode, depth);
        }
    }

    private void
    HandleArray(
        XElement array,
        int depth)
    {
        var name = (string?)array.Attribute("name") ?? "(array)";
        var sizeExpr = (string?)array.Attribute("size") ?? "0";

        long count;
        var untilEndOfFile = sizeExpr is "*" or "@";

        if (untilEndOfFile)
        {
            count = long.MaxValue;
        }
        else
        {
            try
            {
                count = ExpressionEvaluator.EvalNumber(sizeExpr, _vars);
            }
            catch (Exception ex)
            {
                _notes.Add($"Couldn't size array '{name}' ({sizeExpr}): {ex.Message}");

                return;
            }
        }

        if (count <= 0)
        {
            return;
        }

        _fields.Add(
            new DecodedField
            {
                Name = name,
                Value = untilEndOfFile ? "(until end of file)" : $"{count} entries",
                Type = "ARRAY",
                OffsetBytes = _reader.ByteOffset,
                OffsetBits = _reader.BitPosition,
                Depth = depth
            });

        // A small, explicitly-bounded array (e.g. size="1") is never a
        // hang risk -- it ends on its own after `count` passes even if
        // some of them read nothing. The stall guard below only matters
        // for arrays that are effectively unbounded (size="*"/"@", or an
        // expression that evaluated to something huge), where a
        // malformed body (e.g. entirely behind a false <if>) reading and
        // adding nothing on every pass would otherwise loop forever --
        // not even Cancel could interrupt that, since nothing in such a
        // loop ever yields back to check it.
        var guardAgainstStall = untilEndOfFile || count > 100_000;

        for (long i = 0; i < count; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (_reader.AtEnd)
            {
                break;
            }

            if (_fields.Count >= MaxFields)
            {
                AddNoteOnce("Stopped: reached the display limit of 200,000 decoded values.");

                return;
            }

            var bitsBefore = _reader.BitPosition;
            var fieldsBefore = _fields.Count;

            Walk(array, depth + 1);

            if (guardAgainstStall && _reader.BitPosition == bitsBefore && _fields.Count == fieldsBefore)
            {
                AddNoteOnce(
                    $"Array '{name}' stopped making progress -- stopped early to avoid hanging.",
                    "array-stall:" + name);

                break;
            }
        }
    }

    private void
    HandleIf(
        XElement ifNode,
        int depth)
    {
        var cond = (string?)ifNode.Attribute("cond") ?? "0";

        bool result;

        try
        {
            result = ExpressionEvaluator.EvalCondition(cond, _vars);
        }
        catch (Exception ex)
        {
            _notes.Add($"Couldn't evaluate condition '{cond}': {ex.Message}");

            return;
        }

        if (result)
        {
            Walk(ifNode, depth);
        }
    }

    private void
    HandleSwitch(
        XElement switchNode,
        int depth)
    {
        var varName = (string?)switchNode.Attribute("name") ?? "";

        if (!_vars.TryGetValue(varName, out var switchValueObj))
        {
            _notes.Add($"Switch on undefined variable '{varName}'.");

            return;
        }

        var switchValue = switchValueObj is long l ? l : 0L;

        XElement? chosen = null;

        foreach (var caseEl in switchNode.Elements("case"))
        {
            var condStr = (string?)caseEl.Attribute("cond");

            if (condStr == null)
            {
                continue;
            }

            try
            {
                if (ExpressionEvaluator.EvalNumber(condStr, _vars) == switchValue)
                {
                    chosen = caseEl;

                    break;
                }
            }
            catch
            {
                // malformed case condition -- skip it, keep looking
            }
        }

        chosen ??= switchNode.Element("default");

        if (chosen != null)
        {
            Walk(chosen, depth);
        }
    }

    private void
    HandleOper(
        XElement oper)
    {
        var action = (string?)oper.Attribute("action");

        if (string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        try
        {
            ExpressionEvaluator.EvalAssignment(action, _vars);
        }
        catch (Exception ex)
        {
            _notes.Add($"Couldn't evaluate action '{action}': {ex.Message}");
        }
    }

    private void
    HandleDefine(
        XElement define)
    {
        var name = (string?)define.Attribute("name");
        var value = (string?)define.Attribute("value");

        if (string.IsNullOrWhiteSpace(name) || value == null)
        {
            return;
        }

        if (name.Contains('['))
        {
            AddNoteOnce("Indexed <define> lookup tables (e.g. libAlarmes[n]) aren't supported yet.", "define-index");

            return;
        }

        try
        {
            _vars[name] =
                value.StartsWith('\'') && value.EndsWith('\'')
                    ? value[1..^1]
                    : ExpressionEvaluator.EvalNumber(value, _vars);
        }
        catch (Exception ex)
        {
            _notes.Add($"Couldn't evaluate define '{name}' ({value}): {ex.Message}");
        }
    }

    private byte[]
    ReadBytesForBits(
        long bits)
    {
        if (bits % 8 == 0)
        {
            return _reader.ReadRawBytes((int)(bits / 8));
        }

        var byteCount = (int)((bits + 7) / 8);
        var bytes = new byte[byteCount];

        for (var i = 0; i < byteCount; i++)
        {
            var remaining = bits - (long)i * 8;
            var chunk = (int)Math.Min(8, remaining);

            bytes[i] = (byte)(_reader.ReadBits(chunk) << (8 - chunk));
        }

        return bytes;
    }

    private void
    AddNoteOnce(
        string note,
        string? key = null)
    {
        if (!_warnedTags.Add(key ?? note))
        {
            return;
        }

        _notes.Add(note);
    }
}
