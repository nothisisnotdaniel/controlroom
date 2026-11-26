using System.Net;

namespace ControlRoom.Server.Tests;

public sealed class HttpSafetyTests
{
    private const string ExpectedPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; "
        + "manifest-src 'self'; worker-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    public static TheoryData<string, string> WriteRequests()
    {
        TheoryData<string, string> data = [];
        foreach (string method in new[] { "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "TRACE" })
        {
            foreach (string path in new[]
                     {
                         "/", "/api/backlogs", "/api/story?project=Normal&number=1", "/api/board",
                         "/api/job?id=1-a", "/index.html",
                         "/Normal/backlog.md", "/x",
                     })
            {
                data.Add(method, path);
            }
        }

        return data;
    }

    /// <summary>Methods other than GET, HEAD and POST.</summary>
    public sealed class Methods
    {
        [Theory]
        [MemberData(nameof(WriteRequests), MemberType = typeof(HttpSafetyTests))]
        public async Task OtherMethods_AreRefusedAndWriteNothing(string method, string path)
        {
            using TempFactoryRoot root = TempFactoryRoot.WithSamples();
            root.AddJob("1-a", "{\"stage\":\"review\",\"round\":1}");
            root.AddRecord("1-a", "spec.md", "# Spec\n");
            root.AddRecord("1-a", "round-1/review-ux.md", "VERDICT: PASS\n");
            IReadOnlyList<string> before = root.Snapshot();
            using ServerFactory factory = new(root.Path);
            using HttpClient client = factory.CreateTokenClient();
            using HttpRequestMessage request = new(new HttpMethod(method), path)
            {
                Content = new StringContent("- [ ] Injected: line\n"),
            };

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound });
            Assert.Equal(before, root.Snapshot());
        }
    }

    /// <summary>What is served and the headers every response carries.</summary>
    public sealed class Headers
    {
        [Theory]
        [InlineData("/CLAUDE.md")]
        [InlineData("/factory/CLAUDE.md")]
        [InlineData("/Normal/backlog.md")]
        [InlineData("/Times/backlog.md")]
        [InlineData("/../x")]
        [InlineData("/%2e%2e/CLAUDE.md")]
        [InlineData("/..%2fCLAUDE.md")]
        [InlineData("/api/backlogs/Normal")]
        public async Task FactoryRootFiles_AreNeverServed(string path)
        {
            using TempFactoryRoot root = TempFactoryRoot.WithSamples();
            root.AddProject("Times", "- [ ] Times: line\n");
            using ServerFactory factory = new(root.Path);
            using HttpClient client = factory.CreateTokenClient();

            using HttpResponseMessage response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Root_ServesIndexHtml()
        {
            using TempFactoryRoot root = new();
            using ServerFactory factory = new(root.Path);
            using HttpClient client = factory.CreateTokenClient();

            using HttpResponseMessage response = await client.GetAsync("/");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("<main id=\"backlogs\">", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("GET", "/")]
        [InlineData("GET", "/index.html")]
        [InlineData("GET", "/app.js")]
        [InlineData("GET", "/app.css")]
        [InlineData("GET", "/sw.js")]
        [InlineData("GET", "/manifest.webmanifest")]
        [InlineData("GET", "/icons/apple-touch-icon.png")]
        [InlineData("GET", "/icons/icon-192.png")]
        [InlineData("GET", "/icons/icon-512.png")]
        [InlineData("POST", "/sw.js")]
        [InlineData("POST", "/manifest.webmanifest")]
        [InlineData("GET", "/api/backlogs")]
        [InlineData("GET", "/missing")]
        [InlineData("HEAD", "/")]
        [InlineData("POST", "/api/backlogs")]
        [InlineData("GET", "/board.js")]
        [InlineData("GET", "/api/board")]
        [InlineData("HEAD", "/api/board")]
        [InlineData("POST", "/api/board")]
        [InlineData("GET", "/api/job?id=1-a")]
        [InlineData("GET", "/api/job?id=..")]
        [InlineData("HEAD", "/api/job?id=1-a")]
        [InlineData("POST", "/api/job?id=1-a")]
        public async Task EveryResponse_CarriesCspAndNoSniff(string method, string path)
        {
            using TempFactoryRoot root = new();
            using ServerFactory factory = new(root.Path);
            using HttpClient client = factory.CreateTokenClient();

            using HttpRequestMessage request = new(new HttpMethod(method), path);
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(ExpectedPolicy, string.Join(",", response.Headers.GetValues("Content-Security-Policy")));
            Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
        }

        [Fact]
        public async Task Csp_AllowsOnlySelfForManifestAndWorkerAndStaysStrict()
        {
            using TempFactoryRoot root = new();
            using ServerFactory factory = new(root.Path);
            using HttpClient client = factory.CreateTokenClient();

            using HttpResponseMessage response = await client.GetAsync("/sw.js");
            string policy = string.Join(",", response.Headers.GetValues("Content-Security-Policy"));

            foreach (string part in new[]
                     {
                         "default-src 'none'", "script-src 'self'", "connect-src 'self'", "frame-ancestors 'none'",
                         "manifest-src 'self'", "worker-src 'self'",
                     })
            {
                Assert.Contains(part, policy.Split("; "));
            }
        }
    }

    /// <summary>Where a POST is accepted and where it is refused.</summary>
    public sealed class Posts
    {
        [Theory]
        [InlineData("/sw.js")]
        [InlineData("/manifest.webmanifest")]
        [InlineData("/board.js")]
        [InlineData("/api/board")]
        [InlineData("/api/job?id=1-a")]
        public async Task Post_ToAppFiles_Gets405(string path)
        {
            using TempFactoryRoot root = new();
            using ServerFactory factory = new(root.Path);
            using HttpClient client = factory.CreateTokenClient();

            using HttpResponseMessage response = await client.PostAsync(path, new StringContent("x"));

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        [Theory]
        [InlineData("POST", "/api/backlogs")]
        [InlineData("POST", "/api/story?project=Normal&number=1")]
        [InlineData("POST", "/index.html")]
        [InlineData("POST", "/app.js")]
        [InlineData("POST", "/edit.js")]
        [InlineData("POST", "/API/backlog/change")]
        [InlineData("POST", "/api/backlog/change/")]
        [InlineData("POST", "/api/backlog")]
        [InlineData("PUT", "/api/backlog/change")]
        [InlineData("PATCH", "/api/backlog/change")]
        [InlineData("DELETE", "/api/backlog/change")]
        public async Task Post_IsAcceptedOnlyOnTheBacklogChangeRoute(string method, string path)
        {
            using TempFactoryRoot root = TempFactoryRoot.WithSamples();
            IReadOnlyList<string> before = root.Snapshot();
            using ServerFactory factory = new(root.Path);
            using HttpClient client = factory.CreateTokenClient();
            using HttpRequestMessage request = new(new HttpMethod(method), path)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
            Assert.Equal(before, root.Snapshot());
        }

        [Fact]
        public async Task Post_ToTheBacklogChangeRoute_ReachesTheEndpointBehindTheToken()
        {
            using TempFactoryRoot root = TempFactoryRoot.WithSamples();
            using ServerFactory factory = new(root.Path);
            using HttpClient client = factory.CreateTokenClient();
            using HttpClient anonymous = factory.CreateClient();

            using HttpResponseMessage response = await client.PostAsync(
                "/api/backlog/change", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            using HttpResponseMessage refused = await anonymous.PostAsync(
                "/api/backlog/change", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("{\"error\":\"invalid\"}", await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        [Fact]
        public async Task ServiceWorker_IsJavaScriptWithNoCache()
        {
            using TempFactoryRoot root = new();
            using ServerFactory factory = new(root.Path);
            using HttpClient client = factory.CreateTokenClient();

            using HttpResponseMessage response = await client.GetAsync("/sw.js");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(response.Content.Headers.ContentType?.MediaType,
                new[] { "text/javascript", "application/javascript" });
            Assert.True(response.Headers.CacheControl?.NoCache);
        }

        [Theory]
        [InlineData("POST", "/API/job/act")]
        [InlineData("POST", "/api/job/act/")]
        [InlineData("POST", "/api/job/acts")]
        [InlineData("POST", "/api/job")]
        [InlineData("POST", "/act.js")]
        [InlineData("PUT", "/api/job/act")]
        [InlineData("PATCH", "/api/job/act")]
        [InlineData("DELETE", "/api/job/act")]
        public async Task Post_IsAcceptedOnlyOnTheExactJobActRoute(string method, string path)
        {
            using TempFactoryRoot root = new();
            root.AddJob("1-a", "{\"stage\":\"needs-human\",\"round\":1}");
            IReadOnlyList<string> before = root.Snapshot();
            using ServerFactory factory = new(root.Path);
            using HttpClient client = factory.CreateTokenClient();
            using HttpRequestMessage request = new(new HttpMethod(method), path)
            {
                Content = new StringContent(JobActBody, System.Text.Encoding.UTF8, "application/json"),
            };

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
            Assert.Equal(before, root.Snapshot());
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("HEAD")]
        public async Task GetOnTheJobActRoute_IsNotAWrite(string method)
        {
            using TempFactoryRoot root = new();
            root.AddJob("1-a", "{\"stage\":\"needs-human\",\"round\":1}");
            IReadOnlyList<string> before = root.Snapshot();
            using ServerFactory factory = new(root.Path);
            using HttpClient client = factory.CreateTokenClient();
            using HttpRequestMessage request = new(new HttpMethod(method), "/api/job/act?id=1-a&action=approve&round=1");

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound });
            Assert.Equal(before, root.Snapshot());
        }

        [Fact]
        public async Task PostToTheJobActRoute_NeedsTheTokenAndAnAllowedHost()
        {
            using TempFactoryRoot root = new();
            root.AddJob("1-a", "{\"stage\":\"needs-human\",\"round\":1}");
            IReadOnlyList<string> before = root.Snapshot();
            using ServerFactory factory = new(root.Path);
            using HttpClient anonymous = factory.CreateClient();
            using HttpClient client = factory.CreateTokenClient();
            using HttpRequestMessage badHost = new(HttpMethod.Post, "/api/job/act")
            {
                Content = new StringContent(JobActBody, System.Text.Encoding.UTF8, "application/json"),
            };
            badHost.Headers.Host = "evil.example:8100";

            using HttpResponseMessage refused = await anonymous.PostAsync(
                "/api/job/act", new StringContent(JobActBody, System.Text.Encoding.UTF8, "application/json"));
            using HttpResponseMessage misdirected = await client.SendAsync(badHost);

            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            Assert.True(refused.Headers.CacheControl?.NoStore);
            Assert.Equal(HttpStatusCode.MisdirectedRequest, misdirected.StatusCode);
            Assert.Equal(before, root.Snapshot());

            using HttpResponseMessage accepted = await client.PostAsync(
                "/api/job/act", new StringContent(JobActBody, System.Text.Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.True(accepted.Headers.CacheControl?.NoStore);
            Assert.Equal(["1-a/human.json", "1-a/state.json"],
                Directory.GetFiles(root.JobsPath, "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(root.JobsPath, file).Replace('\\', '/'))
                    .Order(StringComparer.Ordinal));
        }
    }

    private const string JobActBody = "{\"id\":\"1-a\",\"action\":\"approve\",\"round\":1}";
}
