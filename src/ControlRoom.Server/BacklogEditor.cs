using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ControlRoom.Server;

/// <summary>A text file split into lines for editing: the lines without their <c>\n</c> (a <c>\r</c> stays part of
/// its line), whether the file ends with a newline and whether it starts with a UTF-8 BOM. Turning it back into
/// bytes gives the same bytes for every line that was not changed.</summary>
internal sealed class EditableText
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public EditableText(IReadOnlyList<string> lines, bool finalNewline, bool hasBom)
    {
        Lines = lines;
        FinalNewline = finalNewline;
        HasBom = hasBom;
    }

    public IReadOnlyList<string> Lines { get; }

    public bool FinalNewline { get; }

    public bool HasBom { get; }

    /// <summary>Splits UTF-8 bytes into lines, or returns <c>null</c> when they are not valid UTF-8.</summary>
    public static EditableText? Parse(byte[] bytes)
    {
        bool hasBom = bytes.AsSpan().StartsWith(Bom);
        string text;
        try
        {
            text = StrictUtf8.GetString(hasBom ? bytes[Bom.Length..] : bytes);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (text.Length == 0)
        {
            return new EditableText([], true, hasBom);
        }

        bool finalNewline = text.EndsWith('\n');
        string body = finalNewline ? text[..^1] : text;
        return new EditableText(body.Split('\n'), finalNewline, hasBom);
    }

    /// <summary>The same file with other lines.</summary>
    public EditableText With(IReadOnlyList<string> lines)
    {
        return new EditableText(lines, FinalNewline, HasBom);
    }

    public string Text => string.Join("\n", Lines) + (FinalNewline && Lines.Count > 0 ? "\n" : "");

    public byte[] ToBytes()
    {
        byte[] text = StrictUtf8.GetBytes(Text);
        return HasBom ? [.. Bom, .. text] : text;
    }

    /// <summary>A line without its trailing <c>\r</c>.</summary>
    public static string Content(string line)
    {
        return line.TrimEnd('\r');
    }
}

/// <summary>One edited backlog: the new text and the subject for the commit message (<c>#N</c>, or
/// <c>line &lt;index+1&gt;</c> for an unnumbered line).</summary>
internal sealed record BacklogEdit(EditableText Text, string Subject);

/// <summary>Where <c>add</c> puts the new line.</summary>
internal enum AddPosition
{
    Top,
    Bottom,
}

/// <summary>Which way <c>up</c>, <c>down</c> and <c>top</c> move a line.</summary>
internal enum MoveDirection
{
    Up,
    Down,
    Top,
}

/// <summary>Pure edits of <c>backlog.md</c> lines and story text. Every method returns <c>null</c> when the change
/// is not allowed (wrong line, wrong mark, a change that changes nothing). Slug, next number, story template and
/// the top and bottom positions follow <c>factory/tools/job.py</c>.</summary>
internal static partial class BacklogEditor
{
    public const int MaxTitleLength = 200;

    public static BacklogEdit? Approve(EditableText text, int line)
    {
        if (!IsMark(text, line, '?'))
        {
            return null;
        }

        string raw = text.Lines[line];
        List<string> lines = [.. text.Lines];
        lines[line] = "- [ ] " + raw[6..];
        return new BacklogEdit(text.With(lines), Subject(text, line));
    }

    /// <summary>Removes a <c>[?]</c> line; the caller removes its story file.</summary>
    public static BacklogEdit? Drop(EditableText text, int line)
    {
        if (!IsMark(text, line, '?'))
        {
            return null;
        }

        List<string> lines = [.. text.Lines];
        lines.RemoveAt(line);
        return new BacklogEdit(text.With(lines), Subject(text, line));
    }

    /// <summary>Moves an open line: <c>Up</c> and <c>Down</c> swap it with the nearest item line above or below,
    /// <c>Top</c> puts it before the first item that is not done. Prose lines stay where they are.</summary>
    public static BacklogEdit? Move(EditableText text, int line, MoveDirection direction)
    {
        if (!IsOpenItem(text, line))
        {
            return null;
        }

        List<string> lines = [.. text.Lines];
        if (direction == MoveDirection.Top)
        {
            int first = FirstOpenItem(text);
            if (first < 0 || first >= line)
            {
                return null;
            }

            string moved = lines[line];
            lines.RemoveAt(line);
            lines.Insert(first, moved);
            return new BacklogEdit(text.With(lines), Subject(text, line));
        }

        int step = direction == MoveDirection.Up ? -1 : 1;
        int other = line + step;
        while (other >= 0 && other < lines.Count && !BacklogParser.IsItem(EditableText.Content(lines[other])))
        {
            other += step;
        }

        if (other < 0 || other >= lines.Count)
        {
            return null;
        }

        (lines[line], lines[other]) = (lines[other], lines[line]);
        return new BacklogEdit(text.With(lines), Subject(text, line));
    }

    /// <summary>Replaces the title of an open numbered line, keeping the link and the tail. <paramref name="title"/>
    /// must already be a clean title (<see cref="CleanTitle"/>).</summary>
    public static BacklogEdit? Title(EditableText text, int line, string title)
    {
        if (!IsOpenItem(text, line) || NumberedEntry(text, line) is null)
        {
            return null;
        }

        string raw = text.Lines[line];
        string content = EditableText.Content(raw);
        string? replaced = BacklogParser.ReplaceTitle(content[6..], LineTitle(title));
        if (replaced is null)
        {
            return null;
        }

        string next = content[..6] + replaced + raw[content.Length..];
        if (next == raw)
        {
            return null;
        }

        List<string> lines = [.. text.Lines];
        lines[line] = next;
        return new BacklogEdit(text.With(lines), Subject(text, line));
    }

    /// <summary>Inserts <c>- [ ] [#N](backlog/NNN-slug.md) Title</c>: <c>Top</c> before the first item that is not
    /// done, <c>Bottom</c> after the last item line (at the end when there is none).</summary>
    public static BacklogEdit Add(EditableText text, int number, string title, AddPosition where)
    {
        List<int> items = ItemRows(text);
        int end = items.Count > 0 ? items[^1] + 1 : text.Lines.Count;
        int at = where == AddPosition.Top ? FirstOpenItem(text) : end;
        if (at < 0)
        {
            at = end;
        }

        string ending = text.Lines.Count > 0 && text.Lines[0].EndsWith('\r') ? "\r" : "";
        List<string> lines = [.. text.Lines];
        lines.Insert(at, "- [ ] [#" + number.ToString(CultureInfo.InvariantCulture) + "](" + StoryPath(number, title)
            + ") " + LineTitle(title) + ending);
        return new BacklogEdit(text.With(lines), "#" + number.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The numbered entry of a line, or <c>null</c> for any other line.</summary>
    public static BacklogEntry? NumberedEntry(EditableText text, int line)
    {
        if (line < 0 || line >= text.Lines.Count)
        {
            return null;
        }

        string content = EditableText.Content(text.Lines[line]);
        if (!BacklogParser.IsItem(content))
        {
            return null;
        }

        string mark = content[3] == 'X' ? "x" : content[3].ToString();
        BacklogEntry entry = BacklogParser.ToEntry(content[6..], mark) with { Line = line };
        return entry.Number is null ? null : entry;
    }

    /// <summary>True for an item line that is not done (<c>[ ]</c>, <c>[?]</c> or <c>[h]</c>).</summary>
    public static bool IsOpenItem(EditableText text, int line)
    {
        if (line < 0 || line >= text.Lines.Count)
        {
            return false;
        }

        string content = EditableText.Content(text.Lines[line]);
        return BacklogParser.IsItem(content) && content[3] != 'x' && content[3] != 'X';
    }

    /// <summary>The highest number among the numbered lines and the <c>backlog/</c> names (<c>NNN-…</c>), plus
    /// 1.</summary>
    public static int NextNumber(EditableText text, IEnumerable<string> storyFolderNames)
    {
        int highest = 0;
        foreach (string line in text.Lines)
        {
            string content = EditableText.Content(line);
            if (BacklogParser.IsItem(content))
            {
                Match match = LineNumber().Match(content[6..]);
                if (match.Success && BacklogParser.TryParseNumber(match.Groups["number"].Value, out int number))
                {
                    highest = Math.Max(highest, number);
                }
            }
        }

        foreach (string name in storyFolderNames)
        {
            Match match = FileNumber().Match(name);
            if (match.Success && BacklogParser.TryParseNumber(match.Groups["number"].Value, out int number))
            {
                highest = Math.Max(highest, number);
            }
        }

        return highest + 1;
    }

    /// <summary>Like <c>job.py slugify</c>: lowercase, runs of anything but <c>a-z0-9</c> become one separator,
    /// the first six words joined by <c>-</c>, cut to 50 characters, <c>-</c> trimmed, <c>item</c> when
    /// empty.</summary>
    public static string Slugify(string title)
    {
        string[] words = NotSlug().Replace(title.ToLowerInvariant(), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string slug = string.Join("-", words.Take(6));
        if (slug.Length > 50)
        {
            slug = slug[..50];
        }

        slug = slug.Trim('-');
        return slug.Length == 0 ? "item" : slug;
    }

    /// <summary><c>backlog/NNN-slug.md</c>.</summary>
    public static string StoryPath(int number, string title)
    {
        return StoryReader.FolderName + "/" + number.ToString("D3", CultureInfo.InvariantCulture) + "-"
            + Slugify(title) + ".md";
    }

    /// <summary>Like <c>job.py render_story</c> for <c>{title, story?, what, why}</c>.</summary>
    public static string RenderStory(int number, string title, string? story, string what, string why)
    {
        List<string> lines = ["# " + number.ToString("D3", CultureInfo.InvariantCulture) + " " + title.Trim(), ""];
        if (!string.IsNullOrEmpty(story))
        {
            lines.AddRange([story.Trim(), ""]);
        }

        lines.AddRange(["## What", "", what.Trim(), "", "## Why", "", why.Trim(), ""]);
        return string.Join("\n", lines);
    }

    /// <summary>The story text with its first line changed from <c># NNN old</c> to <c># NNN new</c>, or
    /// <c>null</c> when the first line is not exactly the old heading.</summary>
    public static string? RetitleStory(string story, int number, string oldTitle, string newTitle)
    {
        string prefix = "# " + number.ToString("D3", CultureInfo.InvariantCulture) + " ";
        int end = story.IndexOf('\n', StringComparison.Ordinal);
        string first = end < 0 ? story : story[..end];
        if (first != prefix + oldTitle)
        {
            return null;
        }

        return prefix + LineTitle(newTitle) + (end < 0 ? "" : story[end..]);
    }

    /// <summary>The trimmed title, or <c>null</c> unless it has 1 to 200 characters, is one line, has no control
    /// characters, no <c>→</c> and no <c>(after:</c>.</summary>
    public static string? CleanTitle(string? title)
    {
        if (title is null)
        {
            return null;
        }

        string trimmed = title.Trim();
        if (trimmed.Length == 0 || !IsValidUnicode(trimmed))
        {
            return null;
        }

        foreach (char c in trimmed)
        {
            if (char.IsControl(c) || c == '\u2028' || c == '\u2029')
            {
                return null;
            }
        }

        if (trimmed.Contains('→', StringComparison.Ordinal)
            || trimmed.Contains("(after:", StringComparison.Ordinal)
            || trimmed.EnumerateRunes().Count() > MaxTitleLength)
        {
            return null;
        }

        return trimmed;
    }

    /// <summary>Story text from a request: valid Unicode, no control characters but <c>\n</c> and <c>\t</c>
    /// (so LF line endings only).</summary>
    public static bool IsStoryText(string text)
    {
        if (!IsValidUnicode(text))
        {
            return false;
        }

        foreach (char c in text)
        {
            if (char.IsControl(c) && c != '\n' && c != '\t')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The title as written on a backlog line: whitespace runs become one space (like
    /// <c>' '.join(title.split())</c>).</summary>
    public static string LineTitle(string title)
    {
        return Whitespace().Replace(title.Trim(), " ");
    }

    private static bool IsValidUnicode(string text)
    {
        try
        {
            _ = new UTF8Encoding(false, true).GetByteCount(text);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsMark(EditableText text, int line, char mark)
    {
        if (line < 0 || line >= text.Lines.Count)
        {
            return false;
        }

        string content = EditableText.Content(text.Lines[line]);
        return BacklogParser.IsItem(content) && content[3] == mark;
    }

    private static List<int> ItemRows(EditableText text)
    {
        List<int> rows = [];
        for (int i = 0; i < text.Lines.Count; i++)
        {
            if (BacklogParser.IsItem(EditableText.Content(text.Lines[i])))
            {
                rows.Add(i);
            }
        }

        return rows;
    }

    private static int FirstOpenItem(EditableText text)
    {
        for (int i = 0; i < text.Lines.Count; i++)
        {
            if (IsOpenItem(text, i))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Subject(EditableText text, int line)
    {
        BacklogEntry? entry = NumberedEntry(text, line);
        return entry?.Number is int number
            ? "#" + number.ToString(CultureInfo.InvariantCulture)
            : "line " + (line + 1).ToString(CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"\A\[#(?<number>[0-9]{1,9})\]\([^)\s]+\)", RegexOptions.CultureInvariant)]
    private static partial Regex LineNumber();

    [GeneratedRegex(@"\A(?<number>[0-9]{1,9})-", RegexOptions.CultureInvariant)]
    private static partial Regex FileNumber();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NotSlug();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
