using System.Diagnostics;
using System.Security.Cryptography;

namespace ControlRoom.Server.Tests;

/// <summary>A temporary factory root with sample projects, deleted on dispose. Tests never use the real
/// factory root.</summary>
internal sealed class TempFactoryRoot : IDisposable
{
    public const string NormalBacklog =
        "# Normal backlog\n"
        + "\n"
        + "Some prose with - [ ] inside.\n"
        + "- [ ] First open: do `this` → 125-x\n"
        + "- [?] Proposal: wait for me\n"
        + "  - [ ] Indented: ignored\n"
        + "- [h] Hand task: for you\n"
        + "- [x] Done one: finished\n"
        + "- [ ] Second open without rest\n"
        + "- [X] Done two: a: b\n"
        + "- [y] Other mark: ignored\n"
        + "-  [ ] Bad spacing: ignored\n"
        + "- [ ]No space: ignored\n"
        + "* [ ] Star: ignored\n";

    private readonly List<string> lockedFiles = [];

    public TempFactoryRoot()
    {
        string name = "controlroom-tests-" + Guid.NewGuid().ToString("N");
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Creates a project folder with a <c>CLAUDE.md</c> and, when given, a <c>backlog.md</c>.</summary>
    public string AddProject(string name, string? backlog)
    {
        string folder = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(System.IO.Path.Combine(folder, "CLAUDE.md"), "# " + name + "\n");
        if (backlog is not null)
        {
            File.WriteAllText(System.IO.Path.Combine(folder, "backlog.md"), backlog);
        }

        return folder;
    }

    /// <summary>Writes a story file into the project folder's <c>backlog</c> folder (created when missing) and
    /// returns its path.</summary>
    public static string AddStory(string projectFolder, string fileName, string content)
    {
        string folder = System.IO.Path.Combine(projectFolder, "backlog");
        Directory.CreateDirectory(folder);
        string file = System.IO.Path.Combine(folder, fileName);
        File.WriteAllText(file, content);
        return file;
    }

    /// <summary>The full sample set from the spec's test plan.</summary>
    public static TempFactoryRoot WithSamples()
    {
        TempFactoryRoot root = new();
        root.AddProject("Normal", NormalBacklog);
        Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "NoClaude"));
        File.WriteAllText(System.IO.Path.Combine(root.Path, "NoClaude", "backlog.md"), "- [ ] Hidden: no\n");
        root.AddProject("factory", "- [ ] Factory: no\n");
        root.AddProject(".hidden", "- [ ] Hidden: no\n");
        root.AddProject("Missing", null);
        root.AddProject("Oversized", new string('a', (int)BacklogReader.MaxBytes) + "\n- [ ] Big: no\n");
        root.AddProject("emptyOne", "# Nothing here\n\nJust prose.\n");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "CLAUDE.md"), "# root file\n");

        string outside = System.IO.Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(System.IO.Path.Combine(outside, "CLAUDE.md"), "# outside\n");
        File.WriteAllText(System.IO.Path.Combine(outside, "backlog.md"), "- [ ] Secret: outside\n");
        Directory.CreateSymbolicLink(System.IO.Path.Combine(root.Path, "LinkedFolder"), outside);

        string linkedBacklog = root.AddProject("LinkedBacklog", null);
        File.CreateSymbolicLink(
            System.IO.Path.Combine(linkedBacklog, "backlog.md"), System.IO.Path.Combine(outside, "backlog.md"));
        return root;
    }

    /// <summary>Makes a file unreadable (Unix only); undone on dispose.</summary>
    public void Lock(string file)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(file, UnixFileMode.None);
        lockedFiles.Add(file);
    }

    /// <summary>Creates a named pipe (FIFO) at the path with <c>mkfifo</c> (Unix only; tests only).</summary>
    public static void MakeFifo(string path)
    {
        using Process process = Process.Start(new ProcessStartInfo("mkfifo") { ArgumentList = { path } })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>Every file and folder under the root with its size, time and content hash.</summary>
    public IReadOnlyList<string> Snapshot()
    {
        List<string> entries = [];
        foreach (string entry in Directory.EnumerateFileSystemEntries(Path, "*", new EnumerationOptions
                 {
                     RecurseSubdirectories = true,
                     AttributesToSkip = 0,
                     IgnoreInaccessible = true,
                 }))
        {
            FileInfo info = new(entry);
            string hash = "";
            if (info.Exists && info.LinkTarget is null && !lockedFiles.Contains(entry))
            {
                hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry)));
            }

            entries.Add(entry + "|" + info.LastWriteTimeUtc.Ticks + "|" + hash);
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    public void Dispose()
    {
        foreach (string file in lockedFiles)
        {
            if (OperatingSystem.IsWindows())
            {
                break;
            }

            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Directory.Delete(Path, recursive: true);
    }
}
