using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;

namespace ControlRoom.Server.Tests;

/// <summary>The guard for the split of the release tests (<c>.github/test-part-1.txt</c>, read by
/// <c>.github/scripts/test-filter.sh</c>): every test of this assembly lands in exactly one of the two parts, and no
/// listed name of this assembly's namespace is stale. Linked into <c>RoomKit.Tests</c>, so each test assembly checks
/// its own tests only.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class TestPartsGuardTests
{
    private static readonly string[] Prefixes = ["ControlRoom.Server.Tests.", "RoomKit.Tests."];

    [Fact]
    public void EveryTest_LandsInExactlyOnePart()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        (string[] part1, string[] part2) = Filters();
        Assert.Equal(part1, part2);

        string[] tests = TestNames();
        Assert.NotEmpty(tests);
        Assert.Contains(typeof(TestPartsGuardTests).FullName + "." + nameof(EveryTest_LandsInExactlyOnePart), tests);
        List<string> wrong = [];
        foreach (string test in tests)
        {
            bool inPart1 = part1.Any(name => test.Contains(name, StringComparison.OrdinalIgnoreCase));
            bool inPart2 = part2.All(name => !test.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (inPart1 == inPart2)
            {
                wrong.Add(test);
            }
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void EveryListedName_BelongsToATestAssemblyAndMatchesATestOfItsOwn()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        (string[] names, _) = Filters();
        Assert.All(names, name => Assert.Contains(Prefixes, prefix => name.StartsWith(prefix, StringComparison.Ordinal)));

        string own = typeof(TestPartsGuardTests).Assembly.GetName().Name + ".";
        string[] tests = TestNames();
        string[] stale = [.. names.Where(name => name.StartsWith(own, StringComparison.Ordinal)
            && !tests.Any(test => test.Contains(name, StringComparison.OrdinalIgnoreCase)))];
        Assert.Empty(stale);
    }

    /// <summary>The names in the two printed filters of the committed list: part 1's <c>|</c>-joined
    /// <c>FullyQualifiedName~</c> terms and part 2's <c>&amp;</c>-joined <c>FullyQualifiedName!~</c> terms.</summary>
    private static (string[] Part1, string[] Part2) Filters()
    {
        string list = Path.Combine(TestFilterScript.RepoFolder(), ".github", "test-part-1.txt");
        (int code1, string out1, string err1) = TestFilterScript.Run(list, "1");
        (int code2, string out2, string err2) = TestFilterScript.Run(list, "2");
        Assert.True(code1 == 0, "part 1: " + err1);
        Assert.True(code2 == 0, "part 2: " + err2);
        return (Terms(out1, '|', "FullyQualifiedName~"), Terms(out2, '&', "FullyQualifiedName!~"));
    }

    private static string[] Terms(string output, char join, string prefix)
    {
        Assert.EndsWith("\n", output, StringComparison.Ordinal);
        string[] terms = output.TrimEnd('\n').Split(join);
        Assert.All(terms, term => Assert.StartsWith(prefix, term, StringComparison.Ordinal));
        string[] names = [.. terms.Select(term => term[prefix.Length..])];
        Assert.All(names, name => Assert.NotEqual("", name));
        return names;
    }

    /// <summary>Every <c>[Fact]</c> or <c>[Theory]</c> of this assembly as the test host names it:
    /// <c>Namespace.Outer+Nested.Method</c>.</summary>
    private static string[] TestNames()
    {
        const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        return [.. typeof(TestPartsGuardTests).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(Methods)
                .Where(method => method.GetCustomAttributes<FactAttribute>(inherit: true).Any())
                .Select(method => type.FullName + "." + method.Name))
            .Distinct(StringComparer.Ordinal)];
    }
}

/// <summary>Runs <c>.github/scripts/test-filter.sh</c> with <c>/bin/sh</c>, a clean environment and its own temp
/// working folder; returns the exit code, stdout and stderr.</summary>
[UnsupportedOSPlatform("windows")]
internal static class TestFilterScript
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(60);

    public static (int Code, string Output, string Error) Run(string list, string part)
    {
        string folder = Path.Combine(Path.GetTempPath(), "controlroom-test-filter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            ProcessStartInfo start = new("/bin/sh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = folder,
            };
            start.ArgumentList.Add(Path.Combine(RepoFolder(), ".github", "scripts", "test-filter.sh"));
            start.ArgumentList.Add(list);
            start.ArgumentList.Add(part);
            start.Environment.Clear();
            start.Environment["PATH"] = "/usr/bin:/bin";

            using Process process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(ProcessTimeout))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("test-filter.sh did not exit within " + ProcessTimeout);
            }

            process.WaitForExit();
            return (process.ExitCode, output.Result, error.Result);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    public static string RepoFolder()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ControlRoom.slnx")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return current.FullName;
    }
}
