using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary><c>.github/workflows/release.yml</c>, README and CLAUDE.md, read as text: the trigger, the job's
/// <c>if:</c>, both permission blocks, the SHA pins, PR fields only through <c>env:</c>, and the release steps in
/// order.</summary>
public sealed partial class ReleaseWorkflowTests
{
    private const string Slug = "nothisisnotdaniel/controlroom";

    [Fact]
    public void Trigger_IsAPushToDevelopOrAClosedPullRequestIntoMain()
    {
        string yaml = Workflow();

        Assert.Equal(
            "on:\n  push:\n    branches:\n      - \"develop\"\n    paths-ignore:\n      - \"backlog.md\"\n"
            + "      - \"backlog/**\"\n  pull_request:\n    types:\n      - \"closed\"\n    branches:\n      - \"main\"\n",
            TopLevelBlock(yaml, "on:"));
        Assert.Single(TopLevelKey().Matches(yaml), match => match.Value == "on:");
        Assert.Single(Regex.Matches(yaml, "paths-ignore:"));
        Assert.DoesNotContain("\n    tags:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("workflow_dispatch", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request_target", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Job_RunsForAPushOrAMergedDevelopPullRequestFromThisRepository()
    {
        string yaml = Workflow();

        string condition = Assert.Single(yaml.Split('\n'), line => line.TrimStart().StartsWith("if:", StringComparison.Ordinal));
        Assert.Equal(
            "if: github.event_name == 'push' || (github.event.pull_request.merged == true "
            + "&& github.event.pull_request.head.ref == 'develop' "
            + "&& github.event.pull_request.head.repo.full_name == github.repository)",
            condition.Trim());
    }

    [Fact]
    public void Permissions_GrantNothingAtTheTopAndExactlyThreeOnTheJob()
    {
        string yaml = Workflow();

        Assert.Equal("permissions: {}\n", TopLevelBlock(yaml, "permissions: {}"));
        Assert.Equal(2, Regex.Matches(yaml, "permissions:").Count);
        int job = yaml.IndexOf("\n    permissions:\n", StringComparison.Ordinal);
        Assert.True(job >= 0);
        string block = yaml[(job + "\n    permissions:\n".Length)..];
        string[] grants = block.Split('\n').TakeWhile(line => line.StartsWith("      ", StringComparison.Ordinal))
            .Select(line => line.Trim()).ToArray();
        Assert.Equal(["contents: write", "id-token: write", "attestations: write"], grants);
    }

    [Fact]
    public void EveryAction_IsPinnedToACommitShaWithItsTag()
    {
        string yaml = Workflow();

        string[] uses = [.. yaml.Split('\n').Where(line => line.Contains("uses:", StringComparison.Ordinal))];
        Assert.Equal(3, uses.Length);
        Assert.All(uses, line => Assert.Matches(PinnedUse(), line));
        Assert.Contains("uses: actions/checkout@", yaml, StringComparison.Ordinal);
        Assert.Contains("uses: actions/attest-build-provenance@", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Checkout_KeepsNoCredentialsAndPullRequestFieldsReachTheShellOnlyThroughEnv()
    {
        string yaml = Workflow();

        Assert.Contains("persist-credentials: false", yaml, StringComparison.Ordinal);
        List<string> runs = RunBlocks(yaml);
        Assert.True(runs.Count >= 9);
        Assert.All(runs, run => Assert.DoesNotContain("${{", run, StringComparison.Ordinal));
        Assert.All(runs, run => Assert.DoesNotContain("github.event", run, StringComparison.Ordinal));
        Assert.Contains("PR_TITLE: ${{ github.event.pull_request.title }}", yaml, StringComparison.Ordinal);
        Assert.Contains("MERGE_SHA: ${{ github.event.pull_request.merge_commit_sha }}", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Job_RunsOnAnArm64MacRunner()
    {
        string yaml = Workflow();

        Assert.Contains("\n    runs-on: macos-15\n", yaml, StringComparison.Ordinal);
        Assert.Contains("if [ \"$(uname -m)\" != \"arm64\" ]; then", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Steps_CheckVersionTestPublishSmokePackSumAttestReleaseAndPruneInOrder()
    {
        string yaml = Workflow();
        string[] steps =
        [
            "if [ \"$(uname -m)\" != \"arm64\" ]; then",
            "if [ \"$GITHUB_EVENT_NAME\" = \"push\" ]; then",
            "if [ \"$GITHUB_REF\" != \"refs/heads/develop\" ]; then",
            "case \"$GITHUB_SHA\" in\n              '' | *[!0-9a-f]*)",
            "if [ \"${#GITHUB_SHA}\" -ne 40 ]; then",
            "if [ \"$(git rev-parse HEAD)\" != \"$GITHUB_SHA\" ]; then",
            "elif [ \"$GITHUB_EVENT_NAME\" = \"pull_request\" ]; then",
            "if [ \"$PR_MERGED\" != \"true\" ]; then",
            "if [ \"$PR_HEAD_REF\" != \"develop\" ]; then",
            "if [ \"$PR_HEAD_REPO\" != \"$GITHUB_REPOSITORY\" ]; then",
            "'' | *[!0-9a-f]*)",
            "if [ \"${#MERGE_SHA}\" -ne 40 ]; then",
            "if [ \"$(git rev-parse HEAD)\" != \"$MERGE_SHA\" ]; then",
            "if ! git merge-base --is-ancestor \"$MERGE_SHA\" origin/main; then",
            "          else\n            echo \"Only a push to develop or a merged release pull request makes a release\" >&2\n            exit 1\n",
            "if [ \"$GITHUB_EVENT_NAME\" = \"push\" ]; then",
            "RELEASE_VERSION=\"$(git tag --list 'v*' | sh .github/scripts/develop-version.sh \"$GITHUB_RUN_NUMBER\")\"",
            "RELEASE_TARGET=\"$GITHUB_SHA\"",
            "RELEASE_VERSION=\"$(git tag --list 'v*' | sh .github/scripts/release-version.sh \"$PR_TITLE\")\"",
            "RELEASE_TARGET=\"$MERGE_SHA\"",
            "echo \"RELEASE_VERSION=$RELEASE_VERSION\" >> \"$GITHUB_ENV\"",
            "echo \"RELEASE_TARGET=$RELEASE_TARGET\" >> \"$GITHUB_ENV\"",
            "run: dotnet test -c Release\n",
            "dotnet publish src/ControlRoom.Server -c Release -r osx-arm64 --self-contained -p:Version=\"$RELEASE_VERSION\"",
            "ACTUAL=\"$(\"$RUNNER_TEMP/publish/ControlRoom.Server\" version)\"",
            "if [ \"$ACTUAL\" != \"ControlRoom $RELEASE_VERSION\" ]; then",
            "NAME=\"controlroom-$RELEASE_VERSION-osx-arm64\"",
            "mv \"$RUNNER_TEMP/publish\" \"$RUNNER_TEMP/pack/$NAME\"",
            "$NAME/wwwroot/index.html",
            "tar -czf \"dist/$NAME.tar.gz\" -C \"$RUNNER_TEMP/pack\" \"$NAME\"",
            "shasum -a 256 \"controlroom-$RELEASE_VERSION-osx-arm64.tar.gz\" > SHA256SUMS",
            "uses: actions/attest-build-provenance@",
            "subject-path: dist/controlroom-${{ env.RELEASE_VERSION }}-osx-arm64.tar.gz",
            "case \"$RELEASE_VERSION\" in\n            *-*) PRERELEASE=\"--prerelease\" ;;",
            "gh release create \"v$RELEASE_VERSION\" \"dist/controlroom-$RELEASE_VERSION-osx-arm64.tar.gz\" dist/SHA256SUMS ",
            "--target \"$RELEASE_TARGET\" $PRERELEASE",
            "- name: Prune old prereleases\n        continue-on-error: true\n",
            "case \"$RELEASE_VERSION\" in\n",
            "*-*)\n              echo \"Version $RELEASE_VERSION is a prerelease; nothing to prune\"\n              exit 0\n",
            "set -o pipefail\n",
            "gh release list --limit 1000 --json tagName,isPrerelease --jq '.[] | select(.isPrerelease) | .tagName' |",
            "sh .github/scripts/old-prereleases.sh \"$RELEASE_VERSION\" |",
            "while IFS= read -r tag; do",
            "gh release delete \"$tag\" --cleanup-tag --yes || echo \"::warning::Could not delete prerelease $tag\"",
        ];

        int last = -1;
        foreach (string step in steps)
        {
            int index = yaml.IndexOf(step, last + 1, StringComparison.Ordinal);
            Assert.True(index > last, "step out of order or missing: " + step);
            last = index;
        }
    }

    [Fact]
    public void GithubEnv_HoldsOnlyReleaseNamesThatNoMsBuildPropertyReads()
    {
        string yaml = Workflow();
        string[] msBuildProperties =
        [
            "Version", "VersionPrefix", "VersionSuffix", "PackageVersion", "AssemblyVersion", "FileVersion",
            "InformationalVersion", "Configuration", "Target",
        ];

        string[] envLines = [.. yaml.Split('\n').Where(line => line.Contains(">> \"$GITHUB_ENV\"", StringComparison.Ordinal))];
        string[] written = [.. envLines.Select(line => GithubEnvWrite().Match(line)).Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)];
        Assert.Equal(envLines.Length, written.Length);
        Assert.Equal(["RELEASE_VERSION", "RELEASE_TARGET"], written);

        List<string> keys = EnvBlockKeys(yaml);
        Assert.Contains("PR_TITLE", keys);
        Assert.Contains("GH_TOKEN", keys);
        Assert.All(written.Concat(keys), name => Assert.DoesNotContain(name, msBuildProperties, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(BareVersionOrTarget().Matches(yaml));

        int version = yaml.IndexOf("\n      - name: Version\n", StringComparison.Ordinal);
        int test = yaml.IndexOf("\n      - name: Test\n", StringComparison.Ordinal);
        int publish = yaml.IndexOf("\n      - name: Publish\n", StringComparison.Ordinal);
        Assert.True(version >= 0 && test > version && publish > test, "Test must come after Version and before Publish");
        string testStep = yaml[(test + "\n      - name: Test\n".Length)..publish];
        Assert.Equal("        run: dotnet test -c Release\n", testStep.TrimEnd('\n') + "\n");
    }

    [Fact]
    public void PushPath_TakesNoPullRequestRefCheckAndTheShaComesFromTheEnvironment()
    {
        string yaml = Workflow();

        Assert.DoesNotContain("github.sha", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("github.ref", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("github.run_number", yaml, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(yaml, "--prerelease"));
        Assert.Single(Regex.Matches(yaml, "gh release create "));
        Assert.Single(Regex.Matches(yaml, "gh release delete "));
        int create = yaml.IndexOf("gh release create ", StringComparison.Ordinal);
        int prune = yaml.IndexOf("- name: Prune old prereleases", StringComparison.Ordinal);
        Assert.True(prune > create);
        Assert.Single(Regex.Matches(yaml, "\n        continue-on-error: true\n"));
    }

    [Fact]
    public void ReadmeAndClaudeMd_DescribeTheReleaseInstallVersionAndAttestation()
    {
        string repo = RepoFolder();
        foreach (string file in new[] { "README.md", "CLAUDE.md" })
        {
            string text = File.ReadAllText(Path.Combine(repo, file));
            Assert.Contains(".github/workflows/release.yml", text, StringComparison.Ordinal);
            Assert.Contains("install --release [vX.Y.Z]", text, StringComparison.Ordinal);
            Assert.Contains("-- version", text, StringComparison.Ordinal);
            Assert.Contains("GET /api/version", text, StringComparison.Ordinal);
            Assert.Contains("gh attestation verify", text, StringComparison.Ordinal);
            Assert.Contains("--repo " + Slug, text, StringComparison.Ordinal);
        }
    }

    private static string RepoFolder()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ControlRoom.slnx")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return current.FullName;
    }

    private static string Workflow()
    {
        return File.ReadAllText(Path.Combine(RepoFolder(), ".github", "workflows", "release.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>The top-level line <paramref name="start"/> and every indented line after it, up to the next
    /// top-level key.</summary>
    private static string TopLevelBlock(string yaml, string start)
    {
        int index = yaml.IndexOf("\n" + start + "\n", StringComparison.Ordinal);
        Assert.True(index >= 0, "missing top-level " + start);
        string rest = yaml[(index + 1)..];
        List<string> lines = [rest.Split('\n')[0]];
        lines.AddRange(rest.Split('\n').Skip(1).TakeWhile(line => line.StartsWith(' ')));
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>The text of every <c>run:</c> script: the inline value, or the indented lines of a block.</summary>
    private static List<string> RunBlocks(string yaml)
    {
        string[] lines = yaml.Split('\n');
        List<string> blocks = [];
        for (int index = 0; index < lines.Length; index++)
        {
            Match match = RunLine().Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            string value = match.Groups["value"].Value;
            if (value != "|")
            {
                blocks.Add(value);
                continue;
            }

            int indent = match.Groups["indent"].Value.Length;
            List<string> body = [];
            while (index + 1 < lines.Length
                   && (lines[index + 1].Trim().Length == 0 || lines[index + 1].Length - lines[index + 1].TrimStart().Length > indent))
            {
                index++;
                body.Add(lines[index]);
            }

            blocks.Add(string.Join("\n", body));
        }

        return blocks;
    }

    /// <summary>Every key of every <c>env:</c> block (job or step): the more indented lines after <c>env:</c>,
    /// comments left out.</summary>
    private static List<string> EnvBlockKeys(string yaml)
    {
        string[] lines = yaml.Split('\n');
        List<string> keys = [];
        for (int index = 0; index < lines.Length; index++)
        {
            if (lines[index].Trim() != "env:")
            {
                continue;
            }

            int indent = lines[index].Length - lines[index].TrimStart().Length;
            while (index + 1 < lines.Length
                   && lines[index + 1].Trim().Length > 0
                   && lines[index + 1].Length - lines[index + 1].TrimStart().Length > indent)
            {
                index++;
                Match key = EnvKey().Match(lines[index]);
                if (key.Success)
                {
                    keys.Add(key.Groups["name"].Value);
                }
            }
        }

        return keys;
    }

    [GeneratedRegex(@"^\s*echo ""(?<name>[A-Za-z_][A-Za-z0-9_]*)=[^""]*"" >> ""\$GITHUB_ENV""$")]
    private static partial Regex GithubEnvWrite();

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*):")]
    private static partial Regex EnvKey();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(\$\{?VERSION\b|\$TARGET\b|env\.VERSION\b)")]
    private static partial Regex BareVersionOrTarget();

    [GeneratedRegex(@"^[a-z]+:", RegexOptions.Multiline)]
    private static partial Regex TopLevelKey();

    [GeneratedRegex(@"^\s*(- )?uses: [a-z0-9-]+/[a-z0-9-]+@[0-9a-f]{40} # v[0-9]+(\.[0-9]+)*$")]
    private static partial Regex PinnedUse();

    [GeneratedRegex(@"^(?<indent>\s*)(- )?run: (?<value>.*)$")]
    private static partial Regex RunLine();
}
