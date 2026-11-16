using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.StaticFiles;

namespace ControlRoom.Server;

/// <summary>Builds the web app: loopback-only Kestrel, security headers, GET/HEAD only, static files from the
/// server's own <c>wwwroot</c>, an access token on every <c>/api/</c> request, the backlogs, story, board and job
/// APIs.</summary>
internal static class ServerApp
{
    public const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; "
        + "manifest-src 'self'; worker-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>The service worker, served with <c>Cache-Control: no-cache</c> so a changed worker is picked
    /// up.</summary>
    public const string ServiceWorkerPath = "/sw.js";

    /// <summary>The fixed body of a refused API request.</summary>
    public const string UnauthorizedBody = "{\"error\":\"unauthorized\"}";

    /// <summary>Builds the app, or returns <c>null</c> after writing a fixed error text when there is no
    /// factory root or the access token file is not safe. A missing token file is created (the token itself is
    /// never written to <paramref name="error"/>).</summary>
    public static WebApplication? Create(string[] args, TextWriter error)
    {
        // Content root (and so wwwroot) is the server's own output folder, never the current directory.
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });

        string? root = FactoryRootLocator.Resolve(builder.Configuration["FactoryRoot"], AppContext.BaseDirectory);
        if (root is null)
        {
            error.WriteLine(FactoryRootLocator.NotFoundText);
            return null;
        }

        string tokenPath = AccessToken.ResolvePath(builder.Configuration[AccessToken.ConfigKey]);
        TokenFileRead token = AccessToken.Read(tokenPath);
        if (token.State == TokenFileState.Invalid)
        {
            error.WriteLine(AccessToken.InvalidText);
            return null;
        }

        if (token.State == TokenFileState.Missing)
        {
            if (AccessToken.Replace(tokenPath, AccessToken.SystemRandom) is null)
            {
                error.WriteLine(AccessToken.WriteFailedText);
                return null;
            }

            error.WriteLine(AccessToken.CreatedText);
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

            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                && !IsAuthorized(context.Request, tokenPath))
            {
                await WriteUnauthorized(context.Response);
                return;
            }

            await next(context);
        });
        app.UseDefaultFiles();
        app.UseStaticFiles(CreateStaticFileOptions());
        BacklogsEndpoint.Map(app);
        StoryEndpoint.Map(app);
        BoardEndpoint.Map(app);
        JobEndpoint.Map(app);
        return app;
    }

    /// <summary>True when the request carries exactly one <c>Authorization: Bearer &lt;token&gt;</c> header that
    /// matches the stored token. The file is read on every request, so a replaced token counts at once; an
    /// unsafe or missing file refuses everything. A token in the query string is never looked at.</summary>
    private static bool IsAuthorized(HttpRequest request, string tokenPath)
    {
        Microsoft.Extensions.Primitives.StringValues values = request.Headers.Authorization;
        if (values.Count != 1)
        {
            return false;
        }

        string value = values[0] ?? "";
        const string Scheme = "Bearer ";
        if (!value.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        TokenFileRead stored = AccessToken.Read(tokenPath);
        if (stored.State != TokenFileState.Valid || stored.Token is null)
        {
            return false;
        }

        return AccessToken.Matches(stored.Token, value[Scheme.Length..]);
    }

    private static async Task WriteUnauthorized(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.Headers.WWWAuthenticate = "Bearer";
        response.Headers.CacheControl = "no-store";
        response.ContentType = "application/json; charset=utf-8";
        await response.WriteAsync(UnauthorizedBody);
    }

    /// <summary>Static files: <c>.webmanifest</c> as <c>application/manifest+json</c>, and the service worker
    /// revalidated on every load.</summary>
    private static StaticFileOptions CreateStaticFileOptions()
    {
        FileExtensionContentTypeProvider contentTypes = new();
        contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
        return new StaticFileOptions
        {
            ContentTypeProvider = contentTypes,
            OnPrepareResponse = context =>
            {
                if (string.Equals(context.Context.Request.Path.Value, ServiceWorkerPath, StringComparison.Ordinal))
                {
                    context.Context.Response.Headers.CacheControl = "no-cache";
                }
            },
        };
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
