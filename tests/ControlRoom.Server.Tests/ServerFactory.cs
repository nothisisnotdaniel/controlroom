using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ControlRoom.Server.Tests;

/// <summary>Hosts the server in memory (no real port) with <c>FactoryRoot</c> set to a temporary root.</summary>
internal sealed class ServerFactory : WebApplicationFactory<Program>
{
    private readonly string factoryRoot;

    public ServerFactory(string factoryRoot)
    {
        this.factoryRoot = factoryRoot;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("FactoryRoot", factoryRoot);
    }
}
