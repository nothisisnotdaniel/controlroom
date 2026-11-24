using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ControlRoom.Server.Tests;

public sealed class WindowsTokenTests
{
    [Fact]
    public void TokenNew_CreatesUserOnlyFilesIncludingTheTemporaryFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempTokenFolder tokens = new();
        string folder = Path.GetDirectoryName(tokens.TokenFile)!;
        string? first = AccessToken.Replace(tokens.TokenFile, buffer => buffer.Fill(10));
        Assert.NotNull(first);
        AssertPrivate(new DirectoryInfo(folder).GetAccessControl());
        AssertPrivate(new FileInfo(tokens.TokenFile).GetAccessControl());
        string temporary = Path.Combine(folder, "creation-test.tmp");
        using (AccessToken.CreatePrivateFile(temporary))
        {
            AssertPrivate(new FileInfo(temporary).GetAccessControl());
        }

        StringWriter output = new();
        Assert.Equal(0, TokenCommand.Run(["token", "--new", "--TokenFile", tokens.TokenFile],
            output, TextWriter.Null, buffer => buffer.Fill(11)));
        Assert.NotEqual(first, AccessToken.Read(tokens.TokenFile).Token);
        Assert.Equal(AccessToken.Generate(buffer => buffer.Fill(11)) + "\n", output.ToString());
        AssertPrivate(new FileInfo(tokens.TokenFile).GetAccessControl());
        Assert.Contains(@".\controlroom.exe token", AccessToken.CreatedText(null), StringComparison.Ordinal);
        Assert.Contains(@".\controlroom.exe token --new --TokenFile ", AccessToken.InvalidText(tokens.TokenFile), StringComparison.Ordinal);
    }

    [Fact]
    public void TokenLookupSeparatesMissingFilesFromRefusedPaths()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempTokenFolder tokens = new();
        Assert.Equal(TokenFileState.Missing, AccessToken.Read(tokens.TokenFile).State);
        DirectoryInfo folder = new FileInfo(tokens.TokenFile).Directory!;
        WindowsPrivateFiles.CreateDirectory(folder);
        Assert.Equal(TokenFileState.Missing, AccessToken.Read(tokens.TokenFile).State);
        using (AccessToken.CreatePrivateFile(tokens.TokenFile))
        {
            Assert.Equal(TokenFileState.Invalid, AccessToken.Read(tokens.TokenFile).State);
        }

        Assert.Equal(TokenFileState.Invalid, AccessToken.Read(tokens.TokenFile + ":extra").State);
        Assert.Equal(TokenFileState.Invalid, AccessToken.Read(Path.Combine(tokens.TokenFile, "child")).State);
    }

    [Fact]
    public void VerifiedDirectoryStaysPinnedThroughTokenReplacement()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempTokenFolder tokens = new();
        DirectoryInfo folder = new FileInfo(tokens.TokenFile).Directory!;
        WindowsPrivateFiles.CreateDirectory(folder);
        File.WriteAllText(tokens.TokenFile, AccessToken.Generate(buffer => buffer.Fill(10)));
        string moved = Path.Combine(tokens.Path, "previous-folder");
        string next = AccessToken.Generate(buffer => buffer.Fill(11));
        using (FileStream parent = WindowsPrivateFiles.OpenDirectory(folder))
        {
            Assert.Throws<IOException>(() => Directory.Move(folder.FullName, moved));
            WindowsPrivateFiles.Replace(parent, Path.GetFileName(tokens.TokenFile), System.Text.Encoding.ASCII.GetBytes(next));
            Assert.Equal(next, AccessToken.Read(tokens.TokenFile).Token);
        }

        Directory.Move(folder.FullName, moved);
        Directory.CreateDirectory(folder.FullName);
        Assert.False(File.Exists(tokens.TokenFile));
        Assert.Equal(next, File.ReadAllText(Path.Combine(moved, Path.GetFileName(tokens.TokenFile))));
    }

    [Fact]
    public void SharedStoreReplacesOnlyPrivateChildrenOfTheVerifiedDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempTokenFolder fixture = new();
        DirectoryInfo folder = new(Path.Combine(fixture.Path, "custom token folder"));
        PrivateFolder store = new(folder.FullName, null);
        const string Name = "push-keys.json";
        FileInfo file = new(Path.Combine(folder.FullName, Name));
        store.Write(Name, [1]);
        using (FileStream old = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            store.Write(Name, [2]);
            Assert.Equal(1, old.ReadByte());
            Assert.Equal(new byte[] { 2 }, store.Read(Name, 16, checkFileOwner: true, createFolder: false));
        }

        using (FileStream parent = WindowsPrivateFiles.OpenDirectory(folder))
        {
            Assert.Throws<IOException>(() => Directory.Move(folder.FullName, Path.Combine(fixture.Path, "moved")));
            WindowsPrivateFiles.WritePrivateChild(parent, "settings.json", [3]);
        }

        AssertPrivate(file.GetAccessControl());
        AssertPrivate(new FileInfo(Path.Combine(folder.FullName, "settings.json")).GetAccessControl());
        FileSecurity shared = file.GetAccessControl();
        shared.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        FileInfo subscriptions = new(Path.Combine(folder.FullName, "push.json"));
        using (FileStream stream = subscriptions.Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None,
                   4096, FileOptions.None, shared))
        {
            stream.WriteByte(7);
        }
        Assert.Throws<PrivateFileException>(() => store.Write(subscriptions.Name, [4]));
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(subscriptions.FullName));
        Assert.Empty(folder.GetFiles("*.tmp"));
    }

    [Fact]
    public void TokenRotationLetsAnExistingReaderFinishWithItsOriginalBytes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempTokenFolder tokens = new();
        string? old = AccessToken.Replace(tokens.TokenFile, buffer => buffer.Fill(1));
        Assert.NotNull(old);
        using FileStream opened = File.Open(tokens.TokenFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using StreamReader reader = new(opened);
        string? next = AccessToken.Replace(tokens.TokenFile, buffer => buffer.Fill(2));
        Assert.NotNull(next);
        Assert.Equal(old + "\n", reader.ReadToEnd());
        Assert.Equal(next, AccessToken.Read(tokens.TokenFile).Token);
    }

    [Fact]
    public void AForeignOwnerIsRefusedEvenWithOnlyTheCurrentUsersGrant()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        CheckForeignOwner();
    }

    [SupportedOSPlatform("windows")]
    private static void CheckForeignOwner()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User!;
        // Testing this descriptor directly keeps the case runnable without the privilege to assign a foreign owner.
        RawSecurityDescriptor foreign = new("O:SYG:SYD:P(A;;FA;;;" + user.Value + ")");
        Assert.Throws<UnauthorizedAccessException>(() => WindowsPrivateFiles.CheckDescriptor(foreign, user));
    }

    [SupportedOSPlatform("windows")]
    private static void AssertPrivate(FileSystemSecurity acl)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        Assert.Equal(identity.User, acl.GetOwner(typeof(SecurityIdentifier)));
        Assert.True(acl.AreAccessRulesProtected);
        FileSystemAccessRule only = Assert.Single(acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>());
        Assert.Equal(identity.User, only.IdentityReference);
        Assert.Equal(FileSystemRights.FullControl, only.FileSystemRights);
    }
}
