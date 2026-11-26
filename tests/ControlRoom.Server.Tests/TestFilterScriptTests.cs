using System.Runtime.Versioning;

namespace ControlRoom.Server.Tests;

/// <summary><c>.github/scripts/test-filter.sh</c>, run with <c>/bin/sh</c> on lists written into a per-test temp
/// folder: the part 1 and part 2 filter expressions, and every refused input.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class TestFilterScriptTests
{
    [Theory]
    [InlineData("A\n", "FullyQualifiedName~A", "FullyQualifiedName!~A")]
    [InlineData(
        "# The slow classes\n\nControlRoom.Server.Tests.PublishTests\n   \n\t\nRoomKit.Tests.Sample_Room\n#ControlRoom.Server.Tests.Old\nZ9\n",
        "FullyQualifiedName~ControlRoom.Server.Tests.PublishTests|FullyQualifiedName~RoomKit.Tests.Sample_Room|FullyQualifiedName~Z9",
        "FullyQualifiedName!~ControlRoom.Server.Tests.PublishTests&FullyQualifiedName!~RoomKit.Tests.Sample_Room&FullyQualifiedName!~Z9")]
    [InlineData("_B.c\nA.B", "FullyQualifiedName~_B.c|FullyQualifiedName~A.B", "FullyQualifiedName!~_B.c&FullyQualifiedName!~A.B")]
    public void List_PrintsBothPartsWithTheSameNamesInTheSameOrder(string list, string part1, string part2)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WithList(list, path =>
        {
            Assert.Equal((0, part1 + "\n", ""), TestFilterScript.Run(path, "1"));
            Assert.Equal((0, part2 + "\n", ""), TestFilterScript.Run(path, "2"));
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("12")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("01")]
    [InlineData("one")]
    [InlineData("$(id)")]
    public void BadPart_FailsWithAMessageAndPrintsNothing(string part)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WithList("A.B\n", path => AssertRefused(TestFilterScript.Run(path, part)));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public void MissingList_FailsWithAMessageAndPrintsNothing(string part)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WithList("A.B\n", path =>
        {
            AssertRefused(TestFilterScript.Run(path + ".missing", part));
            AssertRefused(TestFilterScript.Run(Path.GetDirectoryName(path)!, part));
            AssertRefused(TestFilterScript.Run("", part));
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("# only a comment\n  \n")]
    public void ListWithNoNames_FailsWithAMessageAndPrintsNothing(string list)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WithList(list, path =>
        {
            AssertRefused(TestFilterScript.Run(path, "1"));
            AssertRefused(TestFilterScript.Run(path, "2"));
        });
    }

    [Theory]
    [InlineData("A|B")]
    [InlineData("A&B")]
    [InlineData("!A")]
    [InlineData("A~B")]
    [InlineData("A=B")]
    [InlineData("A(B)")]
    [InlineData("A\\B")]
    [InlineData("A,B")]
    [InlineData("A B")]
    [InlineData(" A")]
    [InlineData("A ")]
    [InlineData("\tA")]
    [InlineData("A+B")]
    [InlineData("1A")]
    [InlineData(".A")]
    [InlineData("A\r")]
    [InlineData("$(id)")]
    [InlineData("A;B")]
    [InlineData("\"A\"")]
    [InlineData("A*")]
    [InlineData("Ä")]
    public void BadName_FailsWithAMessageAndPrintsNothing(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WithList("Good.Name\n" + name + "\nOther\n", path =>
        {
            AssertRefused(TestFilterScript.Run(path, "1"));
            AssertRefused(TestFilterScript.Run(path, "2"));
        });
    }

    private static void AssertRefused((int Code, string Output, string Error) result)
    {
        Assert.NotEqual(0, result.Code);
        Assert.Equal("", result.Output);
        Assert.NotEqual("", result.Error.Trim());
    }

    private static void WithList(string text, Action<string> test)
    {
        string folder = Path.Combine(Path.GetTempPath(), "controlroom-test-list-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "test-part-1.txt");
            File.WriteAllText(path, text);
            test(path);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
