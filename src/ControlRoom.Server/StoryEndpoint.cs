using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;

namespace ControlRoom.Server;

/// <summary>The <c>GET /api/story</c> response.</summary>
internal sealed record StoryResponse(string Project, int Number, string Title, IReadOnlyList<StoryBlock> Blocks);

/// <summary><c>GET /api/story?project=&lt;name&gt;&amp;number=&lt;N&gt;</c>: one numbered backlog item's story as a
/// block tree. Only <c>project</c> (matched exactly against the discovered projects) and <c>number</c> are read;
/// the file path comes from the backlog line. Every refusal is the same fixed 404.</summary>
internal static partial class StoryEndpoint
{
    public const string Route = "/api/story";

    public const string NotFoundBody = "{\"error\":\"not found\"}";

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], (FactoryRoot root, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            StoryResponse? story = Build(root.Path, context.Request.Query["project"], context.Request.Query["number"]);
            if (story is null)
            {
                return Results.Content(NotFoundBody, "application/json", statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Json(story);
        });
    }

    public static StoryResponse? Build(string factoryRoot, StringValues projectValue, StringValues numberValue)
    {
        if (projectValue.Count != 1 || numberValue.Count != 1)
        {
            return null;
        }

        string projectName = projectValue[0] ?? "";
        string numberText = numberValue[0] ?? "";
        if (!Number().IsMatch(numberText) || !BacklogParser.TryParseNumber(numberText, out int number))
        {
            return null;
        }

        ProjectFolder? project = ProjectFinder.Find(factoryRoot)
            .FirstOrDefault(folder => string.Equals(folder.Name, projectName, StringComparison.Ordinal));
        if (project is null)
        {
            return null;
        }

        Backlog? backlog = BacklogReader.Read(project);
        BacklogEntry? entry = backlog?.All.FirstOrDefault(line => line.Number == number);
        if (entry is null)
        {
            return null;
        }

        string? text = StoryReader.Read(project, entry);
        if (text is null)
        {
            return null;
        }

        return new StoryResponse(project.Name, number, entry.Title, StoryMarkdown.Parse(text));
    }

    [GeneratedRegex(@"\A[0-9]{1,9}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Number();
}
