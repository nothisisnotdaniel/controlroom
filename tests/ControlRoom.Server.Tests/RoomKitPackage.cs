using System.Reflection;

namespace ControlRoom.Server.Tests;

/// <summary>The restored <c>RoomKit</c> package folder (<c>$(PkgRoomKit)</c>, passed in by the test project as the
/// assembly metadata <c>RoomKitPackage</c>). Tests that compare with RoomKit's files read them here; a missing folder
/// fails the test with its name, it never skips.</summary>
internal static class RoomKitPackage
{
    /// <summary>The package folder. Fails the test, naming the folder, when it is not set or does not exist.</summary>
    public static string Folder()
    {
        string? folder = typeof(RoomKitPackage).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "RoomKitPackage")?.Value;
        return Existing(folder);
    }

    /// <summary>The package's <c>scripts/</c> folder.</summary>
    public static string Scripts() => Existing(Path.Combine(Folder(), "scripts"));

    /// <summary>The package's <c>staticwebassets/</c> folder (RoomKit's web files).</summary>
    public static string WebAssets() => Existing(Path.Combine(Folder(), "staticwebassets"));

    /// <summary>Returns <paramref name="folder"/>, or fails the test naming it when it is not set or does not exist.</summary>
    public static string Existing(string? folder)
    {
        Assert.False(
            string.IsNullOrEmpty(folder),
            "The RoomKit package folder ($(PkgRoomKit), assembly metadata RoomKitPackage) is not set; restore RoomKit first.");
        Assert.True(
            Directory.Exists(folder),
            "The RoomKit package folder " + folder + " ($(PkgRoomKit)) is missing; restore RoomKit first.");
        return folder;
    }
}
