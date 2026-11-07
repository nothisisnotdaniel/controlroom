namespace ControlRoom.Server.Tests;

public sealed class ProjectFinderTests
{
    [Fact]
    public void Find_ListsOnlyRealProjectFoldersSortedByNameIgnoringCase()
    {
        using TempFactoryRoot root = TempFactoryRoot.WithSamples();

        IReadOnlyList<ProjectFolder> projects = ProjectFinder.Find(root.Path);

        Assert.Equal(
            ["emptyOne", "LinkedBacklog", "Missing", "Normal", "Oversized"],
            projects.Select(project => project.Name));
        Assert.All(projects, project => Assert.Equal(Path.Combine(root.Path, project.Name), project.Path));
    }

    [Fact]
    public void Find_MissingRootGivesNoProjects()
    {
        Assert.Empty(ProjectFinder.Find(Path.Combine(Path.GetTempPath(), "controlroom-missing-" + Guid.NewGuid())));
    }
}
