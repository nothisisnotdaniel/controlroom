using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ControlRoom.Server.Tests;

/// <summary>RoomKit comes from its own repo as the pinned package <c>RoomKit</c> (#151): no in-repo copy is left, every
/// <c>RoomKit</c> <c>PackageReference</c> has the same fixed version, <c>nuget.config</c> takes it only from GitHub
/// Packages and holds no credential, and README and CLAUDE.md describe the package and its setup. Text tests on the
/// committed files.</summary>
public sealed partial class RoomKitPinTests
{
    private const string Feed = "https://nuget.pkg.github.com/nothisisnotdaniel/index.json";

    private const string Docs = "https://github.com/nothisisnotdaniel/roomkit/blob/main/docs/roomkit.md";

    [Fact]
    public void Repo_HasNoInRepoCopyOfRoomKit()
    {
        string repo = RepoCopy.RepoFolder();

        Assert.False(Directory.Exists(Path.Combine(repo, "src", "RoomKit")), "src/RoomKit is still in the repo");
        Assert.False(Directory.Exists(Path.Combine(repo, "tests", "RoomKit.Tests")), "tests/RoomKit.Tests is still in the repo");
        Assert.False(File.Exists(Path.Combine(repo, "docs", "roomkit.md")), "docs/roomkit.md is still in the repo");
        foreach (string file in ProjectFiles(repo).Append(Path.Combine(repo, "ControlRoom.slnx")))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("RoomKit.csproj", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("RoomKit.Tests", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("roomkit.md", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(InRepoKitPath(), text);
        }
    }

    [Fact]
    public void Server_PinsExactlyOneFixedRoomKitVersion_AndEveryOtherReferenceUsesIt()
    {
        string repo = RepoCopy.RepoFolder();
        XElement server = Assert.Single(KitReferences(
            Path.Combine(repo, "src", "ControlRoom.Server", "ControlRoom.Server.csproj")));
        string pin = (string?)server.Attribute("Version") ?? "";

        Assert.Equal("0.3.0", pin);
        Assert.Matches(FixedVersion(), pin);
        Assert.Equal("true", (string?)server.Attribute("GeneratePathProperty"));

        List<XElement> all = [.. ProjectFiles(repo).SelectMany(KitReferences)];
        Assert.Contains(all, reference => reference.Document!.BaseUri.Contains("ControlRoom.Server.Tests", StringComparison.Ordinal));
        Assert.All(all, reference =>
        {
            Assert.Equal(pin, (string?)reference.Attribute("Version"));
            Assert.Null(reference.Attribute("VersionOverride"));
            Assert.Empty(reference.Elements());
        });
        Assert.All(ProjectFiles(repo), file =>
            Assert.DoesNotContain("ProjectReference Include=\"..\\RoomKit", File.ReadAllText(file), StringComparison.Ordinal));
    }

    [Fact]
    public void NugetConfig_ClearsSourcesAndMapsRoomKitOnlyToGitHubPackages()
    {
        string path = Path.Combine(RepoCopy.RepoFolder(), "nuget.config");
        XElement config = XDocument.Load(path).Root!;
        XElement sources = Assert.Single(config.Elements("packageSources"));

        Assert.Equal("clear", sources.Elements().First().Name.LocalName);
        Assert.Single(sources.Elements("clear"));
        Assert.Equal(
            [("nuget.org", "https://api.nuget.org/v3/index.json"), ("github", Feed)],
            sources.Elements("add").Select(add => ((string?)add.Attribute("key"), (string?)add.Attribute("value"))));

        XElement mapping = Assert.Single(config.Elements("packageSourceMapping"));
        Dictionary<string, string[]> patterns = mapping.Elements("packageSource").ToDictionary(
            source => (string)source.Attribute("key")!,
            source => source.Elements("package").Select(package => (string)package.Attribute("pattern")!).ToArray());
        Assert.Equal(["RoomKit"], patterns["github"]);
        Assert.Equal(["*"], patterns["nuget.org"]);
        Assert.Equal(2, patterns.Count);
    }

    [Fact]
    public void NugetConfig_HoldsNoCredential()
    {
        string text = File.ReadAllText(Path.Combine(RepoCopy.RepoFolder(), "nuget.config"));

        // The comment may name the env var NuGetPackageSourceCredentials_github, so the section is matched as an element.
        Assert.DoesNotContain("<packageSourceCredentials", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<apikeys", text, StringComparison.OrdinalIgnoreCase);
        foreach (string word in new[] { "ClearTextPassword", "Password", "Username", "apikey" })
        {
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotMatch(TokenLike(), text);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("CLAUDE.md")]
    public void Docs_NameThePackageTheFeedTheSetupAndThePinBump(string file)
    {
        string text = Regex.Replace(File.ReadAllText(Path.Combine(RepoCopy.RepoFolder(), file)), @"\s+", " ");

        Assert.Contains("package `RoomKit`", text, StringComparison.Ordinal);
        Assert.Contains(Feed, text, StringComparison.Ordinal);
        Assert.Contains(Docs, text, StringComparison.Ordinal);
        Assert.Contains("`read:packages`", text, StringComparison.Ordinal);
        Assert.Contains("`NuGetPackageSourceCredentials_github`", text, StringComparison.Ordinal);
        Assert.Contains("never in the repo", text.Replace("Never commit", "never in the repo", StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("change `Version`", text, StringComparison.Ordinal);
        Assert.Contains("both", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("CLAUDE.md")]
    public void Docs_NoLongerDescribeTheInRepoRoomKit(string file)
    {
        string text = Regex.Replace(File.ReadAllText(Path.Combine(RepoCopy.RepoFolder(), file)), @"\s+", " ");

        Assert.DoesNotContain("](docs/roomkit.md)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("`docs/roomkit.md`", text, StringComparison.Ordinal);
        Assert.DoesNotContain("src/RoomKit", text, StringComparison.Ordinal);
        Assert.DoesNotContain("RoomKit.Tests", text, StringComparison.Ordinal);
        Assert.DoesNotContain("nupkg", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pack RoomKit", text, StringComparison.Ordinal);
        Assert.DoesNotContain("packs RoomKit", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("folder feed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("and RoomKit package", text, StringComparison.Ordinal);
        Assert.DoesNotContain("in each test assembly", text, StringComparison.Ordinal);
    }

    private static IEnumerable<string> ProjectFiles(string repo)
    {
        return new[] { "src", "tests" }.SelectMany(folder => Directory.GetFiles(
            Path.Combine(repo, folder), "*.csproj", SearchOption.AllDirectories))
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));
    }

    private static IEnumerable<XElement> KitReferences(string csproj)
    {
        XDocument project = XDocument.Load(csproj, LoadOptions.SetBaseUri);
        return project.Descendants("PackageReference")
            .Where(reference => string.Equals((string?)reference.Attribute("Include"), "RoomKit", StringComparison.OrdinalIgnoreCase)
                || string.Equals((string?)reference.Attribute("Update"), "RoomKit", StringComparison.OrdinalIgnoreCase));
    }

    // X.Y.Z with an optional prerelease: no range, no wildcard, no floating version.
    [GeneratedRegex(@"\A[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?\z")]
    private static partial Regex FixedVersion();

    // A path to the old in-repo library or its tests (forward or back slashes).
    [GeneratedRegex(@"(src|tests)[\\/]RoomKit([\\/.]|\b)", RegexOptions.IgnoreCase)]
    private static partial Regex InRepoKitPath();

    // GitHub tokens (ghp_, gho_, ghs_, github_pat_) or any long base64-like run.
    [GeneratedRegex(@"(gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|[A-Za-z0-9+/=_-]{40,})")]
    private static partial Regex TokenLike();
}
