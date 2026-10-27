// Purpose: Tests that local file storage only ever reads or writes plain names inside its own folder.
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;

namespace FireAlarmScheduler.Tests.Services;

public class LocalFileStorageTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("sub/file.csv")]
    [InlineData("..")]
    [InlineData("")]
    public void OpenRead_NameWithPathParts_Throws(string storedName)
    {
        var storage = new LocalFileStorage(_dir.Combine("uploads"));

        Assert.Throws<ArgumentException>(() => storage.OpenRead(storedName));
    }

    [Fact]
    public async Task OpenRead_SecretOutsideFolder_CannotBeReached()
    {
        await File.WriteAllTextAsync(_dir.Combine("secret.txt"), "secret");
        var storage = new LocalFileStorage(_dir.Combine("uploads"));

        Assert.Throws<ArgumentException>(() => storage.OpenRead("../secret.txt"));
    }

    [Fact]
    public async Task SaveAsync_ExistingName_DoesNotOverwrite()
    {
        var storage = new LocalFileStorage(_dir.Combine("uploads"));
        await storage.SaveAsync("a.csv", new MemoryStream([1]));

        await Assert.ThrowsAsync<IOException>(() => storage.SaveAsync("a.csv", new MemoryStream([2])));

        Assert.Equal([1], await File.ReadAllBytesAsync(_dir.Combine("uploads", "a.csv")));
    }

    [Fact]
    public void OpenRead_MissingFile_ReturnsNull()
    {
        Assert.Null(new LocalFileStorage(_dir.Path).OpenRead("missing.csv"));
    }
}
