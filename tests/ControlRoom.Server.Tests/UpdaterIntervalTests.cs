namespace ControlRoom.Server.Tests;

/// <summary><c>scripts/updater.sh</c> deciding whether a run is due (#142): the first run since load, the
/// <c>last-check</c> stamp and the interval from <c>settings.json</c>, on the fake clock (<c>date +%s</c> prints
/// <see cref="UpdaterHome.StartEpoch"/> unless a test sets another time) and the fake <c>launchctl print</c> of the
/// updater's label (same harness as the rest of <see cref="UpdaterScriptTests"/>).</summary>
public sealed partial class UpdaterScriptTests
{
    private const UnixFileMode StampMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static string IntervalSettings(int minutes, string channel = "stable", bool autoUpdate = true)
    {
        return "{\"autoUpdate\":" + (autoUpdate ? "true" : "false") + ",\"channel\":\"" + channel
            + "\",\"updateInterval\":" + minutes + "}\n";
    }

    private static string StampText(long epoch)
    {
        return epoch.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";
    }

    /// <summary>A run that is not due: exit 0, no output or errors, nothing in HOME changed (the stamp included), no
    /// state-changing launchctl call, no curl call and only the given sleep calls.</summary>
    private static void AssertNotDue(UpdaterHome home, string[] sleeps, params string[] extra)
    {
        IReadOnlyList<string> before = home.Snapshot("", withStamp: true);

        ScriptRun run = home.RunRaw(extra);

        Assert.Equal(new ScriptRun(0, "", ""), run);
        Assert.Equal(before, home.Snapshot("", withStamp: true));
        Assert.Empty(home.LaunchctlCalls());
        Assert.Empty(home.CurlCalls());
        Assert.Equal(sleeps, home.SleepCalls());
        Assert.False(Directory.Exists(home.Lock));
        Assert.False(File.Exists(home.Result));
        Assert.Empty(home.Leftovers());
    }

    /// <summary>A due run on the default home that finds nothing newer: logs <c>current</c> and writes the
    /// stamp.</summary>
    private static void AssertDueAndStamped(UpdaterHome home, long now = UpdaterHome.StartEpoch)
    {
        home.SetRedirect(Releases + "/tag/v1.2.0");

        Assert.Matches(LinePattern("current v1.2.0 v1.2.0"), home.RunOnce());

        Assert.Single(home.CurlCalls());
        Assert.Equal(StampText(now), File.ReadAllText(home.Stamp));
        Assert.Equal(StampMode, File.GetUnixFileMode(home.Stamp));
        Assert.Empty(home.Leftovers());
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(360)]
    [InlineData(720)]
    public void StampOneSecondShortOfTheIntervalLessSixty_IsNotDueAndLeavesNoTrace(int minutes)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(IntervalSettings(minutes));
        home.SetUpdaterRuns(2);
        home.SetStamp(StampText(UpdaterHome.StartEpoch - ((minutes * 60) - 61)));

        AssertNotDue(home, []);
        Assert.Single(home.UpdaterPrints());
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(360)]
    [InlineData(720)]
    public void StampAtTheIntervalLessSixty_IsDueWritesTheStampAndChecks(int minutes)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(IntervalSettings(minutes));
        home.SetUpdaterRuns(2);
        home.SetStamp(StampText(UpdaterHome.StartEpoch - ((minutes * 60) - 60)));

        AssertDueAndStamped(home);
    }

    [Fact]
    public void NotDueWithTheStartDelay_SleepsOnlyTheDelayAndLeavesNoTrace()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetUpdaterRuns(5);
        home.SetStamp(StampText(UpdaterHome.StartEpoch - 60));

        AssertNotDue(home, ["60"], "--start-delay", "60");
    }

    [Fact]
    public void ChangingTheIntervalFrom720To30_MakesTheNextRunDueWithoutAReinstall()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(IntervalSettings(720));
        home.SetUpdaterRuns(4);
        home.SetStamp(StampText(UpdaterHome.StartEpoch - (30 * 60)));
        AssertNotDue(home, []);

        home.SetSettings(IntervalSettings(30));

        AssertDueAndStamped(home);
    }

    [Theory]
    [InlineData("runs-1")]
    [InlineData("failed-print")]
    [InlineData("no-runs-line")]
    [InlineData("runs-not-a-number")]
    public void FirstRunSinceLoadOrAnUnreadablePrint_IsDueEvenWithAFreshStamp(string kind)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(IntervalSettings(720));
        home.SetStamp(StampText(UpdaterHome.StartEpoch - 10));
        switch (kind)
        {
            case "runs-1":
                home.SetUpdaterRuns(1);
                break;
            case "no-runs-line":
                home.SetUpdaterPrint("\tstate = running\n\tlast exit code = 0\n");
                break;
            case "runs-not-a-number":
                home.SetUpdaterPrint("\truns = x\n");
                break;
            default:
                // No answer set: the fake print exits 113.
                break;
        }

        AssertDueAndStamped(home);
        Assert.Equal(["print " + home.Domain + "/local.controlroom.updater"], home.UpdaterPrints());
    }

    [Theory]
    [InlineData("\truns = 2\n")]
    [InlineData("\t\truns = 1\n\truns = 7\n")]
    [InlineData("\truns = 11\n")]
    public void LaterRunWithAFreshStamp_IsNotDue(string print)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetUpdaterPrint(print);
        home.SetStamp(StampText(UpdaterHome.StartEpoch - 10));

        AssertNotDue(home, []);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("loose-mode")]
    [InlineData("symlink")]
    [InlineData("letters")]
    [InlineData("no-line-break")]
    [InlineData("two-line-breaks")]
    [InlineData("crlf")]
    [InlineData("empty")]
    [InlineData("leading-space")]
    [InlineData("over-cap")]
    [InlineData("future")]
    public void MissingOrBrokenOrFutureStamp_IsDueAndIsReplaced(string kind)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(IntervalSettings(720));
        home.SetUpdaterRuns(3);
        string fresh = (UpdaterHome.StartEpoch - 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
        switch (kind)
        {
            case "missing":
                break;
            case "loose-mode":
                home.SetStamp(fresh + "\n", StampMode | UnixFileMode.GroupRead);
                break;
            case "symlink":
                string target = Path.Combine(home.Path, "stamp-target");
                File.WriteAllText(target, fresh + "\n");
                File.SetUnixFileMode(target, StampMode);
                File.CreateSymbolicLink(home.Stamp, target);
                break;
            case "letters":
                home.SetStamp("17999x9990\n");
                break;
            case "no-line-break":
                home.SetStamp(fresh);
                break;
            case "two-line-breaks":
                home.SetStamp(fresh + "\n\n");
                break;
            case "crlf":
                home.SetStamp(fresh + "\r\n");
                break;
            case "empty":
                home.SetStamp("");
                break;
            case "leading-space":
                home.SetStamp(" " + fresh + "\n");
                break;
            case "over-cap":
                home.SetStamp(new string('0', 10) + fresh + "\n");
                break;
            default:
                home.SetStamp(StampText(UpdaterHome.StartEpoch + 1));
                break;
        }

        AssertDueAndStamped(home);
        Assert.Null(new FileInfo(home.Stamp).LinkTarget);
    }

    [Fact]
    public void StampThatIsAFolder_IsDueAndTheRunGoesOnWithoutAStamp()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetUpdaterRuns(3);
        Directory.CreateDirectory(home.Stamp);
        home.SetRedirect(Releases + "/tag/v1.2.0");

        Assert.Matches(LinePattern("current v1.2.0 v1.2.0"), home.RunOnce());
        Assert.True(Directory.Exists(home.Stamp));
        Assert.Empty(home.Leftovers());
    }

    [Theory]
    [InlineData("missing", "off")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":060}\n", "off")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":\"720\"}\n", "off")]
    [InlineData("{\"autoUpdate\":true,\"updateInterval\":720,\"channel\":\"stable\"}\n", "off")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":720,\"x\":1}\n", "off")]
    [InlineData("loose-mode", "off")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":45}\n", "current v1.2.0 v1.2.0")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":0}\n", "current v1.2.0 v1.2.0")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":00}\n", "off")]
    [InlineData("{\"autoUpdate\":true}\n", "current v1.2.0 v1.2.0")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\"}\n", "current v1.2.0 v1.2.0")]
    public void UnreadableInvalidOrOldSettings_CountAsThirtyMinutes(string settings, string word)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        if (settings == "missing")
        {
            home.SetSettings(null);
        }
        else if (settings == "loose-mode")
        {
            home.SetSettings(IntervalSettings(720));
            File.SetUnixFileMode(home.Settings, StampMode | UnixFileMode.OtherRead);
        }
        else
        {
            home.SetSettings(settings);
        }

        home.SetUpdaterRuns(3);
        home.SetRedirect(Releases + "/tag/v1.2.0");
        home.SetStamp(StampText(UpdaterHome.StartEpoch - ((30 * 60) - 60)));

        Assert.Matches(LinePattern(word), home.RunOnce());
        Assert.Equal(StampText(UpdaterHome.StartEpoch), File.ReadAllText(home.Stamp));

        // One second less is not due: the interval really was 30 minutes.
        home.ResetLogs();
        home.SetNow(UpdaterHome.StartEpoch + (30 * 60) - 61);
        AssertNotDue(home, []);
    }

    [Fact]
    public void DueRunWithTheSettingOff_WritesTheStampAndLogsOff()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(IntervalSettings(60, autoUpdate: false));

        Assert.Matches(LinePattern("off"), home.RunOnce());
        Assert.Equal(StampText(UpdaterHome.StartEpoch), File.ReadAllText(home.Stamp));
        Assert.Empty(home.CurlCalls());
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon\n")]
    [InlineData("-5\n")]
    public void UnreadableClock_IsDueAndWritesNoStamp(string now)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetUpdaterRuns(3);
        File.WriteAllText(Path.Combine(home.Path, "now"), now);
        home.SetRedirect(Releases + "/tag/v1.2.0");

        Assert.Matches(LinePattern("current v1.2.0 v1.2.0"), home.RunOnce());
        Assert.False(File.Exists(home.Stamp));
        Assert.Equal(1, home.ClockReads());
    }

    [Fact]
    public void FourKeyDevelopSetting_PicksTheDevelopChannelAndUpdates()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(IntervalSettings(120, "develop"));
        home.SetList("[{\"tag_name\":\"v1.2.1-develop.3\"}]\n");
        home.AddRelease("1.2.1-develop.3", "1.2.1-develop.3");

        Assert.Matches(LinePattern("updated v1.2.0 v1.2.1-develop.3"), home.RunOnce());
        Assert.Equal(ListCall, home.CurlCalls()[0]);
        Assert.Equal(StampText(UpdaterHome.StartEpoch), File.ReadAllText(home.Stamp));
    }

    [Fact]
    public void FourKeySettingWithAJobThatNeverEnds_StillWaitsTwentyThreeTimesAndStampsOnce()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(IntervalSettings(360));
        home.AddJob("1-active", "build");

        IReadOnlyList<string> lines = home.RunAll("--start-delay", "60");

        Assert.Equal(RetryWaits + 1, lines.Count);
        Assert.Matches(LinePattern("job_running v1.2.0"), lines[^1]);
        Assert.Equal(["60", .. Enumerable.Repeat("900", RetryWaits)], home.SleepCalls());
        Assert.Equal(1, home.ClockReads());
        Assert.Equal(StampText(UpdaterHome.StartEpoch), File.ReadAllText(home.Stamp));
        Assert.Empty(home.CurlCalls());
    }
}
