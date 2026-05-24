using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Noxrat.Analyzers;

public sealed class CodegenFormatTemplate
{
    private readonly ImmutableArray<TemplateSegment> segments;

    private CodegenFormatTemplate(ImmutableArray<TemplateSegment> segments)
    {
        this.segments = segments;
    }

    public static bool TryParse(
        string format,
        out CodegenFormatTemplate? template,
        out string error
    )
    {
        format ??= string.Empty;
        var builtSegments = ImmutableArray.CreateBuilder<TemplateSegment>();
        var literal = new StringBuilder();
        var hasToken = false;

        var index = 0;
        while (index < format.Length)
        {
            var ch = format[index];
            if (ch == '\\')
            {
                if (index + 1 >= format.Length)
                {
                    template = null;
                    error = $"Trailing escape sequence at index {index}.";
                    return false;
                }

                literal.Append(format[index + 1]);
                index += 2;
                continue;
            }

            if (ch != '{')
            {
                literal.Append(ch);
                index++;
                continue;
            }

            if (literal.Length > 0)
            {
                builtSegments.Add(TemplateSegment.Literal(literal.ToString()));
                literal.Clear();
            }

            if (!TryParseToken(format, ref index, out var tokenSegment, out error))
            {
                template = null;
                return false;
            }

            builtSegments.Add(tokenSegment);
            hasToken = true;
        }

        if (literal.Length > 0)
            builtSegments.Add(TemplateSegment.Literal(literal.ToString()));

        if (!hasToken)
        {
            template = null;
            error = "Format must contain at least one token: {field} or {field_count}.";
            return false;
        }

        template = new CodegenFormatTemplate(builtSegments.ToImmutable());
        error = string.Empty;
        return true;
    }

    public string Render(IReadOnlyList<string> fieldValues)
    {
        var sb = new StringBuilder();

        foreach (var segment in segments)
        {
            if (segment.kind == SegmentKind.Literal)
            {
                sb.Append(segment.literal);
                continue;
            }

            if (segment.kind == SegmentKind.FieldCount)
            {
                sb.Append(fieldValues.Count);
                continue;
            }

            for (var i = 0; i < fieldValues.Count; i++)
            {
                if (i > 0)
                    sb.Append(segment.separator);
                sb.Append(segment.prefix);
                sb.Append(fieldValues[i]);
                sb.Append(segment.suffix);
            }
        }

        return sb.ToString();
    }

    private static bool TryParseToken(
        string format,
        ref int index,
        out TemplateSegment tokenSegment,
        out string error
    )
    {
        var tokenStart = index;
        var tokenBodyStart = index + 1;

        var inQuoted = false;
        var escaped = false;
        var closingIndex = -1;
        for (var i = tokenBodyStart; i < format.Length; i++)
        {
            var ch = format[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '\'')
            {
                inQuoted = !inQuoted;
                continue;
            }

            if (!inQuoted && ch == '}')
            {
                closingIndex = i;
                break;
            }
        }

        if (closingIndex < 0)
        {
            tokenSegment = default;
            error = $"Unclosed token starting at index {tokenStart}.";
            return false;
        }

        var tokenBody = format.Substring(tokenBodyStart, closingIndex - tokenBodyStart);
        if (!TryParseTokenBody(tokenBody, tokenStart, out tokenSegment, out error))
            return false;

        index = closingIndex + 1;
        return true;
    }

    private static bool TryParseTokenBody(
        string tokenBody,
        int tokenStartIndexInFormat,
        out TemplateSegment segment,
        out string error
    )
    {
        segment = default;
        var cursor = 0;
        SkipWhitespace(tokenBody, ref cursor);

        if (!TryReadIdentifier(tokenBody, ref cursor, out var specifier))
        {
            error = $"Missing token identifier at index {tokenStartIndexInFormat}.";
            return false;
        }

        SkipWhitespace(tokenBody, ref cursor);

        if (string.Equals(specifier, "field_count", StringComparison.Ordinal))
        {
            if (cursor != tokenBody.Length)
            {
                error =
                    $"Token 'field_count' does not allow options at index {tokenStartIndexInFormat}.";
                return false;
            }

            segment = TemplateSegment.FieldCount();
            error = string.Empty;
            return true;
        }

        if (!string.Equals(specifier, "field", StringComparison.Ordinal))
        {
            error = $"Unsupported token '{specifier}' at index {tokenStartIndexInFormat}.";
            return false;
        }

        var separator = ", ";
        var prefix = string.Empty;
        var suffix = string.Empty;

        while (cursor < tokenBody.Length)
        {
            if (tokenBody[cursor] != ',')
            {
                error =
                    $"Expected ',' after token identifier at index {tokenStartIndexInFormat + cursor + 1}.";
                return false;
            }

            cursor++;
            SkipWhitespace(tokenBody, ref cursor);

            if (!TryReadIdentifier(tokenBody, ref cursor, out var optionName))
            {
                error = $"Missing option name at index {tokenStartIndexInFormat + cursor + 1}.";
                return false;
            }

            SkipWhitespace(tokenBody, ref cursor);
            if (cursor >= tokenBody.Length || tokenBody[cursor] != ':')
            {
                error =
                    $"Missing ':' after option '{optionName}' at index {tokenStartIndexInFormat + cursor + 1}.";
                return false;
            }

            cursor++;
            SkipWhitespace(tokenBody, ref cursor);

            if (
                !TryReadOptionValue(tokenBody, ref cursor, out var optionValue, out var optionError)
            )
            {
                error = $"{optionError} (token starts at index {tokenStartIndexInFormat}).";
                return false;
            }

            if (string.Equals(optionName, "separator", StringComparison.Ordinal))
                separator = optionValue;
            else if (string.Equals(optionName, "prefix", StringComparison.Ordinal))
                prefix = optionValue;
            else if (string.Equals(optionName, "suffix", StringComparison.Ordinal))
                suffix = optionValue;
            else
            {
                error =
                    $"Unsupported option '{optionName}' at index {tokenStartIndexInFormat + cursor + 1}.";
                return false;
            }

            SkipWhitespace(tokenBody, ref cursor);
        }

        segment = TemplateSegment.Field(separator, prefix, suffix);
        error = string.Empty;
        return true;
    }

    private static bool TryReadIdentifier(string source, ref int cursor, out string identifier)
    {
        var start = cursor;
        while (cursor < source.Length)
        {
            var ch = source[cursor];
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                cursor++;
                continue;
            }
            break;
        }

        if (cursor == start)
        {
            identifier = string.Empty;
            return false;
        }

        identifier = source.Substring(start, cursor - start);
        return true;
    }

    private static bool TryReadOptionValue(
        string source,
        ref int cursor,
        out string value,
        out string error
    )
    {
        if (cursor >= source.Length)
        {
            value = string.Empty;
            error = $"Missing option value at index {cursor}.";
            return false;
        }

        if (source[cursor] == '\'')
        {
            cursor++;
            var sb = new StringBuilder();
            while (cursor < source.Length)
            {
                var ch = source[cursor];
                if (ch == '\\')
                {
                    if (cursor + 1 >= source.Length)
                    {
                        value = string.Empty;
                        error = $"Trailing escape inside quoted value at index {cursor}.";
                        return false;
                    }

                    var escaped = source[cursor + 1];
                    sb.Append(EscapeCharToValue(escaped));
                    cursor += 2;
                    continue;
                }

                if (ch == '\'')
                {
                    cursor++;
                    value = sb.ToString();
                    error = string.Empty;
                    return true;
                }

                sb.Append(ch);
                cursor++;
            }

            value = string.Empty;
            error = $"Unclosed quoted value at index {cursor}.";
            return false;
        }

        var start = cursor;
        while (cursor < source.Length && source[cursor] != ',')
            cursor++;

        value = source.Substring(start, cursor - start).Trim();
        value = UnescapeBareValue(value);
        error = string.Empty;
        return true;
    }

    private static char EscapeCharToValue(char ch)
    {
        return ch switch
        {
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            '\\' => '\\',
            '\'' => '\'',
            '"' => '"',
            '{' => '{',
            '}' => '}',
            _ => ch,
        };
    }

    private static string UnescapeBareValue(string value)
    {
        if (value.Length == 0 || value.IndexOf('\\') < 0)
            return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '\\' && i + 1 < value.Length)
            {
                i++;
                sb.Append(EscapeCharToValue(value[i]));
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static void SkipWhitespace(string source, ref int cursor)
    {
        while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
            cursor++;
    }

    private enum SegmentKind
    {
        Literal,
        Field,
        FieldCount,
    }

    private readonly struct TemplateSegment
    {
        private TemplateSegment(
            SegmentKind kind,
            string literal,
            string separator,
            string prefix,
            string suffix
        )
        {
            this.kind = kind;
            this.literal = literal;
            this.separator = separator;
            this.prefix = prefix;
            this.suffix = suffix;
        }

        public readonly SegmentKind kind;
        public readonly string literal;
        public readonly string separator;
        public readonly string prefix;
        public readonly string suffix;

        public static TemplateSegment Literal(string value)
        {
            return new TemplateSegment(
                SegmentKind.Literal,
                value,
                string.Empty,
                string.Empty,
                string.Empty
            );
        }

        public static TemplateSegment Field(string separator, string prefix, string suffix)
        {
            return new TemplateSegment(SegmentKind.Field, string.Empty, separator, prefix, suffix);
        }

        public static TemplateSegment FieldCount()
        {
            return new TemplateSegment(
                SegmentKind.FieldCount,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty
            );
        }
    }
}
