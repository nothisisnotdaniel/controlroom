using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ControlRoom.Server;

/// <summary>Reads a UTF-8 text file safely: refuses a missing file, a symlink, anything that is not a regular file
/// (FIFO, socket, device), a file over the size cap and any read error (returns <c>null</c> then). Strips a
/// leading BOM.</summary>
internal static class SafeFileReader
{
    public static string? ReadText(string path, long maxBytes)
    {
        byte[]? bytes = ReadBytes(path, maxBytes);
        return bytes is null ? null : Decode(bytes);
    }

    /// <summary>The file's raw bytes with the same refusals as <see cref="ReadText"/>, or <c>null</c>.</summary>
    public static byte[]? ReadBytes(string path, long maxBytes)
    {
        try
        {
            FileInfo file = new(path);
            if (!file.Exists || file.LinkTarget is not null || file.Length > maxBytes)
            {
                return null;
            }

            using FileStream? stream = OpenRegularFile(file.FullName);
            if (stream is null || stream.Length > maxBytes)
            {
                return null;
            }

            byte[] buffer = new byte[stream.Length + 1];
            int total = 0;
            int read;
            while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
            {
                total += read;
            }

            if (total > stream.Length)
            {
                return null;
            }

            return buffer[..total];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>UTF-8 text of the bytes (invalid sequences become U+FFFD) without a leading BOM.</summary>
    public static string Decode(byte[] bytes)
    {
        string text = new UTF8Encoding(false).GetString(bytes);
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        return text;
    }

    /// <summary>The version of a file's content: lowercase hex SHA-256 of its bytes.</summary>
    public static string Version(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>Opens the file for reading without following a symlink and without blocking (so a FIFO can't
    /// hang the request), and returns it only when it is a regular (seekable) file.</summary>
    private static FileStream? OpenRegularFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            FileStream windowsStream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return KeepIfRegular(windowsStream);
        }

        SafeFileHandle handle = OpenNoFollowNonBlocking(path);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        FileStream stream;
        try
        {
            stream = new FileStream(handle, FileAccess.Read, bufferSize: 0);
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        return KeepIfRegular(stream);
    }

    private static FileStream? KeepIfRegular(FileStream stream)
    {
        if (stream.CanSeek)
        {
            return stream;
        }

        stream.Dispose();
        return null;
    }

    private static SafeFileHandle OpenNoFollowNonBlocking(string path)
    {
        // Flags: O_RDONLY | O_NONBLOCK | O_NOFOLLOW | O_CLOEXEC (the values differ per platform; on Linux,
        // O_NOFOLLOW is 0x8000 on arm64 and 0x20000 on x64). Linux needs glibc (libc.so.6).
        int linuxNoFollow = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? 0x8000 : 0x20000;
        int descriptor = OperatingSystem.IsMacOS()
            ? MacOpen(path, 0x0000 | 0x0004 | 0x0100 | 0x01000000)
            : LinuxOpen(path, 0x0000 | 0x0800 | linuxNoFollow | 0x80000);
        return new SafeFileHandle(descriptor, ownsHandle: true);
    }

    [DllImport("libSystem.dylib", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int MacOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc.so.6", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int LinuxOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
}
