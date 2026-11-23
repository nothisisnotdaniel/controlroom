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
        Assert.Contains(@".\controlroom.exe token", AccessToken.CreatedText, StringComparison.Ordinal);
        Assert.Contains(@".\controlroom.exe token --new", AccessToken.InvalidText, StringComparison.Ordinal);
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
