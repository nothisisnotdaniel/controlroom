using System.Text;

namespace ControlRoom.Server;

/// <summary><c>settings.json</c> next to <c>push.json</c>: exactly <c>{"autoUpdate":true}</c> or
/// <c>{"autoUpdate":false}</c> (optionally with one trailing line break; the store writes it with one). The same
/// rules as <see cref="PushStore"/>: <c>0600</c>, written atomically, folder <c>0700</c> and owned by the current
/// user, no symlink; the file must also be owned by the user. A refused or invalid file throws
/// <see cref="PrivateFileException"/> and is never overwritten. The updater (<c>scripts/updater.sh</c>) reads the
/// same exact bytes, so both sides agree on what counts as on.</summary>
internal sealed class UpdateSettingsStore
{
    public const string FileName = "settings.json";

    public const long MaxFileBytes = 64;

    private static readonly byte[] OnBytes = Encoding.UTF8.GetBytes("{\"autoUpdate\":true}\n");

    private static readonly byte[] OffBytes = Encoding.UTF8.GetBytes("{\"autoUpdate\":false}\n");

    private readonly object gate = new();

    private readonly PrivateFolder folder;

    public UpdateSettingsStore(string folder, uint? owner)
    {
        this.folder = new PrivateFolder(folder, owner);
    }

    public string Path => System.IO.Path.Combine(folder.Path, FileName);

    /// <summary>The stored value; <c>false</c> when the file is missing.</summary>
    public bool AutoUpdate()
    {
        lock (gate)
        {
            return Load();
        }
    }

    /// <summary>Stores the value after checking the current file (so a refused file is left as it is).</summary>
    public bool SetAutoUpdate(bool autoUpdate)
    {
        lock (gate)
        {
            Load();
            folder.Write(FileName, autoUpdate ? OnBytes : OffBytes);
            return autoUpdate;
        }
    }

    private bool Load()
    {
        byte[]? bytes = folder.Read(FileName, MaxFileBytes, checkFileOwner: true, createFolder: true);
        if (bytes is null)
        {
            return false;
        }

        return Parse(bytes) ?? throw new PrivateFileException("settings file not valid");
    }

    /// <summary><c>true</c> or <c>false</c> for the exact allowed contents, else <c>null</c>.</summary>
    public static bool? Parse(byte[] bytes)
    {
        if (Matches(bytes, OnBytes))
        {
            return true;
        }

        return Matches(bytes, OffBytes) ? false : null;
    }

    /// <summary>Equal to <paramref name="canonical"/>, with or without its trailing line break.</summary>
    private static bool Matches(byte[] bytes, byte[] canonical)
    {
        return bytes.AsSpan().SequenceEqual(canonical) || bytes.AsSpan().SequenceEqual(canonical.AsSpan(0, canonical.Length - 1));
    }
}
