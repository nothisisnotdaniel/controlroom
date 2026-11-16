using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ControlRoom.Server.Tests;

/// <summary>Hosts the server in memory (no real port) with <c>FactoryRoot</c> set to a temporary root and
/// <c>TokenFile</c> set to a file in the factory's own temp folder (outside the root, never the real one), deleted
/// on dispose.</summary>
internal sealed class ServerFactory : WebApplicationFactory<Program>
{
    private readonly string factoryRoot;

    private readonly string? ownTokenFolder;

    public ServerFactory(string factoryRoot)
        : this(factoryRoot, null)
    {
    }

    public ServerFactory(string factoryRoot, string? tokenFile)
    {
        this.factoryRoot = factoryRoot;
        if (tokenFile is null)
        {
            ownTokenFolder = Path.Combine(Path.GetTempPath(), "controlroom-token-" + Guid.NewGuid().ToString("N"));
            tokenFile = Path.Combine(ownTokenFolder, "token", AccessToken.FileName);
        }

        TokenFile = tokenFile;
    }

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
