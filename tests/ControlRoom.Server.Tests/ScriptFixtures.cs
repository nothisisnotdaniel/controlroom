using System.Runtime.Versioning;

namespace ControlRoom.Server.Tests;

/// <summary>Helpers shared by the shell-script tests: the fake <c>codesign</c>, a temp copy of the scripts that
/// <c>install --release</c> runs from (so no test reads the real repo's <c>signing-cert.sha1</c>), and a
/// <c>PATH</c> folder without <c>codesign</c>.</summary>
[UnsupportedOSPlatform("windows")]
internal static class ScriptFixtures
{
    /// <summary>A valid fingerprint (40 lower-case hex characters).</summary>
    public const string Fingerprint = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>Logs its arguments, tab-separated, as one line of <c>$HOME/codesign.log</c>. Exits with the code
    /// in <c>$HOME/codesign-exit</c> (default 0), or 1 when its last argument ends with the text in
    /// <c>$HOME/codesign-fail-for</c>.</summary>
    public const string FakeCodesign =
        "#!/bin/sh\n"
        + "tab=$(printf '\\t')\n"
        + "line=\"\"; last=\"\"\n"
        + "for a in \"$@\"; do if [ -z \"$line\" ]; then line=$a; else line=\"$line$tab$a\"; fi; last=$a; done\n"
        + "printf '%s\\n' \"$line\" >> \"$HOME/codesign.log\"\n"
        + "if [ -f \"$HOME/codesign-fail-for\" ]; then\n"
        + "  case $last in *\"$(cat \"$HOME/codesign-fail-for\")\") exit 1 ;; esac\n"
        + "fi\n"
        + "if [ -f \"$HOME/codesign-exit\" ]; then exit \"$(cat \"$HOME/codesign-exit\")\"; fi\n"
        + "exit 0\n";

    /// <summary>The requirement <c>verify_signature</c> passes for the server executable.</summary>
    public static string ServerRequirement(string fingerprint) =>
        "-R=identifier \"ControlRoom.Server\" and certificate leaf = H\"" + fingerprint + "\"";

    /// <summary>The requirement <c>verify_signature</c> passes for a native library or <c>createdump</c>.</summary>
    public static string LibraryRequirement(string fingerprint) =>
        "-R=certificate leaf = H\"" + fingerprint + "\"";

    /// <summary>Builds <c>&lt;parent&gt;/tree/ControlRoom/</c> with <c>ControlRoom.slnx</c> and copies of the real
    /// <c>scripts/launch-agent.sh</c> and <c>scripts/updater.sh</c>; returns the copied <c>launch-agent.sh</c>. The
    /// factory root the script sees is <c>&lt;parent&gt;/tree</c>.</summary>
    public static string BuildReleaseTree(string parent, string repoFolder)
    {
        string repo = Path.Combine(parent, "tree", "ControlRoom");
        string scripts = Path.Combine(repo, "scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(repo, "ControlRoom.slnx"), "");
        File.Copy(Path.Combine(repoFolder, "scripts", "updater.sh"), Path.Combine(scripts, "updater.sh"));
        string script = Path.Combine(scripts, "launch-agent.sh");
        File.Copy(Path.Combine(repoFolder, "scripts", "launch-agent.sh"), script);
        return script;
    }

    /// <summary>Makes <paramref name="folder"/> hold a symlink to every program in <c>/usr/bin</c>, <c>/bin</c>,
    /// <c>/usr/sbin</c> and <c>/sbin</c> apart from <c>codesign</c>, so it can stand in for those on
    /// <c>PATH</c>.</summary>
    public static void LinkSystemProgramsWithoutCodesign(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (string source in new[] { "/usr/bin", "/bin", "/usr/sbin", "/sbin" })
        {
            if (!Directory.Exists(source))
            {
                continue;
            }

            foreach (string program in Directory.EnumerateFiles(source))
            {
                string name = Path.GetFileName(program);
                string link = Path.Combine(folder, name);
                if (name == "codesign" || File.Exists(link))
                {
                    continue;
                }

                File.CreateSymbolicLink(link, program);
            }
        }
    }
}
