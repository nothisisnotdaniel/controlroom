using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ControlRoom.Server.Tests;

/// <summary><c>.github/workflows/release.yml</c>, README and CLAUDE.md, read as text: the trigger, the version
/// job's <c>if:</c>, the four jobs (<c>version</c>, then <c>windows</c> and <c>macos</c> in parallel, then
/// <c>release</c>), every permission block, the SHA pins, PR fields and hashes only through <c>env:</c>, and the
/// steps of each job in order.</summary>
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

        // The job's if: (indented four spaces); the only other if: is the keychain cleanup's always().
        string condition = Assert.Single(yaml.Split('\n'), line => line.StartsWith("    if:", StringComparison.Ordinal));
        Assert.Equal(
            ["if: always()"],
            yaml.Split('\n').Where(line => line.TrimStart().StartsWith("if:", StringComparison.Ordinal)
                && !line.StartsWith("    if:", StringComparison.Ordinal)).Select(line => line.Trim()));
        Assert.Equal(
            "if: github.event_name == 'push' || (github.event.pull_request.merged == true "
            + "&& github.event.pull_request.head.ref == 'develop' "
            + "&& github.event.pull_request.head.repo.full_name == github.repository)",
            condition.Trim());
    }

    [Fact]
    public void Permissions_GrantOnlyTheReleaseJobWriteAccess()
    {
        string yaml = Workflow();

        Assert.Equal("permissions: {}\n", TopLevelBlock(yaml, "permissions: {}"));
        Assert.Equal(5, Regex.Matches(yaml, "permissions:").Count);
        foreach (string job in Jobs)
        {
            string[] lines = yaml.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
                .SkipWhile(line => line != "  " + job + ":").Skip(1)
                .TakeWhile(line => line.StartsWith("    ", StringComparison.Ordinal)).ToArray();
            Assert.Single(lines, line => line == "    permissions:");
            string[] grants = [.. lines.SkipWhile(line => line != "    permissions:").Skip(1)
                .TakeWhile(line => line.StartsWith("      ", StringComparison.Ordinal)).Select(line => line.Trim())];
            Assert.Equal(job == "release" ? ["contents: write", "id-token: write", "attestations: write"] : new[] { "contents: read" }, grants);
        }
    }

    [Fact]
    public void EveryAction_IsPinnedToACommitShaWithItsTag()
    {
        string yaml = Workflow();

        string[] uses = [.. yaml.Split('\n').Where(line => line.Contains("uses:", StringComparison.Ordinal))];
        Assert.Equal(10, uses.Length);
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
            "echo \"RELEASE_VERSION=$RELEASE_VERSION\" >> \"$GITHUB_OUTPUT\"",
            "echo \"RELEASE_TARGET=$RELEASE_TARGET\" >> \"$GITHUB_OUTPUT\"",
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
            "subject-path: |\n            dist/controlroom-${{ env.RELEASE_VERSION }}-osx-arm64.tar.gz",
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
    public void PackRoomKit_ComesAfterTheTestsAndShipsWithEveryRelease()
    {
        string yaml = Workflow();
        string[] names = [.. StepName().Matches(yaml).Select(match => match.Groups["name"].Value)];
        int test = Array.IndexOf(names, "Test");
        int pack = Array.IndexOf(names, "Pack RoomKit");
        Assert.True(test >= 0 && pack > test, "Pack RoomKit must come after Test");
        Assert.True(pack < Array.IndexOf(names, "Checksums"), "Pack RoomKit must come before Checksums");
        Assert.Equal(
            "        run: dotnet pack src/RoomKit -c Release -p:Version=\"$RELEASE_VERSION\" -o dist\n",
            Step(yaml, "Pack RoomKit").TrimEnd('\n') + "\n");

        string sums = Step(yaml, "Checksums");
        Assert.Contains("shasum -a 256 \"RoomKit.$RELEASE_VERSION.nupkg\" >> SHA256SUMS\n", sums, StringComparison.Ordinal);
        Assert.Contains(
            "subject-path: |\n            dist/controlroom-${{ env.RELEASE_VERSION }}-osx-arm64.tar.gz\n            dist/RoomKit.${{ env.RELEASE_VERSION }}.nupkg\n            dist/controlroom-${{ env.RELEASE_VERSION }}-win-x64.zip\n",
            yaml, StringComparison.Ordinal);
        // One gh release create for stable releases and prereleases alike (only $PRERELEASE differs).
        Assert.Single(Regex.Matches(yaml, "gh release create "));
        Assert.Contains(
            "dist/SHA256SUMS \"dist/RoomKit.$RELEASE_VERSION.nupkg\" \"dist/controlroom-$RELEASE_VERSION-win-x64.zip\" --title ", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("packages:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("nuget push", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, Regex.Matches(yaml, "secrets\\.").Count);
        Assert.Equal(10, Regex.Matches(yaml, "\n +uses: ").Count);
    }

    [Fact]
    public void BothReleasePaths_PublishTheTestedBuildsFromTheVersionCommit()
    {
        string yaml = Workflow();
        AssertInOrder(yaml, [
            "  version:\n", "release_target: ${{ steps.version.outputs.RELEASE_TARGET }}",
            "  windows:\n    needs: version\n    runs-on: windows-latest\n",
            "ref: ${{ needs.version.outputs.release_target }}", "run: dotnet test -c Release",
            "run: ./.github/scripts/windows-release.ps1", "uses: actions/upload-artifact@",
            "name: windows-release", "  macos:\n    needs: version\n    runs-on: macos-15\n",
            "ref: ${{ needs.version.outputs.release_target }}", "run: dotnet test -c Release",
            "uses: actions/upload-artifact@", "name: macos-release",
            "  release:\n    needs: [version, windows, macos]\n",
            "ref: ${{ needs.version.outputs.release_target }}",
            "uses: actions/download-artifact@", "pattern: '*-release'", "- name: Check the downloaded files",
            "- name: Checksums", "- name: Attest build provenance", "- name: Create GitHub release",
        ]);
        string release = JobBlock(yaml, "release");
        string sums = Step(release, "Checksums");
        foreach (string name in new[] { "controlroom-$RELEASE_VERSION-win-x64.zip",
                     "controlroom-$RELEASE_VERSION-osx-arm64.tar.gz", "RoomKit.$RELEASE_VERSION.nupkg" })
        {
            Assert.Single(Regex.Matches(sums, Regex.Escape(name)));
            Assert.Single(Regex.Matches(Step(release, "Create GitHub release"), Regex.Escape(name)));
            Assert.Single(Regex.Matches(Step(release, "Attest build provenance"),
                Regex.Escape(name.Replace("$RELEASE_VERSION", "${{ env.RELEASE_VERSION }}", StringComparison.Ordinal))));
        }

        string smoke = File.ReadAllText(Path.Combine(RepoFolder(), ".github", "scripts", "windows-release.ps1"));
        AssertInOrder(smoke, ["-r win-x64 --self-contained", "'controlroom.exe','launch-agent.ps1','updater.ps1','wwwroot'",
            "Copy-Item -LiteralPath", "CreateFromDirectory($package, $zip)", "ExtractToDirectory",
            "& $exe version", "'Factory Root'", "'--FactoryRoot',$root", "GetStringAsync('health')",
            "$health.version -cne $release", "@('/', 'index.html'), @('/app.css', 'app.css')",
            "StatusCode -ne 200", ".Equals($bytes, $expected)", "$server.Kill()"]);
    }

    [Fact]
    public void Pack_CopiesTheUpdaterScriptIntoTheTopFolderBeforeTar()
    {
        string pack = Step(Workflow(), "Pack");
        string copy = "cp scripts/updater.sh \"$RUNNER_TEMP/pack/$NAME/updater.sh\"\n";
        string mode = "chmod 0755 \"$RUNNER_TEMP/pack/$NAME/updater.sh\"\n";
        string tar = "tar -czf \"dist/$NAME.tar.gz\" -C \"$RUNNER_TEMP/pack\" \"$NAME\"";
        int move = pack.IndexOf("mv \"$RUNNER_TEMP/publish\" \"$RUNNER_TEMP/pack/$NAME\"", StringComparison.Ordinal);
        int copied = pack.IndexOf(copy, StringComparison.Ordinal);
        int moded = pack.IndexOf(mode, StringComparison.Ordinal);
        int packed = pack.IndexOf(tar, StringComparison.Ordinal);
        Assert.True(move >= 0 && copied > move && moded > copied && packed > moded,
            "the Pack step must copy scripts/updater.sh (0755) into the top folder after the move and before tar");
        Assert.Single(Regex.Matches(pack, "cp scripts/updater\\.sh "));
    }

    [Fact]
    public void VersionOutputs_HoldOnlyReleaseNamesThatNoMsBuildPropertyReads()
    {
        string yaml = Workflow();
        string[] msBuildProperties =
        [
            "Version", "VersionPrefix", "VersionSuffix", "PackageVersion", "AssemblyVersion", "FileVersion",
            "InformationalVersion", "Configuration", "Target",
        ];

        string[] lines = [.. yaml.Split('\n').Where(line => !line.TrimStart().StartsWith('#'))];
        Assert.DoesNotContain(lines, line => line.Contains("GITHUB_ENV", StringComparison.OrdinalIgnoreCase));
        string[] envLines = [.. lines.Where(line => line.Contains("GITHUB_OUTPUT", StringComparison.OrdinalIgnoreCase))];
        string[] written = [.. envLines.Select(line => GithubOutputWrite().Match(line)).Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)];
        Assert.Equal(envLines.Length, written.Length);
        Assert.Equal(["RELEASE_VERSION", "RELEASE_TARGET", "zip_sha256", "tarball_sha256", "roomkit_sha256"], written);

        List<string> keys = EnvBlockKeys(yaml);
        Assert.Contains("PR_TITLE", keys);
        Assert.Contains("GH_TOKEN", keys);
        Assert.Contains("ZIP_SHA256", keys);
        Assert.Contains("TARBALL_SHA256", keys);
        Assert.Contains("ROOMKIT_SHA256", keys);
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
    public void SigningSteps_ComeBetweenPublishAndTheSmokeCheckWithTheKeychainRemovedAlways()
    {
        string yaml = Workflow();

        string[] names = [.. StepName().Matches(yaml).Select(match => match.Groups["name"].Value)];
        int publish = Array.IndexOf(names, "Publish");
        Assert.True(publish >= 0);
        Assert.Equal(
            ["Publish", "Sign", "Verify signature", "Remove signing keychain", "Smoke check the published server"],
            names[publish..(publish + 5)]);
        Assert.StartsWith("        if: always()\n        run: |\n", Step(yaml, "Remove signing keychain"), StringComparison.Ordinal);
        string remove = Step(yaml, "Remove signing keychain");
        Assert.Contains("if [ -e \"$RUNNER_TEMP/signing.keychain-db\" ]; then", remove, StringComparison.Ordinal);
        Assert.Contains("security delete-keychain \"$RUNNER_TEMP/signing.keychain-db\"", remove, StringComparison.Ordinal);
        Assert.Contains("rm -f \"$RUNNER_TEMP/signing.p12\"", remove, StringComparison.Ordinal);
    }

    [Fact]
    public void Secrets_ReachOnlyTheSignStepThroughItsEnv()
    {
        string yaml = Workflow();

        Assert.Equal(2, Regex.Matches(yaml, Regex.Escape("${{ secrets.")).Count);
        string sign = Step(yaml, "Sign");
        Assert.StartsWith(
            "        env:\n          SIGNING_CERT_P12: ${{ secrets.SIGNING_CERT_P12 }}\n"
            + "          SIGNING_CERT_PASSWORD: ${{ secrets.SIGNING_CERT_PASSWORD }}\n        run: |\n",
            sign, StringComparison.Ordinal);
        Assert.All(RunBlocks(yaml), run => Assert.DoesNotContain("secrets.", run, StringComparison.Ordinal));
        Assert.DoesNotContain("secrets.", Step(yaml, "Test"), StringComparison.Ordinal);
        Assert.DoesNotContain("env:", Step(yaml, "Test"), StringComparison.Ordinal);
        Assert.DoesNotContain("secrets", TopLevelBlock(yaml, "jobs:").Split("    steps:")[0], StringComparison.Ordinal);
        Assert.DoesNotContain("set -x", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("echo \"$SIGNING_CERT", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Sign_WarnsWhenTheFileIsMissingAndFailsWhenItIsInvalidOrASecretIsEmpty()
    {
        string sign = Step(Workflow(), "Sign");
        string[] order =
        [
            "if ! SHA1=\"$(sh .github/scripts/signing-cert.sh signing-cert.sha1)\"; then",
            "exit 1",
            "if [ -z \"$SHA1\" ]; then",
            "echo \"::warning::signing-cert.sha1 is missing, so this build is only ad-hoc signed\"",
            "exit 0",
            "if [ -z \"$SIGNING_CERT_P12\" ] || [ -z \"$SIGNING_CERT_PASSWORD\" ]; then",
            "echo \"signing-cert.sha1 is committed, but the SIGNING_CERT_P12 or SIGNING_CERT_PASSWORD secret is empty\" >&2",
            "exit 1",
            "KEYCHAIN=\"$RUNNER_TEMP/signing.keychain-db\"",
        ];
        AssertInOrder(sign, order);
    }

    [Fact]
    public void Sign_UsesATempKeychainAndSignsTheLibrariesThenTheServerLast()
    {
        string sign = Step(Workflow(), "Sign");
        string[] order =
        [
            "P12=\"$RUNNER_TEMP/signing.p12\"",
            "trap 'rm -f \"$P12\"' EXIT",
            "umask 077",
            "KEYCHAIN_PASSWORD=\"$(openssl rand -hex 32)\"",
            "printf '%s' \"$SIGNING_CERT_P12\" | base64 --decode > \"$P12\"",
            "chmod 600 \"$P12\"",
            "security create-keychain -p \"$KEYCHAIN_PASSWORD\" \"$KEYCHAIN\"",
            "security unlock-keychain -p \"$KEYCHAIN_PASSWORD\" \"$KEYCHAIN\"",
            "security import \"$P12\" -k \"$KEYCHAIN\" -f pkcs12 -P \"$SIGNING_CERT_PASSWORD\" -T /usr/bin/codesign > /dev/null",
            "rm -f \"$P12\"",
            "security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k \"$KEYCHAIN_PASSWORD\" \"$KEYCHAIN\" > /dev/null",
            "for file in \"$PUBLISH\"/*.dylib \"$PUBLISH/createdump\"; do",
            "codesign --force --options runtime --timestamp=none --keychain \"$KEYCHAIN\" --sign \"$SHA1\" \"$file\"",
            "done",
            "codesign --force --options runtime --timestamp=none --keychain \"$KEYCHAIN\" --sign \"$SHA1\" "
                + "--identifier ControlRoom.Server --entitlements .github/signing/entitlements.plist "
                + "-r='designated => identifier \"ControlRoom.Server\" and certificate leaf = H\"'\"$SHA1\"'\"' "
                + "\"$PUBLISH/ControlRoom.Server\"",
        ];
        AssertInOrder(sign, order);
        Assert.Contains("PUBLISH=\"$RUNNER_TEMP/publish\"", sign, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(sign, "codesign --force").Count);
    }

    [Fact]
    public void Sign_PutsTheKeychainOnTheSearchListAndChecksTheIdentityBeforeTheFirstCodesign()
    {
        string sign = Step(Workflow(), "Sign");
        string[] order =
        [
            "security unlock-keychain -p \"$KEYCHAIN_PASSWORD\" \"$KEYCHAIN\"",
            "sh .github/scripts/signing-keychain.sh add \"$KEYCHAIN\" || exit 1\n",
            "security import \"$P12\"",
            "security set-key-partition-list ",
            "if ! sh .github/scripts/signing-keychain.sh check \"$KEYCHAIN\" \"$SHA1\"; then\n",
            "echo \"The SIGNING_CERT_P12 secret holds no code-signing identity with the fingerprint in signing-cert.sha1 "
                + "($SHA1)\" >&2\n",
            "exit 1\n",
            "fi\n",
            "codesign --force",
        ];
        AssertInOrder(sign, order);
        Assert.Single(Regex.Matches(sign, Regex.Escape("signing-keychain.sh add")));
        Assert.Single(Regex.Matches(sign, Regex.Escape("signing-keychain.sh check")));
        Assert.DoesNotContain("signing-keychain.sh remove", sign, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveSigningKeychain_TakesItOffTheSearchListBeforeDeletingItAndOnlyWarnsOnFailure()
    {
        string remove = Step(Workflow(), "Remove signing keychain");
        Assert.StartsWith("        if: always()\n        run: |\n", remove, StringComparison.Ordinal);
        string[] order =
        [
            "if [ -e \"$RUNNER_TEMP/signing.keychain-db\" ]; then\n",
            "sh .github/scripts/signing-keychain.sh remove \"$RUNNER_TEMP/signing.keychain-db\" || echo \"::warning::",
            "security delete-keychain \"$RUNNER_TEMP/signing.keychain-db\"\n",
            "fi\n",
        ];
        AssertInOrder(remove, order);
        Assert.Single(Regex.Matches(remove, Regex.Escape("signing-keychain.sh")));
    }

    [Fact]
    public void SigningKeychainScript_NeverSeesASecretOrPassword()
    {
        string yaml = Workflow();
        string script = File.ReadAllText(Path.Combine(RepoFolder(), ".github", "scripts", "signing-keychain.sh"));

        Assert.DoesNotContain("SIGNING_CERT", script, StringComparison.Ordinal);
        Assert.DoesNotContain("KEYCHAIN_PASSWORD", script, StringComparison.Ordinal);
        Assert.DoesNotContain("set -x", script, StringComparison.Ordinal);
        Assert.DoesNotContain(" -p \"", script, StringComparison.Ordinal);
        string[] calls = [.. yaml.Split('\n').Where(line => line.Contains("sh .github/scripts/signing-keychain.sh ", StringComparison.Ordinal)
                && !line.TrimStart().StartsWith('#'))];
        Assert.Equal(3, calls.Length);
        Assert.All(calls, line =>
        {
            Assert.DoesNotContain("PASSWORD", line, StringComparison.Ordinal);
            Assert.DoesNotContain("SIGNING_CERT_P12\"", line, StringComparison.Ordinal);
            Assert.DoesNotContain("$P12", line, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void VerifySignature_PinsTheCertificateChecksTheRequirementAndRuntimeAndRefusesUnsignedMachO()
    {
        string verify = Step(Workflow(), "Verify signature");
        string[] order =
        [
            "if ! SHA1=\"$(sh .github/scripts/signing-cert.sh signing-cert.sha1)\"; then",
            "exit 1",
            "if [ -z \"$SHA1\" ]; then",
            "exit 0",
            "REQUIREMENT=\"designated => identifier \\\"ControlRoom.Server\\\" and certificate leaf = H\\\"$SHA1\\\"\"",
            "for file in \"$PUBLISH\"/*.dylib \"$PUBLISH/createdump\" \"$PUBLISH/ControlRoom.Server\"; do",
            "codesign --verify --strict -R=\"identifier \\\"ControlRoom.Server\\\" and certificate leaf = H\\\"$SHA1\\\"\" \"$file\"",
            "codesign --verify --strict -R=\"certificate leaf = H\\\"$SHA1\\\"\" \"$file\"",
            "INFO=\"$(codesign -dv \"$file\" 2>&1)\"",
            "grep -Eq '^CodeDirectory .*flags=0x[0-9a-f]+\\([^)]*runtime[^)]*\\)'",
            "DESIGNATED=\"$(codesign -d -r- \"$PUBLISH/ControlRoom.Server\" 2>&1)\"",
            "grep -Fxiq \"$REQUIREMENT\"",
            "find \"$PUBLISH\" -type f > \"$RUNNER_TEMP/publish-files\"",
            "*/*) ;;",
            "ControlRoom.Server | createdump | *.dylib) continue ;;",
            "case \"$(file -b \"$file\")\" in",
            "*Mach-O*)",
            "exit 1",
        ];
        AssertInOrder(verify, order);
        Assert.DoesNotContain("env:", verify, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, 0, "")]
    [InlineData("0123456789abcdef0123456789abcdef01234567\n", 0, "0123456789abcdef0123456789abcdef01234567\n")]
    [InlineData("0123456789ABCDEF0123456789abcdef01234567", 0, "0123456789ABCDEF0123456789abcdef01234567\n")]
    [InlineData("0123456789abcdef0123456789abcdef0123456", 1, "")]
    [InlineData("0123456789abcdef0123456789abcdef012345678", 1, "")]
    [InlineData("0123456789abcdef0123456789abcdef01234567x", 1, "")]
    [InlineData("0123456789abcdef0123456789abcdef01234567\n\n", 1, "")]
    [InlineData("0123456789abcdef0123456789abcdef01234567\r\n", 1, "")]
    [InlineData("0123456789abcdef0123456789abcdef0123456g", 1, "")]
    [InlineData("", 1, "")]
    [InlineData("symlink", 1, "")]
    public async Task FingerprintRule_MissingIsOffValidPrintsAndInvalidFails(string? content, int exit, string output)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = Path.Combine(Path.GetTempPath(), "controlroom-sha1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "signing-cert.sha1");
            if (content == "symlink")
            {
                string target = Path.Combine(folder, "real");
                File.WriteAllText(target, "0123456789abcdef0123456789abcdef01234567\n");
                File.CreateSymbolicLink(file, target);
            }
            else if (content is not null)
            {
                File.WriteAllText(file, content);
            }

            ProcessStartInfo start = new("/bin/sh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(Path.Combine(RepoFolder(), ".github", "scripts", "signing-cert.sh"));
            start.ArgumentList.Add(file);
            using Process process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)), "signing-cert.sh did not exit within 30 s");
            process.WaitForExit();

            Assert.Equal(exit, process.ExitCode);
            Assert.Equal(output, await stdout);
            Assert.Equal(exit == 0 ? "" : file + " must be a regular file holding one 40-character SHA-1 fingerprint\n",
                await stderr);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Entitlements_AreExactlyTheThreeHardenedRuntimeExceptions()
    {
        string path = Path.Combine(RepoFolder(), ".github", "signing", "entitlements.plist");
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Ignore };
        using XmlReader reader = XmlReader.Create(path, settings);
        XDocument document = XDocument.Load(reader);

        Assert.Equal("plist", document.Root!.Name.LocalName);
        XElement dict = Assert.Single(document.Root.Elements());
        Assert.Equal("dict", dict.Name.LocalName);
        XElement[] children = [.. dict.Elements()];
        Assert.Equal(6, children.Length);
        string[] keys = [.. children.Where((_, index) => index % 2 == 0).Select(element =>
        {
            Assert.Equal("key", element.Name.LocalName);
            return element.Value;
        })];
        Assert.All(children.Where((_, index) => index % 2 == 1), element =>
        {
            Assert.Equal("true", element.Name.LocalName);
            Assert.Empty(element.Nodes());
        });
        Assert.Equal(
            ["com.apple.security.cs.allow-jit", "com.apple.security.cs.allow-unsigned-executable-memory",
                "com.apple.security.cs.disable-library-validation"],
            keys);
    }

    [Fact]
    public void Readme_GivesTheOneTimeSigningSetup()
    {
        string text = File.ReadAllText(Path.Combine(RepoFolder(), "README.md"));
        int start = text.IndexOf("\n## Signing\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "README has no ## Signing section");
        int end = text.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        string section = end < 0 ? text[start..] : text[start..end];
        string[] order =
        [
            "Certificate Assistant",
            "`ControlRoom Release`",
            "Self-Signed Root",
            "Code Signing",
            "10 years",
            "p12",
            "gh secret set SIGNING_CERT_P12",
            "gh secret set SIGNING_CERT_PASSWORD",
            "SHA-1",
            "signing-cert.sha1",
            "`develop`",
            "rm ControlRoom-Release.p12",
            "scripts/launch-agent.sh install --release",
            "System Settings > Privacy & Security > Files and Folders",
            "Allow",
        ];
        AssertInOrder(section, order);
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

    [Fact]
    public void Jobs_RunTheWindowsAndMacosBuildsInParallelAfterVersionAndReleaseAfterBoth()
    {
        string yaml = Workflow();

        Assert.Equal(Jobs, JobNames(yaml));
        Assert.Empty(Regex.Matches(JobBlock(yaml, "version"), "\n    needs:"));
        Assert.Equal(["needs: version"], NeedsLines(JobBlock(yaml, "windows")));
        Assert.Equal(["needs: version"], NeedsLines(JobBlock(yaml, "macos")));
        Assert.Equal(["needs: [version, windows, macos]"], NeedsLines(JobBlock(yaml, "release")));
        // windows and macos are needed by release only, and only release reads their outputs.
        string[] needing = [.. Jobs.Where(job => JobBlock(yaml, job).Split('\n')
            .Any(line => line.StartsWith("    needs:", StringComparison.Ordinal)
                && (line.Contains("windows", StringComparison.Ordinal) || line.Contains("macos", StringComparison.Ordinal))))];
        Assert.Equal(["release"], needing);
        Assert.All(Jobs.Where(job => job != "release"), job =>
        {
            Assert.DoesNotContain("needs.windows.", JobBlock(yaml, job), StringComparison.Ordinal);
            Assert.DoesNotContain("needs.macos.", JobBlock(yaml, job), StringComparison.Ordinal);
        });

        // The only job-level if: is the version job's.
        Assert.Single(JobBlock(yaml, "version").Split('\n'), line => line.StartsWith("    if:", StringComparison.Ordinal));
        Assert.All(Jobs.Where(job => job != "version"), job =>
            Assert.DoesNotContain(JobBlock(yaml, job).Split('\n'), line => line.StartsWith("    if:", StringComparison.Ordinal)));
        Assert.Contains("\n    runs-on: ubuntu-latest\n", JobBlock(yaml, "release"), StringComparison.Ordinal);
        Assert.Contains("\n    runs-on: macos-15\n", JobBlock(yaml, "macos"), StringComparison.Ordinal);
    }

    [Fact]
    public void MacosJob_TakesOverTheBuildStepsByteForByteAndReleaseBuildsNothing()
    {
        string yaml = Workflow();
        string macos = JobBlock(yaml, "macos");
        string[] moved =
        [
            "Test", "Publish", "Sign", "Verify signature", "Remove signing keychain", "Smoke check the published server",
            "Pack", "Pack RoomKit", "Hash the macOS files", "Save the verified macOS files",
        ];

        string[] macosNames = StepNames(macos);
        Assert.Equal(
            ["Check out the release commit on macOS", "Set up .NET", .. moved],
            macosNames);
        foreach (string job in Jobs.Where(job => job != "macos"))
        {
            string[] names = StepNames(JobBlock(yaml, job));
            Assert.All(macosNames, name => Assert.DoesNotContain(name, names));
        }

        // Each moved step's body as it was in the old release job (taken from release.yml before #169).
        foreach ((string name, string body) in MovedStepBodies)
        {
            Assert.Equal(body, Step(macos, name));
        }

        // The two long signing steps, by the SHA-256 of their bodies before #169; their lines are checked one by
        // one in the Sign_* and VerifySignature_* tests.
        Assert.Equal("6b2e842d7e0e6389d41eba6cfa887cec6e5449da9678b944e0b9fc08151d455d", Sha256(Step(macos, "Sign")));
        Assert.Equal("2fac1b196861cc5b0a4f42d99e49038ca9438e0231b443cc73ae4ad91f5b5050", Sha256(Step(macos, "Verify signature")));

        string release = JobBlock(yaml, "release");
        foreach (string run in RunBlocks(release))
        {
            string code = string.Join("\n", run.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));
            Assert.DoesNotContain("dotnet", code, StringComparison.Ordinal);
            Assert.DoesNotContain("codesign", code, StringComparison.Ordinal);
            Assert.DoesNotContain("security", code, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("setup-dotnet", release, StringComparison.Ordinal);
    }

    [Fact]
    public void Secrets_StayInTheSignStepOfTheMacosJob()
    {
        string yaml = Workflow();
        string macos = JobBlock(yaml, "macos");

        Assert.Equal(2, Regex.Matches(yaml, Regex.Escape("${{ secrets.")).Count);
        Assert.Equal(2, Regex.Matches(Step(macos, "Sign"), Regex.Escape("${{ secrets.")).Count);
        Assert.All(Jobs.Where(job => job != "macos"), job =>
            Assert.DoesNotContain("secrets.", JobBlock(yaml, job), StringComparison.Ordinal));
        Assert.StartsWith("        if: always()\n", Step(macos, "Remove signing keychain"), StringComparison.Ordinal);
    }

    [Fact]
    public void EachJob_UsesOnlyItsOwnPinnedActions()
    {
        string yaml = Workflow();
        Dictionary<string, string[]> expected = new()
        {
            ["version"] = ["actions/checkout"],
            ["windows"] = ["actions/checkout", "actions/setup-dotnet", "actions/upload-artifact"],
            ["macos"] = ["actions/checkout", "actions/setup-dotnet", "actions/upload-artifact"],
            ["release"] = ["actions/checkout", "actions/download-artifact", "actions/attest-build-provenance"],
        };

        foreach (string job in Jobs)
        {
            string[] uses = [.. JobBlock(yaml, job).Split('\n').Where(line => line.Contains("uses:", StringComparison.Ordinal))];
            Assert.All(uses, line => Assert.Matches(PinnedUse(), line));
            Assert.Equal(expected[job], uses.Select(line => line.Trim().Replace("- ", "", StringComparison.Ordinal)
                .Split('@')[0]["uses: ".Length..]));
        }
    }

    [Fact]
    public void BuildJobs_HashTheExactUploadedFilesAndReportTheHashesAsOutputs()
    {
        string yaml = Workflow();
        string macos = JobBlock(yaml, "macos");
        string windows = JobBlock(yaml, "windows");

        Assert.Contains(
            "    outputs:\n      tarball_sha256: ${{ steps.hash.outputs.tarball_sha256 }}\n"
            + "      roomkit_sha256: ${{ steps.hash.outputs.roomkit_sha256 }}\n", macos, StringComparison.Ordinal);
        string[] names = StepNames(macos);
        Assert.Equal(["Pack RoomKit", "Hash the macOS files", "Save the verified macOS files"], names[^3..]);
        Assert.Equal(
            "        id: hash\n"
            + "        run: |\n"
            + "          set -o pipefail\n"
            + "          TARBALL_SHA256=\"$(shasum -a 256 \"dist/controlroom-$RELEASE_VERSION-osx-arm64.tar.gz\" | cut -d ' ' -f 1)\"\n"
            + "          ROOMKIT_SHA256=\"$(shasum -a 256 \"dist/RoomKit.$RELEASE_VERSION.nupkg\" | cut -d ' ' -f 1)\"\n"
            + "          echo \"tarball_sha256=$TARBALL_SHA256\" >> \"$GITHUB_OUTPUT\"\n"
            + "          echo \"roomkit_sha256=$ROOMKIT_SHA256\" >> \"$GITHUB_OUTPUT\"\n"
            + "\n",
            Step(macos, "Hash the macOS files"));
        Assert.Equal(
            "        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4.6.2\n"
            + "        with:\n"
            + "          name: macos-release\n"
            + "          path: |\n"
            + "            dist/controlroom-${{ env.RELEASE_VERSION }}-osx-arm64.tar.gz\n"
            + "            dist/RoomKit.${{ env.RELEASE_VERSION }}.nupkg\n"
            + "          if-no-files-found: error\n"
            + "          compression-level: 0\n",
            Step(macos, "Save the verified macOS files"));

        Assert.Contains("    outputs:\n      zip_sha256: ${{ steps.hash.outputs.zip_sha256 }}\n", windows, StringComparison.Ordinal);
        Assert.Equal(
            ["Check out the release commit", "Set up .NET on Windows", "Windows tests", "Build and smoke check the Windows ZIP",
                "Hash the verified ZIP", "Save the verified ZIP"],
            StepNames(windows));
        Assert.Equal(
            "        id: hash\n"
            + "        shell: bash\n"
            + "        run: |\n"
            + "          ZIP_SHA256=\"$(sha256sum \"dist/controlroom-$RELEASE_VERSION-win-x64.zip\" | cut -d ' ' -f 1)\"\n"
            + "          echo \"zip_sha256=$ZIP_SHA256\" >> \"$GITHUB_OUTPUT\"\n"
            + "\n",
            Step(windows, "Hash the verified ZIP"));
        Assert.Contains("          path: dist/controlroom-${{ env.RELEASE_VERSION }}-win-x64.zip\n",
            Step(windows, "Save the verified ZIP"), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseJob_DownloadsChecksThenSumsAttestsPublishesAndPrunesLast()
    {
        string release = JobBlock(Workflow(), "release");

        Assert.Equal(
            ["Check out the release commit", "Collect the build artifacts", "Check the downloaded files", "Checksums",
                "Attest build provenance", "Create GitHub release", "Prune old prereleases"],
            StepNames(release));
        Assert.Equal(
            "        uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4.4.0\n"
            + "        with:\n"
            + "          ref: ${{ needs.version.outputs.release_target }}\n"
            + "          persist-credentials: false\n"
            + "\n",
            Step(release, "Check out the release commit"));
        Assert.Equal(
            "        uses: actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093 # v4.3.0\n"
            + "        with:\n"
            + "          pattern: '*-release'\n"
            + "          merge-multiple: true\n"
            + "          path: dist\n"
            + "\n",
            Step(release, "Collect the build artifacts"));
        string check = Step(release, "Check the downloaded files");
        Assert.StartsWith(
            "        env:\n"
            + "          ZIP_SHA256: ${{ needs.windows.outputs.zip_sha256 }}\n"
            + "          TARBALL_SHA256: ${{ needs.macos.outputs.tarball_sha256 }}\n"
            + "          ROOMKIT_SHA256: ${{ needs.macos.outputs.roomkit_sha256 }}\n"
            + "        run: |\n",
            check, StringComparison.Ordinal);
        Assert.DoesNotContain("${{", Assert.Single(RunBlocks(check)), StringComparison.Ordinal);
        Assert.StartsWith("        continue-on-error: true\n", Step(release, "Prune old prereleases"), StringComparison.Ordinal);
        Assert.Contains("sh .github/scripts/old-prereleases.sh \"$RELEASE_VERSION\" |", release, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("match", 0)]
    [InlineData("changed byte", 1)]
    [InlineData("missing file", 1)]
    [InlineData("extra file", 1)]
    [InlineData("extra hidden file", 1)]
    [InlineData("upper-case hash", 1)]
    [InlineData("short hash", 1)]
    [InlineData("empty hash", 1)]
    public async Task CheckTheDownloadedFiles_PassesOnlyForExactlyTheReportedFiles(string change, int exit)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string script = Assert.Single(RunBlocks(Step(JobBlock(Workflow(), "release"), "Check the downloaded files")));
        string folder = Path.Combine(Path.GetTempPath(), "controlroom-check-" + Guid.NewGuid().ToString("N"));
        string dist = Path.Combine(folder, "dist");
        Directory.CreateDirectory(dist);
        try
        {
            string scriptFile = Path.Combine(folder, "check.sh");
            File.WriteAllText(scriptFile, script + "\n");
            Dictionary<string, (string File, string Content)> files = new()
            {
                ["TARBALL_SHA256"] = ("controlroom-1.2.3-develop.4-osx-arm64.tar.gz", "tarball bytes"),
                ["ROOMKIT_SHA256"] = ("RoomKit.1.2.3-develop.4.nupkg", "roomkit bytes"),
                ["ZIP_SHA256"] = ("controlroom-1.2.3-develop.4-win-x64.zip", "zip bytes"),
            };
            Dictionary<string, string> hashes = files.ToDictionary(pair => pair.Key, pair => Sha256(pair.Value.Content));
            foreach ((string file, string content) in files.Values)
            {
                File.WriteAllText(Path.Combine(dist, file), content);
            }

            switch (change)
            {
                case "changed byte":
                    File.WriteAllText(Path.Combine(dist, files["ROOMKIT_SHA256"].File), "roomkit bytez");
                    break;
                case "missing file":
                    File.Delete(Path.Combine(dist, files["ZIP_SHA256"].File));
                    break;
                case "extra file":
                    File.WriteAllText(Path.Combine(dist, "controlroom-1.2.3-develop.4-osx-x64.tar.gz"), "extra");
                    break;
                case "extra hidden file":
                    File.WriteAllText(Path.Combine(dist, ".extra"), "extra");
                    break;
                case "upper-case hash":
                    hashes["TARBALL_SHA256"] = hashes["TARBALL_SHA256"].ToUpperInvariant();
                    break;
                case "short hash":
                    hashes["ZIP_SHA256"] = hashes["ZIP_SHA256"][..63];
                    break;
                case "empty hash":
                    hashes["ROOMKIT_SHA256"] = "";
                    break;
            }

            ProcessStartInfo start = new("/bin/sh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = folder,
            };
            start.ArgumentList.Add(scriptFile);
            start.Environment["RELEASE_VERSION"] = "1.2.3-develop.4";
            foreach ((string name, string hash) in hashes)
            {
                start.Environment[name] = hash;
            }

            using Process process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)), "the check script did not exit within 30 s");
            process.WaitForExit();

            Assert.Equal(exit, process.ExitCode);
            Assert.Equal("", await stdout);
            string errors = await stderr;
            if (exit == 0)
            {
                Assert.Equal("", errors);
            }
            else
            {
                Assert.NotEqual("", errors);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void HeaderReadmeAndClaudeMd_DescribeTheParallelBuildsAndTheHashCheck()
    {
        string repo = RepoFolder();
        string header = string.Join(" ", Workflow().Split('\n').TakeWhile(line => line.StartsWith('#'))
            .Select(line => line.TrimStart('#').Trim()));
        string[] texts =
        [
            Regex.Replace(header, @"\s+", " "),
            Regex.Replace(File.ReadAllText(Path.Combine(repo, "README.md")), @"\s+", " "),
            Regex.Replace(File.ReadAllText(Path.Combine(repo, "CLAUDE.md")), @"\s+", " "),
        ];

        Assert.All(texts, text =>
        {
            Assert.Contains("Windows and macOS builds run in parallel after", text, StringComparison.Ordinal);
            Assert.Contains("checks the downloaded files' hashes against the hashes the build jobs reported", text,
                StringComparison.Ordinal);
            Assert.Contains("then sums, attests and publishes them", text, StringComparison.Ordinal);
        });
    }

    private static readonly string[] Jobs = ["version", "windows", "macos", "release"];

    /// <summary>Each step that moved from the old release job into macos, with its body before #169.</summary>
    private static readonly (string Name, string Body)[] MovedStepBodies =
    [
        ("Check out the release commit on macOS",
            "        uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4.4.0\n"
            + "        with:\n"
            + "          ref: ${{ needs.version.outputs.release_target }}\n"
            + "          persist-credentials: false\n"
            + "\n"),
        ("Set up .NET",
            "        uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4.3.1\n"
            + "        with:\n"
            + "          dotnet-version: \"10.0.x\"\n"
            + "\n"),
        ("Test",
            "        run: dotnet test -c Release\n"
            + "\n"),
        ("Publish",
            "        run: dotnet publish src/ControlRoom.Server -c Release -r osx-arm64 --self-contained -p:Version=\"$RELEASE_VERSION\" -o \"$RUNNER_TEMP/publish\"\n"
            + "\n"),
        ("Remove signing keychain",
            "        if: always()\n"
            + "        run: |\n"
            + "          rm -f \"$RUNNER_TEMP/signing.p12\"\n"
            + "          if [ -e \"$RUNNER_TEMP/signing.keychain-db\" ]; then\n"
            + "            sh .github/scripts/signing-keychain.sh remove \"$RUNNER_TEMP/signing.keychain-db\" || echo \"::warning::Could not take the signing keychain off the keychain search list\"\n"
            + "            security delete-keychain \"$RUNNER_TEMP/signing.keychain-db\"\n"
            + "          fi\n"
            + "\n"),
        ("Smoke check the published server",
            "        run: |\n"
            + "          ACTUAL=\"$(\"$RUNNER_TEMP/publish/ControlRoom.Server\" version)\"\n"
            + "          if [ \"$ACTUAL\" != \"ControlRoom $RELEASE_VERSION\" ]; then\n"
            + "            echo \"ControlRoom.Server version printed '$ACTUAL', expected 'ControlRoom $RELEASE_VERSION'\" >&2\n"
            + "            exit 1\n"
            + "          fi\n"
            + "\n"),
        ("Pack",
            "        env:\n"
            + "          # No ._ AppleDouble entries for extended attributes: the tarball holds only the publish output.\n"
            + "          COPYFILE_DISABLE: \"1\"\n"
            + "        run: |\n"
            + "          NAME=\"controlroom-$RELEASE_VERSION-osx-arm64\"\n"
            + "          mkdir -p \"$RUNNER_TEMP/pack\" dist\n"
            + "          mv \"$RUNNER_TEMP/publish\" \"$RUNNER_TEMP/pack/$NAME\"\n"
            + "          if [ ! -f \"$RUNNER_TEMP/pack/$NAME/wwwroot/index.html\" ]; then\n"
            + "            echo \"The publish output has no wwwroot/index.html\" >&2\n"
            + "            exit 1\n"
            + "          fi\n"
            + "          # The updater installs this copy of itself with the release it swaps in.\n"
            + "          cp scripts/updater.sh \"$RUNNER_TEMP/pack/$NAME/updater.sh\"\n"
            + "          chmod 0755 \"$RUNNER_TEMP/pack/$NAME/updater.sh\"\n"
            + "          tar -czf \"dist/$NAME.tar.gz\" -C \"$RUNNER_TEMP/pack\" \"$NAME\"\n"
            + "\n"),
        ("Pack RoomKit",
            "        run: dotnet pack src/RoomKit -c Release -p:Version=\"$RELEASE_VERSION\" -o dist\n"
            + "\n"),
    ];

    /// <summary>The lines of job <paramref name="job"/>, from its <c>  job:</c> line up to the next job (comment and
    /// blank lines included), each ending in a newline.</summary>
    private static string JobBlock(string yaml, string job)
    {
        string[] lines = JobsLines(yaml);
        int start = Array.IndexOf(lines, "  " + job + ":");
        Assert.True(start >= 0, "missing job " + job);
        IEnumerable<string> block = lines.Skip(start + 1).TakeWhile(line => !JobLine().IsMatch(line));
        return "\n" + string.Join("\n", [lines[start], .. block]).TrimEnd('\n') + "\n";
    }

    private static string[] JobNames(string yaml)
    {
        return [.. JobsLines(yaml).Select(line => JobLine().Match(line))
            .Where(match => match.Success).Select(match => match.Groups["name"].Value)];
    }

    /// <summary>The lines after the top-level <c>jobs:</c> line, up to the next top-level key (blank lines kept).</summary>
    private static string[] JobsLines(string yaml)
    {
        string[] lines = yaml.Split('\n');
        int start = Array.IndexOf(lines, "jobs:");
        Assert.True(start >= 0, "missing top-level jobs:");
        return [.. lines.Skip(start + 1).TakeWhile(line => line.Length == 0 || line.StartsWith(' '))];
    }

    private static string[] NeedsLines(string job)
    {
        return [.. job.Split('\n').Where(line => line.StartsWith("    needs:", StringComparison.Ordinal)).Select(line => line.Trim())];
    }

    private static string[] StepNames(string text)
    {
        return [.. StepName().Matches(text).Select(match => match.Groups["name"].Value)];
    }

    private static string Sha256(string text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static void AssertInOrder(string text, string[] parts)
    {
        int last = -1;
        foreach (string part in parts)
        {
            int index = text.IndexOf(part, last + 1, StringComparison.Ordinal);
            Assert.True(index > last, "missing or out of order: " + part);
            last = index;
        }
    }

    /// <summary>The lines of step <paramref name="name"/> after its <c>- name:</c> line, up to the next step.</summary>
    private static string Step(string yaml, string name)
    {
        string header = "\n      - name: " + name + "\n";
        int start = yaml.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, "missing step " + name);
        start += header.Length;
        int end = yaml.IndexOf("\n      - ", start, StringComparison.Ordinal);
        string body = end < 0 ? yaml[start..] : yaml[start..(end + 1)];
        // Drop the comment lines that introduce the next step.
        return string.Join("\n", body.Split('\n').Where(line => !line.StartsWith("      #", StringComparison.Ordinal)));
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

    [GeneratedRegex(@"^\s*echo ""(?<name>[A-Za-z_][A-Za-z0-9_]*)=[^""]*"" >> ""\$GITHUB_OUTPUT""$")]
    private static partial Regex GithubOutputWrite();

    [GeneratedRegex(@"^  (?<name>[a-z][a-z0-9_-]*):$")]
    private static partial Regex JobLine();

    [GeneratedRegex(@"^      - name: (?<name>.+)$", RegexOptions.Multiline)]
    private static partial Regex StepName();

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
