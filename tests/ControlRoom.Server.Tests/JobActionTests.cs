using System.Text;

namespace ControlRoom.Server.Tests;

/// <summary><see cref="JobAction"/>: the <c>human.json</c> schema, parse and render.</summary>
public sealed class JobActionTests
{
    private const string Time = "2026-03-01T09:30:00Z";

    [Fact]
    public void Render_WritesTheApproveFormWithOneLineBreak()
    {
        byte[] bytes = JobAction.Render(new JobRequest("approve", 4, null, Time));

        Assert.Equal("{\"action\":\"approve\",\"round\":4,\"requested\":\"" + Time + "\"}\n", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Render_WritesTheReworkFormWithTheNoteBeforeTheTime()
    {
        byte[] bytes = JobAction.Render(new JobRequest("rework", 0, "Make it smaller", Time));

        Assert.Equal("{\"action\":\"rework\",\"round\":0,\"note\":\"Make it smaller\",\"requested\":\"" + Time + "\"}\n",
            Encoding.UTF8.GetString(bytes));
    }

    [Theory]
    [InlineData("approve", null)]
    [InlineData("rework", "Plain note")]
    [InlineData("rework", "Quotes \" and \\ backslash, <tags> & ünïcödé 🚗")]
    public void ParseOfRender_RoundTrips(string action, string? note)
    {
        JobRequest request = new(action, 7, note, Time);

        Assert.Equal(request, JobAction.Parse(JobAction.Render(request)));
    }

    [Theory]
    [InlineData("\U0001F697")]
    [InlineData("\u00FC")]
    [InlineData("<")]
    [InlineData("&")]
    [InlineData("\"")]
    [InlineData("\\")]
    public void Render_ANoteOf1000RunesOfAnyKind_FitsIn4KiBAndRoundTrips(string rune)
    {
        string note = string.Concat(Enumerable.Repeat(rune, 1000));
        Assert.Equal(note, BacklogEditor.CleanNote(note));
        JobRequest request = new("rework", int.MaxValue, note, Time);

        byte[] bytes = JobAction.Render(request);

        Assert.True(bytes.Length <= JobAction.MaxFileBytes, "rendered " + bytes.Length + " bytes");
        Assert.Equal(request, JobAction.Parse(bytes));
    }

    [Fact]
    public void Render_WritesNonAsciiAndHtmlCharactersUnescaped()
    {
        byte[] bytes = JobAction.Render(new JobRequest("rework", 1, "<\u00FC> & \U0001F697", Time));

        Assert.Equal("{\"action\":\"rework\",\"round\":1,\"note\":\"<\u00FC> & \U0001F697\",\"requested\":\"" + Time + "\"}\n",
            Encoding.UTF8.GetString(bytes));
    }

    [Theory]
    [InlineData("{\"action\":\"approve\",\"round\":1,\"requested\":\"2026-03-01T09:30:00Z\",\"extra\":1}")]
    [InlineData("{\"action\":\"approve\",\"round\":1,\"note\":\"x\",\"requested\":\"2026-03-01T09:30:00Z\"}")]
    [InlineData("{\"action\":\"rework\",\"round\":1,\"requested\":\"2026-03-01T09:30:00Z\"}")]
    [InlineData("{\"action\":\"approve\",\"round\":1}")]
    [InlineData("{\"action\":\"approve\",\"action\":\"approve\",\"round\":1,\"requested\":\"2026-03-01T09:30:00Z\"}")]
    [InlineData("{\"action\":\"cancel\",\"round\":1,\"requested\":\"2026-03-01T09:30:00Z\"}")]
    [InlineData("{\"action\":\"approve\",\"round\":\"1\",\"requested\":\"2026-03-01T09:30:00Z\"}")]
    [InlineData("{\"action\":\"approve\",\"round\":-1,\"requested\":\"2026-03-01T09:30:00Z\"}")]
    [InlineData("{\"action\":\"approve\",\"round\":1.5,\"requested\":\"2026-03-01T09:30:00Z\"}")]
    [InlineData("{\"action\":1,\"round\":1,\"requested\":\"2026-03-01T09:30:00Z\"}")]
    [InlineData("{\"action\":\"approve\",\"round\":1,\"requested\":\"2026-03-01 09:30:00\"}")]
    [InlineData("{\"action\":\"approve\",\"round\":1,\"requested\":\"2026-13-01T09:30:00Z\"}")]
    [InlineData("{\"action\":\"approve\",\"round\":1,\"requested\":1}")]
    [InlineData("{\"action\":\"rework\",\"round\":1,\"note\":\"  untrimmed\",\"requested\":\"2026-03-01T09:30:00Z\"}")]
    [InlineData("{\"action\":\"rework\",\"round\":1,\"note\":\"\",\"requested\":\"2026-03-01T09:30:00Z\"}")]
    [InlineData("{\"action\":\"rework\",\"round\":1,\"note\":\"Proposed by job 1-a\",\"requested\":\"2026-03-01T09:30:00Z\"}")]
    [InlineData("{\"action\":\"rework\",\"round\":1,\"note\":null,\"requested\":\"2026-03-01T09:30:00Z\"}")]
    [InlineData("[\"approve\"]")]
    [InlineData("not json")]
    [InlineData("")]
    public void Parse_RefusesAnythingButTheExactForms(string text)
    {
        Assert.Null(JobAction.Parse(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void Parse_RefusesMoreThan4KiB()
    {
        string file = "{\"action\":\"approve\",\"round\":1,\"requested\":\"" + Time + "\"}";
        byte[] fits = Encoding.UTF8.GetBytes(file + new string(' ', 4096 - file.Length));
        byte[] tooBig = Encoding.UTF8.GetBytes(file + new string(' ', 4097 - file.Length));

        Assert.NotNull(JobAction.Parse(fits));
        Assert.Null(JobAction.Parse(tooBig));
    }

    [Fact]
    public void Parse_RefusesInvalidUtf8()
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes("{\"action\":\"rework\",\"round\":1,\"note\":\"a"), 0xFF,
            .. Encoding.UTF8.GetBytes("\",\"requested\":\"" + Time + "\"}")];

        Assert.Null(JobAction.Parse(bytes));
    }

    [Fact]
    public void FormatTime_IsUtcWithSeconds()
    {
        Assert.Equal("2026-11-19T21:05:09Z",
            JobAction.FormatTime(new DateTimeOffset(2026, 11, 19, 23, 5, 9, 500, TimeSpan.FromHours(2))));
    }
}
