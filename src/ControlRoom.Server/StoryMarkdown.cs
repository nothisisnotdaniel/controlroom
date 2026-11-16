using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ControlRoom.Server;

/// <summary>One inline run of a story block: <c>text</c>, <c>code</c>, <c>strong</c> or <c>em</c>, with plain
/// text only.</summary>
internal sealed record StoryInline(string Kind, string Text);

/// <summary>One block of a story: <c>heading</c> (with <c>Level</c> 1-3 and <c>Inlines</c>), <c>paragraph</c>
/// (<c>Inlines</c>), <c>list</c> (<c>Ordered</c> and <c>Items</c>, each a list of inlines) or <c>code</c>
/// (<c>Text</c>, verbatim). Fields a kind doesn't use are left out of the JSON.</summary>
internal sealed record StoryBlock(
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Level = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Ordered = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<StoryInline>? Inlines = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<IReadOnlyList<StoryInline>>? Items = null);

/// <summary>Turns a story's Markdown into a block tree of plain texts: headings (levels 4-6 shown as 3),
/// paragraphs, flat lists, fenced code, and inline code, strong and em. A link keeps only its text. Raw HTML is
/// never interpreted: it stays literal text. The output holds no markup made by the renderer.</summary>
internal static partial class StoryMarkdown
{
    private const string EscapableCharacters = "\\`*_{}[]()#+-.!<>|~\"'";

    public static IReadOnlyList<StoryBlock> Parse(string markdown)
    {
        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<StoryBlock> blocks = [];
        List<string> paragraph = [];
        List<List<string>>? listItems = null;
        bool listOrdered = false;

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                blocks.Add(new StoryBlock("paragraph", Inlines: ParseInlines(string.Join(" ", paragraph))));
                paragraph.Clear();
            }
        }

        void FlushList()
        {
            if (listItems is not null)
            {
                blocks.Add(new StoryBlock(
                    "list",
                    Ordered: listOrdered,
                    Items: listItems.Select(item => (IReadOnlyList<StoryInline>)ParseInlines(string.Join(" ", item)))
                        .ToList()));
                listItems = null;
            }
        }

        int index = 0;
        while (index < lines.Length)
        {
            string line = lines[index].TrimEnd('\r');
            Match fence = Fence().Match(line);
            if (fence.Success)
            {
                FlushParagraph();
                FlushList();
                string marker = fence.Groups["fence"].Value;
                List<string> code = [];
                index++;
                while (index < lines.Length && !IsClosingFence(lines[index].TrimEnd('\r'), marker))
                {
                    code.Add(lines[index].TrimEnd('\r'));
                    index++;
                }

                blocks.Add(new StoryBlock("code", Text: string.Join("\n", code)));
                index++;
                continue;
            }

            index++;
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                FlushList();
                continue;
            }

            Match heading = Heading().Match(line);
            if (heading.Success)
            {
                FlushParagraph();
                FlushList();
                int level = Math.Min(heading.Groups["hashes"].Value.Length, 3);
                List<StoryInline> inlines = ParseInlines(heading.Groups["text"].Value);
                blocks.Add(new StoryBlock("heading", Level: level, Inlines: inlines));
                continue;
            }

            if (ThematicBreak().IsMatch(line))
            {
                FlushParagraph();
                FlushList();
                continue;
            }

            Match item = ListItem().Match(line);
            if (item.Success)
            {
                FlushParagraph();
                bool ordered = item.Groups["ordered"].Success;
                if (listItems is not null && ordered != listOrdered)
                {
                    FlushList();
                }

                listItems ??= [];
                listOrdered = ordered;
                listItems.Add([item.Groups["text"].Value.Trim()]);
                continue;
            }

            if (listItems is not null)
            {
                listItems[^1].Add(line.Trim());
                continue;
            }

            paragraph.Add(line.Trim());
        }

        FlushParagraph();
        FlushList();
        return blocks;
    }

    /// <summary>Splits text into inline runs; adjacent text runs are merged.</summary>
    public static List<StoryInline> ParseInlines(string text)
    {
        List<StoryInline> inlines = [];
        StringBuilder plain = new();

        void FlushText()
        {
            if (plain.Length > 0)
            {
                inlines.Add(new StoryInline("text", plain.ToString()));
                plain.Clear();
            }
        }

        void AddRuns(IEnumerable<StoryInline> runs)
        {
            foreach (StoryInline run in runs)
            {
                if (run.Kind == "text")
                {
                    plain.Append(run.Text);
                }
                else
                {
                    FlushText();
                    inlines.Add(run);
                }
            }
        }

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && EscapableCharacters.Contains(text[i + 1]))
            {
                plain.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (c == '`')
            {
                int run = CountRun(text, i, '`');
                int close = FindRun(text, i + run, '`', run);
                if (close >= 0)
                {
                    string code = text[(i + run)..close];
                    if (code.Length >= 2 && code[0] == ' ' && code[^1] == ' ' && code.Trim().Length > 0)
                    {
                        code = code[1..^1];
                    }

                    FlushText();
                    inlines.Add(new StoryInline("code", code));
                    i = close + run;
                    continue;
                }

                plain.Append(text, i, run);
                i += run;
                continue;
            }

            if (c == '[' || (c == '!' && i + 1 < text.Length && text[i + 1] == '['))
            {
                int open = c == '!' ? i + 1 : i;
                int closeBracket = FindClosingBracket(text, open);
                if (closeBracket > open && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                {
                    int closeParen = FindClosingParen(text, closeBracket + 1);
                    if (closeParen > 0)
                    {
                        AddRuns(ParseInlines(text[(open + 1)..closeBracket]));
                        i = closeParen + 1;
                        continue;
                    }
                }
            }

            if (c == '*' || c == '_')
            {
                int run = Math.Min(CountRun(text, i, c), 2);
                int close = FindEmphasisClose(text, i, c, run);
                if (close >= 0)
                {
                    string inner = PlainText(ParseInlines(text[(i + run)..close]));
                    FlushText();
                    inlines.Add(new StoryInline(run == 2 ? "strong" : "em", inner));
                    i = close + run;
                    continue;
                }

                int literal = CountRun(text, i, c);
                plain.Append(text, i, literal);
                i += literal;
                continue;
            }

            plain.Append(c);
            i++;
        }

        FlushText();
        return inlines;
    }

    private static string PlainText(IEnumerable<StoryInline> runs)
    {
        return string.Concat(runs.Select(run => run.Text));
    }

    private static bool IsClosingFence(string line, string marker)
    {
        string trimmed = line.Trim();
        return trimmed.Length >= marker.Length && trimmed.All(ch => ch == marker[0]);
    }

    private static int CountRun(string text, int start, char ch)
    {
        int end = start;
        while (end < text.Length && text[end] == ch)
        {
            end++;
        }

        return end - start;
    }

    private static int FindRun(string text, int start, char ch, int length)
    {
        int i = start;
        while (i < text.Length)
        {
            if (text[i] == ch)
            {
                int run = CountRun(text, i, ch);
                if (run == length)
                {
                    return i;
                }

                i += run;
                continue;
            }

            i++;
        }

        return -1;
    }

    private static int FindClosingBracket(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '[')
            {
                depth++;
            }
            else if (text[i] == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>Finds the <c>)</c> that closes the link destination opened at <paramref name="open"/>, allowing
    /// balanced parentheses inside it.</summary>
    private static int FindClosingParen(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>Finds the closing delimiter of an emphasis run: the opener must be followed by a non-space and,
    /// for <c>_</c>, not stand inside a word; the closer must follow a non-space and, for <c>_</c>, not be
    /// followed by a letter or digit.</summary>
    private static int FindEmphasisClose(string text, int start, char ch, int run)
    {
        int contentStart = start + run;
        if (contentStart >= text.Length || char.IsWhiteSpace(text[contentStart]))
        {
            return -1;
        }

        if (ch == '_' && start > 0 && char.IsLetterOrDigit(text[start - 1]))
        {
            return -1;
        }

        string delimiter = new(ch, run);
        int i = contentStart + 1;
        while (i <= text.Length - run)
        {
            if (text[i] == '`')
            {
                int ticks = CountRun(text, i, '`');
                int close = FindRun(text, i + ticks, '`', ticks);
                i = close >= 0 ? close + ticks : i + ticks;
                continue;
            }

            if (string.CompareOrdinal(text, i, delimiter, 0, run) == 0
                && !char.IsWhiteSpace(text[i - 1])
                && (ch != '_' || i + run >= text.Length || !char.IsLetterOrDigit(text[i + run]))
                && (run == 2 || i + 1 >= text.Length || text[i + 1] != ch))
            {
                return i;
            }

            i++;
        }

        return -1;
    }

    [GeneratedRegex(@"\A {0,3}(?<fence>`{3,}|~{3,})", RegexOptions.CultureInvariant)]
    private static partial Regex Fence();

    [GeneratedRegex(@"\A {0,3}(?<hashes>#{1,6})(?:[ \t]+(?<text>.*?))?(?:[ \t]+#+)?[ \t]*\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\A {0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex ThematicBreak();

    [GeneratedRegex(@"\A[ \t]*(?:[-*+]|(?<ordered>[0-9]{1,9})[.)])[ \t]+(?<text>.*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex ListItem();
}
