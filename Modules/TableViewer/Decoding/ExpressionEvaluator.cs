using System.Globalization;

namespace KubaToolKit.Modules.TableViewer.Decoding;

// A small recursive-descent evaluator for the expressions used in the
// table definition XML's "cond", "size" and "oper action" attributes --
// e.g. "(caisse!=totalService)&&(deletingFinService==0)", "param_size*8",
// "statService=SERV_O_COURSE_F". Numbers (including the table's own
// variables) are all evaluated as long; string literals ('...') exist
// only for <define> lookup values.
public static class ExpressionEvaluator
{
    public static long
    EvalNumber(
        string expression,
        IReadOnlyDictionary<string, object> variables)
    {
        var value = new Parser(expression, variables).ParseExpression();

        return ToLong(value);
    }

    public static bool
    EvalCondition(
        string expression,
        IReadOnlyDictionary<string, object> variables) =>
        EvalNumber(expression, variables) != 0;

    // "varName=expr" -- the only assignment form <oper action="..."/> uses.
    public static void
    EvalAssignment(
        string action,
        Dictionary<string, object> variables)
    {
        var eq = action.IndexOf('=');

        if (eq <= 0)
        {
            throw new FormatException($"not an assignment: '{action}'");
        }

        var name = action[..eq].Trim();
        var expr = action[(eq + 1)..];

        variables[name] = new Parser(expr, variables).ParseExpression();
    }

    private static long
    ToLong(
        object value) =>
        value switch
        {
            long l => l,
            string => 0L,
            _ => Convert.ToInt64(value)
        };

    private sealed class Parser
    {
        private readonly string _text;
        private readonly IReadOnlyDictionary<string, object> _vars;
        private int _pos;

        public Parser(string text, IReadOnlyDictionary<string, object> vars)
        {
            _text = text;
            _vars = vars;
        }

        public object
        ParseExpression() =>
            ParseOr();

        private object
        ParseOr()
        {
            var left = ParseAnd();

            while (Match("||"))
            {
                var right = ParseAnd();
                left = (ToLong(left) != 0 || ToLong(right) != 0) ? 1L : 0L;
            }

            return left;
        }

        private object
        ParseAnd()
        {
            var left = ParseEquality();

            while (Match("&&"))
            {
                var right = ParseEquality();
                left = (ToLong(left) != 0 && ToLong(right) != 0) ? 1L : 0L;
            }

            return left;
        }

        private object
        ParseEquality()
        {
            var left = ParseRelational();

            while (true)
            {
                if (Match("=="))
                {
                    left = Equals2(left, ParseRelational()) ? 1L : 0L;
                }
                else if (Match("!="))
                {
                    left = !Equals2(left, ParseRelational()) ? 1L : 0L;
                }
                else
                {
                    return left;
                }
            }
        }

        private static bool
        Equals2(
            object a,
            object b) =>
            a is string || b is string ? Convert.ToString(a) == Convert.ToString(b) : ToLong(a) == ToLong(b);

        private object
        ParseRelational()
        {
            var left = ParseAdditive();

            while (true)
            {
                if (Match("<=")) left = ToLong(left) <= ToLong(ParseAdditive()) ? 1L : 0L;
                else if (Match(">=")) left = ToLong(left) >= ToLong(ParseAdditive()) ? 1L : 0L;
                else if (Match("<")) left = ToLong(left) < ToLong(ParseAdditive()) ? 1L : 0L;
                else if (Match(">")) left = ToLong(left) > ToLong(ParseAdditive()) ? 1L : 0L;
                else return left;
            }
        }

        private object
        ParseAdditive()
        {
            var left = ParseMultiplicative();

            while (true)
            {
                if (Match("+")) left = ToLong(left) + ToLong(ParseMultiplicative());
                else if (Match("-")) left = ToLong(left) - ToLong(ParseMultiplicative());
                else return left;
            }
        }

        private object
        ParseMultiplicative()
        {
            var left = ParseUnary();

            while (true)
            {
                if (Match("*")) left = ToLong(left) * ToLong(ParseUnary());
                else if (Match("/")) left = ToLong(left) / ToLong(ParseUnary());
                else if (Match("%")) left = ToLong(left) % ToLong(ParseUnary());
                else return left;
            }
        }

        private object
        ParseUnary()
        {
            SkipWhitespace();

            if (Match("!"))
            {
                return ToLong(ParseUnary()) == 0 ? 1L : 0L;
            }

            if (Match("-"))
            {
                return -ToLong(ParseUnary());
            }

            return ParsePrimary();
        }

        private object
        ParsePrimary()
        {
            SkipWhitespace();

            if (Match("("))
            {
                var value = ParseExpression();

                SkipWhitespace();

                // A real typo found in the wild -- cond="(structure<0x500"
                // with no closing ')' at all -- would otherwise throw,
                // which skips the whole <if> and misaligns every byte
                // read after it for the rest of the file. QTable's own
                // parser evidently tolerates this, so an unclosed paren
                // right at the end of the expression is treated as
                // implicitly closed rather than rejected; a missing ')'
                // with real content still following it is still an error.
                if (!Match(")") && _pos < _text.Length)
                {
                    throw new FormatException($"expected ')' in '{_text}'");
                }

                return value;
            }

            if (Peek() == '\'')
            {
                return ParseStringLiteral();
            }

            if (char.IsDigit(Peek()))
            {
                return ParseNumberLiteral();
            }

            if (char.IsLetter(Peek()) || Peek() == '_')
            {
                return ParseIdentifier();
            }

            throw new FormatException($"unexpected character '{Peek()}' in '{_text}'");
        }

        private object
        ParseStringLiteral()
        {
            _pos++; // opening '

            var start = _pos;

            while (_pos < _text.Length && _text[_pos] != '\'')
            {
                _pos++;
            }

            var literal = _text[start.._pos];

            if (_pos < _text.Length)
            {
                _pos++; // closing '
            }

            return literal;
        }

        private object
        ParseNumberLiteral()
        {
            var start = _pos;

            if (Peek() == '0' && _pos + 1 < _text.Length && (_text[_pos + 1] == 'x' || _text[_pos + 1] == 'X'))
            {
                _pos += 2;

                var hexStart = _pos;

                while (_pos < _text.Length && Uri.IsHexDigit(_text[_pos]))
                {
                    _pos++;
                }

                return Convert.ToInt64(_text[hexStart.._pos], 16);
            }

            while (_pos < _text.Length && char.IsDigit(_text[_pos]))
            {
                _pos++;
            }

            return long.Parse(_text[start.._pos], CultureInfo.InvariantCulture);
        }

        private object
        ParseIdentifier()
        {
            var start = _pos;

            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_'))
            {
                _pos++;
            }

            var name = _text[start.._pos];

            if (!_vars.TryGetValue(name, out var value))
            {
                throw new FormatException($"undefined variable '{name}' in '{_text}'");
            }

            return value;
        }

        private char
        Peek() =>
            _pos < _text.Length ? _text[_pos] : '\0';

        private void
        SkipWhitespace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            {
                _pos++;
            }
        }

        private bool
        Match(
            string token)
        {
            SkipWhitespace();

            if (_pos + token.Length > _text.Length)
            {
                return false;
            }

            if (string.CompareOrdinal(_text, _pos, token, 0, token.Length) != 0)
            {
                return false;
            }

            _pos += token.Length;

            return true;
        }
    }
}
