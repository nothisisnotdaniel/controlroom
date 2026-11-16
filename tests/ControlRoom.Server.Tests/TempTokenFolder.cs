namespace ControlRoom.Server.Tests;

/// <summary>A temporary folder for a token file, outside any factory root, deleted on dispose. Tests never use the
/// real token file.</summary>
internal sealed class TempTokenFolder : IDisposable
{
    public TempTokenFolder()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "controlroom-token-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>A token file path in a not-yet-created sub folder, so the folder mode can be checked.</summary>
    public string TokenFile => System.IO.Path.Combine(Path, "ControlRoom", AccessToken.FileName);

    public void Dispose()
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(Path, "*", SearchOption.AllDirectories))
        {
            if (!OperatingSystem.IsWindows() && new FileInfo(entry).LinkTarget is null)
            {
                File.SetUnixFileMode(entry, Directory.Exists(entry)
                    ? AccessToken.FolderMode
                    : AccessToken.FileMode);
            }
        }

        Directory.Delete(Path, recursive: true);
    }
}
