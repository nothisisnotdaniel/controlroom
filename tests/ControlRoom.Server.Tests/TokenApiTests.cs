using System.Net;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ControlRoom.Server.Tests;

/// <summary>Every <c>/api/</c> path needs <c>Authorization: Bearer &lt;token&gt;</c>; the app shell stays public.</summary>
public sealed class TokenApiTests
{
    [Theory]
    [InlineData("file")]
    [InlineData("folder")]
    [InlineData("inherited")]
    [InlineData("linked-folder")]
    [InlineData("linked-token")]
    public async Task UnsafeAclOrLink_BlocksStartupAndAnAlreadyAuthorizedClient(string kind)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await CheckUnsafeWindowsToken(kind);
    }

    [ForeignOwnerTheory]
    [InlineData("foreign-file-owner")]
    [InlineData("foreign-folder-owner")]
    public async Task ForeignOwner_BlocksStartupAndAnAlreadyAuthorizedClient(string kind)
    {
        if (OperatingSystem.IsWindows())
        {
            await CheckUnsafeWindowsToken(kind);
        }
    }

    private sealed class ForeignOwnerTheoryAttribute : TheoryAttribute
    {
        public ForeignOwnerTheoryAttribute()
        {
            if (!OperatingSystem.IsWindows())
            {
                Skip = "This fixture assigns an NTFS owner.";
                return;
            }

            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
                && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
            {
                Skip = "Assigning the fixture's Administrators owner requires an owner-enabled group.";
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task CheckUnsafeWindowsToken(string kind)
    {
        using TempFactoryRoot root = new();
        using TempTokenFolder tokens = new();
        SecurityIdentifier others = new(WellKnownSidType.BuiltinUsersSid, null);
        SecurityIdentifier administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        DirectorySecurity shared = new DirectoryInfo(tokens.Path).GetAccessControl();
        shared.AddAccessRule(new FileSystemAccessRule(others, FileSystemRights.Read,
            InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));
        DirectoryInfo parent = new(Path.Combine(tokens.Path, "parent"));
        parent.Create(shared);
        string tokenFile = Path.Combine(parent.FullName, "private", AccessToken.FileName);
        using ServerFactory factory = new(root.Path, tokenFile);
        using HttpClient client = factory.CreateTokenClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/version")).StatusCode);
        FileInfo file = new(tokenFile);
        DirectoryInfo folder = file.Directory!;
        switch (kind)
        {
            case "foreign-file-owner":
                FileSecurity foreignFile = file.GetAccessControl();
                foreignFile.SetOwner(administrators);
                file.SetAccessControl(foreignFile);
                Assert.Equal(administrators, file.GetAccessControl().GetOwner(typeof(SecurityIdentifier)));
                break;
            case "foreign-folder-owner":
                DirectorySecurity foreignFolder = folder.GetAccessControl();
                foreignFolder.SetOwner(administrators);
                folder.SetAccessControl(foreignFolder);
                Assert.Equal(administrators, folder.GetAccessControl().GetOwner(typeof(SecurityIdentifier)));
                break;
            case "file":
                FileSecurity fileAcl = file.GetAccessControl();
                fileAcl.AddAccessRule(new FileSystemAccessRule(others, FileSystemRights.Read, AccessControlType.Allow));
                string savedToken = File.ReadAllText(tokenFile);
                File.Delete(tokenFile);
                using (FileStream replacement = file.Create(FileMode.CreateNew, FileSystemRights.Write,
                           FileShare.None, 4096, FileOptions.None, fileAcl))
                {
                    replacement.Write(System.Text.Encoding.ASCII.GetBytes(savedToken));
                }

                break;
            case "folder":
            case "inherited":
                string previous = Path.Combine(parent.FullName, "previous");
                Directory.Move(folder.FullName, previous);
                if (kind == "folder")
                {
                    folder.Create(shared);
                }
                else
                {
                    folder.Create();
                    Assert.Contains(folder.GetAccessControl().GetAccessRules(false, true, typeof(SecurityIdentifier))
                        .Cast<FileSystemAccessRule>(), rule => rule.IdentityReference == others && rule.IsInherited);
                }

                File.Move(Path.Combine(previous, file.Name), tokenFile);
                break;
            case "linked-folder":
                string moved = Path.Combine(tokens.Path, "real");
                Directory.Move(folder.FullName, moved);
                Directory.CreateSymbolicLink(folder.FullName, moved);
                break;
            case "linked-token":
                string target = Path.Combine(folder.FullName, "real-token");
                File.Move(file.FullName, target);
                File.CreateSymbolicLink(file.FullName, target);
                break;
        }

        Assert.Equal(TokenFileState.Invalid, AccessToken.Read(tokenFile).State);
        StringWriter error = new();
        Assert.Null(ServerApp.Create(["--FactoryRoot", root.Path, "--TokenFile", tokenFile], error));
        if (kind.Contains("folder", StringComparison.Ordinal) || kind == "inherited")
        {
            Assert.Contains("real directory", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("disable inheritance", error.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("token --new", error.ToString(), StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("token --new --TokenFile ", error.ToString(), StringComparison.Ordinal);
            Assert.Contains(tokenFile, error.ToString(), StringComparison.Ordinal);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/version")).StatusCode);
    }

    private static readonly string[] ApiPaths =
    [
        "/api/backlogs", "/api/story?project=Story&number=1", "/api/board", "/api/job?id=1-a", "/api/x", "/API/board",
        "/api",
    ];

    private static readonly string[] ShellPaths =
    [
        "/", "/index.html", "/app.js", "/board.js", "/token.js", "/app.css", "/sw.js", "/manifest.webmanifest",
        "/icons/apple-touch-icon.png", "/icons/icon-192.png", "/icons/icon-512.png",
    ];

    public static TheoryData<string, string> RefusedRequests()
    {
        TheoryData<string, string> data = [];
        foreach (string path in ApiPaths)
        {
            foreach (string variant in new[]
                     {
                         "none", "wrong", "short", "long", "basic", "bare", "empty", "query-token", "query-access",
                         "two-headers",
                     })
            {
                data.Add(path, variant);
            }
        }

        return data;
    }

    public static TheoryData<string> Api()
    {
        TheoryData<string> data = [];
        foreach (string path in ApiPaths)
        {
            data.Add(path);
        }

        return data;
    }

    public static TheoryData<string> Shell()
    {
        TheoryData<string> data = [];
        foreach (string path in ShellPaths)
        {
            data.Add(path);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RefusedRequests))]
    public async Task Api_WithoutTheRightBearerTokenIsRefusedWithAFixedBody(string path, string variant)
    {
        using TempFactoryRoot root = Samples();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        string token = factory.ReadToken();
        using HttpRequestMessage request = new(HttpMethod.Get, WithVariantQuery(path, variant, token));
        AddVariantHeader(request, variant, token);

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", string.Join(",", response.Headers.WwwAuthenticate.Select(value => value.ToString())));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("{\"error\":\"unauthorized\"}", body);
        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [MemberData(nameof(Api))]
    public async Task Api_WithTheTokenGivesTheSameResponseAsWithoutTheGate(string path)
    {
        using TempFactoryRoot root = Samples();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        if (path.StartsWith("/api/x", StringComparison.Ordinal) || path == "/api")
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Api_AcceptsTheSchemeInAnyCase()
    {
        using TempFactoryRoot root = Samples();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/board");
        request.Headers.TryAddWithoutValidation("Authorization", "bearer " + factory.ReadToken());

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Api_RefusesEverythingWhenTheTokenFileTurnsUnsafeWhileRunning()
    {
        using TempFactoryRoot root = Samples();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/board")).StatusCode);

        File.WriteAllText(factory.TokenFile, "junk\n");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/board")).StatusCode);
    }

    [Theory]
    [InlineData("//api/board")]
    [InlineData("/api/./board")]
    [InlineData("/api/x/../board")]
    [InlineData("/%61pi/board")]
    [InlineData("/api%2Fboard")]
    [InlineData("/api/board/")]
    public async Task Api_OddPathFormsNeverGiveDataWithoutAToken(string path)
    {
        using TempFactoryRoot root = Samples();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(path);
        string body = await response.Content.ReadAsStringAsync();

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("\"jobs\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.ReadToken(), body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Shell))]
    public async Task Shell_LoadsWithoutAToken(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static TempFactoryRoot Samples()
    {
        TempFactoryRoot root = TempFactoryRoot.WithSamples();
        string story = root.AddProject("Story", "- [ ] [#1](backlog/001-first.md) First\n");
        TempFactoryRoot.AddStory(story, "001-first.md", "# First\n\nText.\n");
        root.AddJob("1-a", "{\"stage\":\"review\",\"round\":1}");
        return root;
    }

    private static string WithVariantQuery(string path, string variant, string token)
    {
        string separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return variant switch
        {
            "query-token" => path + separator + "token=" + token,
            "query-access" => path + separator + "access_token=" + token,
            _ => path,
        };
    }

    private static void AddVariantHeader(HttpRequestMessage request, string variant, string token)
    {
        string? value = variant switch
        {
            "wrong" => "Bearer " + (token[0] == 'A' ? "B" : "A") + token[1..],
            "short" => "Bearer " + token[..^1],
            "long" => "Bearer " + token + "A",
            "basic" => "Basic " + token,
            "bare" => token,
            "empty" => "Bearer ",
            _ => null,
        };
        if (value is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", value);
        }

        if (variant == "two-headers")
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        }
    }
}
