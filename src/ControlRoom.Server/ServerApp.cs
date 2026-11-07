using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace ControlRoom.Server;

/// <summary>Builds the web app: loopback-only Kestrel, security headers, GET/HEAD only, static files from the
/// server's own <c>wwwroot</c> and the backlogs API.</summary>
internal static class ServerApp
{
    public const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; "
        + "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>Builds the app, or returns <c>null</c> after writing a fixed error text when there is no
    /// factory root.</summary>
    public static WebApplication? Create(string[] args, TextWriter error)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        string? root = FactoryRootLocator.Resolve(builder.Configuration["FactoryRoot"], AppContext.BaseDirectory);
        if (root is null)
        {
            error.WriteLine(FactoryRootLocator.NotFoundText);
            return null;
        }

        builder.WebHost.PreferHostingUrls(false);
        builder.WebHost.ConfigureKestrel(ConfigureKestrel);
        builder.Services.AddSingleton(new FactoryRoot(root));

        WebApplication app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
            context.Response.Headers.XContentTypeOptions = "nosniff";
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                return;
            }

            await next(context);
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        BacklogsEndpoint.Map(app);
        return app;
    }

    /// <summary>Listens only on <see cref="ServerEndpoint.Address"/>, and drops any <c>Kestrel</c> config section
    /// so <c>Kestrel:Endpoints</c> can't add addresses; <c>--urls</c> is overridden by the explicit
    /// endpoint.</summary>
    public static void ConfigureKestrel(KestrelServerOptions options)
    {
        options.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false);
        options.Listen(ServerEndpoint.Address);
    }
}
