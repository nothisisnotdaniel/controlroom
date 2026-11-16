namespace ControlRoom.Server;

/// <summary>Reads a project's <c>backlog.md</c> through <see cref="SafeFileReader"/>, refusing a missing file, a
/// symlink, anything that is not a regular file (FIFO, socket, device), a file over 1 MiB and any read error
/// (returns <c>null</c> then).</summary>
internal static class BacklogReader
{
    public const string FileName = "backlog.md";

    public const long MaxBytes = 1024 * 1024;

    public static Backlog? Read(ProjectFolder project)
    {
        string? text = SafeFileReader.ReadText(Path.Combine(project.Path, FileName), MaxBytes);
        if (text is null)
        {
            return null;
        }

        return BacklogParser.Parse(text.Split('\n').Select(line => line.TrimEnd('\r')));
    }
}
