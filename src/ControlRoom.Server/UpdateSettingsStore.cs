using System.Text;

namespace ControlRoom.Server;

/// <summary>The stored update settings: <c>autoUpdate</c> and the update channel (<see cref="UpdateChannel"/>).</summary>
internal sealed record UpdateSettings(bool AutoUpdate, string Channel);

/// <summary>The two update channels: <c>stable</c> (proper releases only, the default) and <c>develop</c> (also
/// the <c>-develop</c> prereleases made for every push to <c>develop</c>).</summary>
internal static class UpdateChannel
{
    public const string Stable = "stable";

    public const string Develop = "develop";

    /// <summary>The channel itself when it is one of the two names (ordinal), else <c>null</c>.</summary>
    public static string? Parse(string? value)
    {
        return value switch
        {
            Stable => Stable,
            Develop => Develop,
            _ => null,
        };
    }
}

/// <summary><c>settings.json</c> next to <c>push.json</c>: exactly one of <c>{"autoUpdate":B}</c> (channel
/// <c>stable</c>), <c>{"autoUpdate":B,"channel":"stable"}</c> or <c>{"autoUpdate":B,"channel":"develop"}</c>, with
/// B <c>true</c> or <c>false</c> and optionally one trailing line break; the store always writes the three-key form
/// with one. The same rules as <see cref="PushStore"/>: <c>0600</c>, written atomically, folder <c>0700</c> and
/// owned by the current user, no symlink; the file must also be owned by the user. A refused or invalid file throws
/// <see cref="PrivateFileException"/> and is never overwritten. The updater (<c>scripts/updater.sh</c>) reads the
/// same exact bytes, so both sides agree on what counts as on and which channel is set.</summary>
internal sealed class UpdateSettingsStore
{
    public const string FileName = "settings.json";

    public const long MaxFileBytes = 64;

    /// <summary>A missing file: off, <c>stable</c>.</summary>
    public static readonly UpdateSettings Default = new(false, UpdateChannel.Stable);

    private static readonly bool[] Flags = [true, false];

    private static readonly string[] Channels = [UpdateChannel.Stable, UpdateChannel.Develop];

    private readonly object gate = new();

    private readonly PrivateFolder folder;

    public UpdateSettingsStore(string folder, uint? owner)
    {
        this.folder = new PrivateFolder(folder, owner);
    }

    public string Path => System.IO.Path.Combine(folder.Path, FileName);

    /// <summary>The stored settings; <see cref="Default"/> when the file is missing.</summary>
    public UpdateSettings Settings()
    {
        lock (gate)
        {
            return Load();
        }
    }

    /// <summary>The stored <c>autoUpdate</c>; <c>false</c> when the file is missing.</summary>
    public bool AutoUpdate()
    {
        return Settings().AutoUpdate;
    }

    /// <summary>Stores <paramref name="autoUpdate"/> and keeps the channel.</summary>
    public bool SetAutoUpdate(bool autoUpdate)
    {
        return Set(autoUpdate, null).AutoUpdate;
    }

    /// <summary>Stores the given values after checking the current file (so a refused file is left as it is); a
    /// <c>null</c> value keeps the stored one. <paramref name="channel"/> must be one of
    /// <see cref="UpdateChannel"/>.</summary>
    public UpdateSettings Set(bool? autoUpdate, string? channel)
    {
        return Set(autoUpdate, channel, out _);
    }

    /// <summary><see cref="Set(bool?, string?)"/>, also telling whether the stored channel changed.</summary>
    public UpdateSettings Set(bool? autoUpdate, string? channel, out bool channelChanged)
    {
        if (channel is not null && UpdateChannel.Parse(channel) is null)
        {
            throw new ArgumentException("unknown update channel", nameof(channel));
        }

        lock (gate)
        {
            UpdateSettings current = Load();
            UpdateSettings wanted = new(autoUpdate ?? current.AutoUpdate, channel ?? current.Channel);
            folder.Write(FileName, Canonical(wanted));
            channelChanged = wanted.Channel != current.Channel;
            return wanted;
        }
    }

    private UpdateSettings Load()
    {
        byte[]? bytes = folder.Read(FileName, MaxFileBytes, checkFileOwner: true, createFolder: true);
        if (bytes is null)
        {
            return Default;
        }

        return Parse(bytes) ?? throw new PrivateFileException("settings file not valid");
    }

    /// <summary>The settings for the exact allowed contents, else <c>null</c>.</summary>
    public static UpdateSettings? Parse(byte[] bytes)
    {
        foreach (bool flag in Flags)
        {
            if (Matches(bytes, Bytes("{\"autoUpdate\":" + Flag(flag) + "}\n")))
            {
                return new UpdateSettings(flag, UpdateChannel.Stable);
            }

            foreach (string channel in Channels)
            {
                UpdateSettings settings = new(flag, channel);
                if (Matches(bytes, Canonical(settings)))
                {
                    return settings;
                }
            }
        }

        return null;
    }

    /// <summary>The three-key form the store writes, with its trailing line break.</summary>
    public static byte[] Canonical(UpdateSettings settings)
    {
        return Bytes("{\"autoUpdate\":" + Flag(settings.AutoUpdate) + ",\"channel\":\"" + settings.Channel + "\"}\n");
    }

    private static string Flag(bool value)
    {
        return value ? "true" : "false";
    }

    private static byte[] Bytes(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>Equal to <paramref name="canonical"/>, with or without its trailing line break.</summary>
    private static bool Matches(byte[] bytes, byte[] canonical)
    {
        return bytes.AsSpan().SequenceEqual(canonical) || bytes.AsSpan().SequenceEqual(canonical.AsSpan(0, canonical.Length - 1));
    }
}
