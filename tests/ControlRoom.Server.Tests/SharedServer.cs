namespace ControlRoom.Server.Tests;

/// <summary>One server on one temp factory root, shared by the tests of one class as an xUnit class fixture, so a
/// theory with many cases starts one host instead of one per case. Only for tests that send read-only requests and
/// never touch the root or the token file: a test that writes, or expects a write to be refused, makes its own
/// <see cref="ServerFactory"/>. The tests of one class run one at a time, so one test never sees another's
/// request.</summary>
public abstract class SharedServer : IDisposable
{
    private readonly TempFactoryRoot root;

    private readonly ServerFactory factory;

    private protected SharedServer(TempFactoryRoot root)
    {
        this.root = root;
        factory = new ServerFactory(root.Path);
    }

    internal ServerFactory Factory => factory;

    public void Dispose()
    {
        factory.Dispose();
        root.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>A <see cref="SharedServer"/> on an empty factory root.</summary>
public sealed class EmptyRootServer : SharedServer
{
    public EmptyRootServer()
        : base(new TempFactoryRoot())
    {
    }
}
