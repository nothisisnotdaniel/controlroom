using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ControlRoom.Server.Tests;

/// <summary>Hosts the server in memory (no real port) with <c>FactoryRoot</c> set to a temporary root and
/// <c>TokenFile</c> set to a file in the factory's own temp folder (outside the root, never the real one), deleted
/// on dispose. Clients send <c>Host: 127.0.0.1:8100</c>. Push requests go to <see cref="Push"/> (never the
/// network), the update check goes to <see cref="Update"/> as <see cref="CurrentVersion"/>, and the clock is
/// <see cref="Time"/>, whose timers never fire.</summary>
internal sealed class ServerFactory : WebApplicationFactory<Program>
{
    private readonly string factoryRoot;

    private readonly string? ownTokenFolder;

    private readonly string? tailnetHost;

    public ServerFactory(string factoryRoot)
        : this(factoryRoot, null)
    {
    }

    public ServerFactory(string factoryRoot, string? tokenFile)
        : this(factoryRoot, tokenFile, null)
    {
    }

    /// <summary>Hosts the server; <paramref name="tailnetHost"/> is passed as <c>TailnetHost</c> when not
    /// null.</summary>
    public ServerFactory(string factoryRoot, string? tokenFile, string? tailnetHost)
    {
        this.factoryRoot = factoryRoot;
        this.tailnetHost = tailnetHost;
        // The in-memory host's default Host is "localhost" with no port, which the Host check refuses.
        ClientOptions.BaseAddress = new Uri("http://127.0.0.1:8100/");
        if (tokenFile is null)
        {
            ownTokenFolder = Path.Combine(Path.GetTempPath(), "controlroom-token-" + Guid.NewGuid().ToString("N"));
            if (OperatingSystem.IsWindows())
            {
                WindowsPrivateFiles.CreateDirectory(new DirectoryInfo(ownTokenFolder));
            }

            tokenFile = Path.Combine(ownTokenFolder, "token", AccessToken.FileName);
        }

        TokenFile = tokenFile;
    }

    /// <summary>The fake GitHub API the update check goes to.</summary>
    public FakeUpdateHandler Update { get; } = new();

    /// <summary>The version the server reports as running; set before the host starts (default <c>dev</c>, which
    /// never checks).</summary>
    public string CurrentVersion { get; set; } = AppVersion.Dev;

    /// <summary>The fake push service every push request goes to.</summary>
    public FakePushHandler Push { get; } = new();

    /// <summary>Every log line the server writes.</summary>
    public LogCapture Logs { get; } = new();

    /// <summary>The server's clock.</summary>
    public FakeTime Time { get; } = new();

    /// <summary>The factory's develop worktree folder (<c>FACTORY_DEVELOP_DIR</c>); set before the host starts. By
    /// default a missing folder next to the temp root, so no test ever reads the real one.</summary>
    public string? FactoryDevelopDir { get; set; }

    /// <summary>The token file the hosted server uses.</summary>
    public string TokenFile { get; }

    /// <summary>The stored token (the server creates it on start).</summary>
    public string ReadToken()
    {
        TokenFileRead read = AccessToken.Read(TokenFile);
        Assert.Equal(TokenFileState.Valid, read.State);
        return read.Token!;
    }

    /// <summary>A client that sends <c>Authorization: Bearer &lt;token&gt;</c> on every request.</summary>
    public HttpClient CreateTokenClient()
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ReadToken());
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("FactoryRoot", factoryRoot);
        builder.UseSetting(AccessToken.ConfigKey, TokenFile);
        builder.UseSetting(
            FactoryBacklog.ConfigKey, FactoryDevelopDir ?? factoryRoot.TrimEnd('/', '\\') + "-factory-develop");
        if (tailnetHost is not null)
        {
            builder.UseSetting(HostCheck.ConfigKey, tailnetHost);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton(new PushTransport(Push));
            services.AddSingleton(new UpdateTransport(Update));
            services.AddSingleton(new RunningVersion(CurrentVersion));
            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && ownTokenFolder is not null && Directory.Exists(ownTokenFolder))
        {
            Directory.Delete(ownTokenFolder, recursive: true);
        }
    }
}
