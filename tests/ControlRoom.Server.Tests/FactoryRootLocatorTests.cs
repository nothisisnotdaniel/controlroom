using Microsoft.AspNetCore.Builder;

namespace ControlRoom.Server.Tests;

public sealed class FactoryRootLocatorTests
{
    [Fact]
    public void FindDefault_IsTheParentOfTheFolderHoldingTheSolution()
    {
        using TempFactoryRoot root = new();
        string repo = Path.Combine(root.Path, "ControlRoom");
        string start = Path.Combine(repo, "src", "bin", "Debug");
        Directory.CreateDirectory(start);
        File.WriteAllText(Path.Combine(repo, FactoryRootLocator.SolutionFileName), "<Solution />");

        Assert.Equal(new DirectoryInfo(root.Path).FullName, FactoryRootLocator.FindDefault(start));
    }

    [Fact]
    public void FindDefault_WithoutSolutionGivesNull()
    {
        using TempFactoryRoot root = new();

        Assert.Null(FactoryRootLocator.FindDefault(root.Path));
    }

    [Fact]
    public void Resolve_PrefersTheConfiguredRootAndRefusesAMissingOne()
    {
        using TempFactoryRoot root = new();

        Assert.Equal(root.Path, FactoryRootLocator.Resolve(root.Path, "/"));
        Assert.Null(FactoryRootLocator.Resolve(Path.Combine(root.Path, "nope"), "/"));
    }

    [Fact]
    public void Create_WithoutRootWritesTheFixedTextAndBuildsNothing()
    {
        using TempFactoryRoot root = new();
        StringWriter error = new();

        WebApplication? app = ServerApp.Create(["--FactoryRoot", Path.Combine(root.Path, "nope")], error);

        Assert.Null(app);
        Assert.Equal(FactoryRootLocator.NotFoundText + Environment.NewLine, error.ToString());
    }
}
