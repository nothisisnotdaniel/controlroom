using System.Text;

namespace ControlRoom.Server;

/// <summary>Reads a project's <c>backlog.md</c>, refusing a missing file, a symlink, a file over 1 MiB and any
/// read error (returns <c>null</c> then).</summary>
internal static class BacklogReader
{
    public const string FileName = "backlog.md";

    public const long MaxBytes = 1024 * 1024;

    public static Backlog? Read(ProjectFolder project)
    {
        try
        {
            FileInfo file = new(Path.Combine(project.Path, FileName));
            if (!file.Exists || file.LinkTarget is not null || file.Length > MaxBytes)
            {
                return null;
            }

            using FileStream stream = new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] buffer = new byte[MaxBytes + 1];
            int total = 0;
            int read;
            while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
            {
                total += read;
            }

            if (total > MaxBytes)
            {
                return null;
            }

            string text = new UTF8Encoding(false).GetString(buffer, 0, total);
            if (text.Length > 0 && text[0] == '﻿')
            {
                text = text[1..];
            }

            return BacklogParser.Parse(text.Split('\n').Select(line => line.TrimEnd('\r')));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
