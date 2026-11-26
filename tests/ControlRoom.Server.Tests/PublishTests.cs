namespace ControlRoom.Server.Tests;

/// <summary>A framework-dependent publish holds every web file of the app's <c>wwwroot</c> and of the RoomKit
/// package's <c>staticwebassets</c>.</summary>
public sealed class PublishTests
{
    [Fact]
    public void Publish_HoldsEveryWebFileOfTheAppAndRoomKit()
    {
        using RepoCopy copy = new();

        copy.Dotnet("publish", "src/ControlRoom.Server", "-c", "Release", "-o", copy.Output);

        string published = Path.Combine(copy.Output, "wwwroot");
        // The app's own files from the copy, RoomKit's from the restored package.
        foreach (string source in new[] { Path.Combine(copy.Path, "src", "ControlRoom.Server", "wwwroot"), RoomKitPackage.WebAssets() })
        {
            string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            Assert.NotEmpty(files);
            foreach (string file in files)
            {
                string relative = Path.GetRelativePath(source, file);
                string target = Path.Combine(published, relative);
                Assert.True(File.Exists(target), relative + " is missing from the publish output");
                Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(target));
            }
        }

        Assert.True(File.Exists(Path.Combine(published, "room.js")));
        Assert.True(File.Exists(Path.Combine(copy.Output, "ControlRoom.Server.dll")));
        Assert.True(File.Exists(Path.Combine(copy.Output, "RoomKit.dll")));
        Assert.Empty(Directory.GetFiles(copy.Output, "*.staticwebassets.runtime.json", SearchOption.AllDirectories));
    }
}
