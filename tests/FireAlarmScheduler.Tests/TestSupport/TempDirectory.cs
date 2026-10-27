// Purpose: Creates a unique temporary folder (and the name of a matching throwaway test database) for one
// test and deletes both afterwards, so tests never touch real data and never depend on each other.
namespace FireAlarmScheduler.Tests.TestSupport;

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fas-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string DatabaseName => TestDatabase.NameFor(Path);

    public void Dispose()
    {
        TestDatabase.DropIfUsed(DatabaseName);
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
