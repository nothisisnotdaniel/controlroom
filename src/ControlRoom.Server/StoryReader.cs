using System.Text.RegularExpressions;

namespace ControlRoom.Server;

/// <summary>Reads the story file a numbered backlog line links to. The target must be
/// <c>backlog/NNN-slug.md</c> with the line's own number; the file is read only from the project's
/// <c>backlog</c> folder, which must not be a symlink, through <see cref="SafeFileReader"/> (no-follow, regular
/// file, 1 MiB cap). Returns <c>null</c> for any refusal or read error.</summary>
internal static partial class StoryReader
{
    public const string FolderName = "backlog";

    public const long MaxBytes = 1024 * 1024;

    public static string? Read(ProjectFolder project, BacklogEntry entry)
    {
        if (entry.Number is null || entry.Story is null)
        {
            return null;
        }

        Match match = StoryTarget().Match(entry.Story);
        if (!match.Success
            || !BacklogParser.TryParseNumber(match.Groups["number"].Value, out int number)
            || number != entry.Number.Value)
        {
            return null;
        }

        try
        {
            DirectoryInfo folder = new(Path.Combine(project.Path, FolderName));
            if (!folder.Exists || folder.LinkTarget is not null)
            {
                return null;
            }

            string folderPath = Path.GetFullPath(folder.FullName);
            string filePath = Path.GetFullPath(Path.Combine(folderPath, match.Groups["file"].Value));
            if (!string.Equals(Path.GetDirectoryName(filePath), folderPath, StringComparison.Ordinal))
            {
                return null;
            }

            return SafeFileReader.ReadText(filePath, MaxBytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\Abacklog/(?<file>(?<number>[0-9]{3,})-[A-Za-z0-9-]+\.md)\z", RegexOptions.CultureInvariant)]
    private static partial Regex StoryTarget();
}
