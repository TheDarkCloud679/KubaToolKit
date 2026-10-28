using System.IO;
using System.Text;
using System.Xml.Linq;

namespace KubaToolKit.Modules.TableViewer.Decoding;

public static class TableXmlLoader
{
    // Decoding a whole folder means re-resolving the same (often
    // multi-thousand-line) definitions file for every table file picked
    // -- cached per path (invalidated on last-write-time change) so it's
    // actually re-read and retokenized only once per batch, not once per
    // file.
    private static readonly Dictionary<string, (DateTime LastWriteUtc, XElement Root)> RootCache = new();
    private static readonly object CacheLock = new();

    // Finds the <table name="..."> matching a 3-letter trigram inside a
    // definitions file (e.g. tables_ebrio.xml), with its <include>s
    // already spliced in.
    public static XElement?
    FindTable(
        string definitionsFilePath,
        string trigram)
    {
        if (string.IsNullOrWhiteSpace(definitionsFilePath) || !File.Exists(definitionsFilePath))
        {
            return null;
        }

        var root = GetCachedRoot(definitionsFilePath);

        var table =
            root.Elements("table")
                .FirstOrDefault(
                    t => string.Equals((string?)t.Attribute("name"), trigram, StringComparison.OrdinalIgnoreCase));

        if (table == null)
        {
            return null;
        }

        var baseDir = Path.GetDirectoryName(Path.GetFullPath(definitionsFilePath)) ?? ".";

        // Idempotent on an already-resolved table (its <include>s are
        // already spliced in, so this just walks the tree finding none)
        // -- cheap enough not to need its own cache entry.
        ResolveIncludes(table, baseDir);

        return table;
    }

    private static XElement
    GetCachedRoot(
        string definitionsFilePath)
    {
        var fullPath = Path.GetFullPath(definitionsFilePath);
        var lastWriteUtc = File.GetLastWriteTimeUtc(fullPath);

        lock (CacheLock)
        {
            if (RootCache.TryGetValue(fullPath, out var cached) && cached.LastWriteUtc == lastWriteUtc)
            {
                return cached.Root;
            }

            var root = LoadLenient(fullPath);

            RootCache[fullPath] = (lastWriteUtc, root);

            return root;
        }
    }

    private static void
    ResolveIncludes(
        XElement element,
        string baseDir)
    {
        foreach (var include in element.Elements("include").ToList())
        {
            var fileAttr = (string?)include.Attribute("file");

            if (string.IsNullOrWhiteSpace(fileAttr))
            {
                include.Remove();

                continue;
            }

            var includePath = Path.Combine(baseDir, fileAttr);

            if (!File.Exists(includePath))
            {
                include.ReplaceWith(new XComment($"include not found: {fileAttr}"));

                continue;
            }

            var includedRoot = LoadLenient(includePath);

            ResolveIncludes(includedRoot, Path.GetDirectoryName(includePath) ?? baseDir);

            include.ReplaceWith(includedRoot.Elements());
        }

        foreach (var child in element.Elements())
        {
            ResolveIncludes(child, baseDir);
        }
    }

    // Some included fragments are just a flat list of top-level
    // <define>/<field> entries with no single root element, so they
    // aren't well-formed XML on their own -- wrap them before parsing
    // rather than requiring every such fragment to be rewritten.
    private static XElement
    LoadLenient(
        string path)
    {
        var text = XmlTolerance.Clean(File.ReadAllText(path));

        try
        {
            return XElement.Parse(text);
        }
        catch (System.Xml.XmlException)
        {
            return XElement.Parse($"<_root>{text}</_root>");
        }
    }
}

// These definitions files are hand-edited over the years and aren't
// actually well-formed XML -- QTable's own parser is evidently more
// forgiving than .NET's. Real issues seen in the wild: raw '<'/'&'
// inside cond="..." attributes (e.g. cond="structure<0x0501",
// cond="a&&b"), a mismatched closing tag name ("</fi>" for "</if>"), and
// a stray extra quote before a self-close ("size=\"8\"\"/>"). Rather than
// patch each shape of typo as it's hit, this retokenizes every tag from
// scratch: attribute values are read up to their matching quote (so a
// raw '<'/'&' inside one is just data, not a syntax error), a stray
// character between attributes is skipped, and every closing tag is
// rewritten to match whatever's actually open on the stack -- nesting
// from the well-formed opening tags is trusted, not the text of the
// closing tag.
internal static class XmlTolerance
{
    public static string
    Clean(
        string xml)
    {
        var sb = new StringBuilder(xml.Length + 128);
        var stack = new Stack<string>();
        var i = 0;
        var n = xml.Length;

        while (i < n)
        {
            if (xml[i] != '<')
            {
                sb.Append(xml[i]);
                i++;

                continue;
            }

            if (i + 3 < n && xml[i + 1] == '!' && xml[i + 2] == '-' && xml[i + 3] == '-')
            {
                var end = xml.IndexOf("-->", i, StringComparison.Ordinal);

                if (end < 0)
                {
                    sb.Append(xml, i, n - i);

                    break;
                }

                end += 3;
                sb.Append(xml, i, end - i);
                i = end;

                continue;
            }

            if (i + 1 < n && (xml[i + 1] == '?' || xml[i + 1] == '!'))
            {
                var end = xml.IndexOf('>', i);

                if (end < 0)
                {
                    sb.Append(xml, i, n - i);

                    break;
                }

                sb.Append(xml, i, end - i + 1);
                i = end + 1;

                continue;
            }

            i++; // consume '<'

            if (i < n && xml[i] == '/')
            {
                i++;

                var nameStart = i;

                while (i < n && IsNameChar(xml[i]))
                {
                    i++;
                }

                var writtenName = xml[nameStart..i];

                while (i < n && xml[i] != '>')
                {
                    i++;
                }

                if (i < n)
                {
                    i++;
                }

                var correctName = stack.Count > 0 ? stack.Pop() : writtenName;

                sb.Append("</").Append(correctName).Append('>');

                continue;
            }

            var tagNameStart = i;

            while (i < n && IsNameChar(xml[i]))
            {
                i++;
            }

            var tagName = xml[tagNameStart..i];
            var attributes = new List<(string Name, string Value)>();
            var selfClosing = false;

            while (true)
            {
                while (i < n && char.IsWhiteSpace(xml[i]))
                {
                    i++;
                }

                if (i >= n)
                {
                    break;
                }

                if (xml[i] == '/')
                {
                    i++;

                    while (i < n && xml[i] != '>')
                    {
                        i++;
                    }

                    if (i < n)
                    {
                        i++;
                    }

                    selfClosing = true;

                    break;
                }

                if (xml[i] == '>')
                {
                    i++;

                    break;
                }

                if (!IsNameStartChar(xml[i]))
                {
                    // A stray character between attributes (e.g. an extra
                    // quote) -- not part of any valid attribute, skip it.
                    i++;

                    continue;
                }

                var attrNameStart = i;

                while (i < n && IsNameChar(xml[i]))
                {
                    i++;
                }

                var attrName = xml[attrNameStart..i];

                while (i < n && char.IsWhiteSpace(xml[i]))
                {
                    i++;
                }

                var attrValue = "";

                if (i < n && xml[i] == '=')
                {
                    i++;

                    while (i < n && char.IsWhiteSpace(xml[i]))
                    {
                        i++;
                    }

                    if (i < n && (xml[i] == '"' || xml[i] == '\''))
                    {
                        var quote = xml[i];

                        i++;

                        var valueStart = i;

                        while (i < n && xml[i] != quote)
                        {
                            i++;
                        }

                        attrValue = xml[valueStart..Math.Min(i, n)];

                        if (i < n)
                        {
                            i++;
                        }
                    }
                    else
                    {
                        var valueStart = i;

                        while (i < n && !char.IsWhiteSpace(xml[i]) && xml[i] != '>' && xml[i] != '/')
                        {
                            i++;
                        }

                        attrValue = xml[valueStart..i];
                    }
                }

                attributes.Add((attrName, attrValue));
            }

            sb.Append('<').Append(tagName);

            foreach (var (name, value) in attributes)
            {
                sb.Append(' ').Append(name).Append("=\"").Append(EscapeAttributeValue(value)).Append('"');
            }

            if (selfClosing)
            {
                sb.Append("/>");
            }
            else
            {
                sb.Append('>');
                stack.Push(tagName);
            }
        }

        return sb.ToString();
    }

    private static string
    EscapeAttributeValue(
        string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;");

    private static bool
    IsNameStartChar(
        char c) =>
        char.IsLetter(c) || c == '_';

    private static bool
    IsNameChar(
        char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':';
}
