// Purpose: Tests spreadsheet uploads per building: allowed types, size limits, safe generated
// storage names, malicious file names, byte-for-byte downloads, and deletion.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;

namespace FireAlarmScheduler.Tests.Services;

public class BuildingFileServiceTests : IDisposable
{
    private const long MaxBytes = 1024;

    private readonly TempDirectory _dir = new();
    private readonly ServiceHarness _harness;

    public BuildingFileServiceTests()
    {
        _harness = new ServiceHarness(_dir.Path, MaxBytes);
    }

    public void Dispose() => _dir.Dispose();

    private BuildingFileService Files => _harness.FileService;

    private async Task<(Job Job, Building Building)> CreateJobWithBuildingAsync()
    {
        var job = await _harness.JobService.CreateAsync(ServiceHarness.ValidJob());
        var building = (await _harness.JobService.AddBuildingAsync(job.Id, "Building A", null))!;
        return (job, building);
    }

    private Task<UploadedFile?> UploadAsync(Guid jobId, Guid buildingId, string name, byte[] bytes) =>
        Files.UploadAsync(jobId, buildingId, name, new MemoryStream(bytes), bytes.Length);

    [Theory]
    [InlineData("devices.xlsx")]
    [InlineData("devices.xls")]
    [InlineData("devices.csv")]
    [InlineData("DEVICES.XLSX")]
    public async Task UploadAsync_AllowedExtension_StoresFileAndRecordsItOnBuilding(string name)
    {
        var (job, building) = await CreateJobWithBuildingAsync();

        var file = await UploadAsync(job.Id, building.Id, name, [1, 2, 3]);

        Assert.NotNull(file);
        Assert.Equal(name, file.OriginalFileName);
        Assert.Equal(building.Id, file.BuildingId);
        Assert.Equal(3, file.SizeBytes);
        Assert.Equal(ServiceHarness.Now.UtcDateTime, file.UploadDate);
        var stored = Assert.Single((await _harness.JobService.GetAsync(job.Id))!.Buildings[0].UploadedFiles);
        Assert.Equal(file.Id, stored.Id);
        Assert.True(File.Exists(Path.Combine(_harness.UploadsDirectory, file.StoredFileName)));
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("report.pdf")]
    [InlineData("devices.xlsx.exe")]
    [InlineData("noextension")]
    [InlineData(".xlsx")]
    public async Task UploadAsync_DisallowedExtension_ThrowsAndStoresNothing(string name)
    {
        var (job, building) = await CreateJobWithBuildingAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() => UploadAsync(job.Id, building.Id, name, [1]));

        Assert.False(Directory.Exists(_harness.UploadsDirectory) && Directory.EnumerateFiles(_harness.UploadsDirectory).Any());
        Assert.Empty((await _harness.JobService.GetAsync(job.Id))!.Buildings[0].UploadedFiles);
    }

    [Fact]
    public async Task UploadAsync_OverSizeLimit_Throws()
    {
        var (job, building) = await CreateJobWithBuildingAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() => UploadAsync(job.Id, building.Id, "big.csv", new byte[MaxBytes + 1]));
    }

    [Fact]
    public async Task UploadAsync_ExactlyAtSizeLimit_Succeeds()
    {
        var (job, building) = await CreateJobWithBuildingAsync();

        Assert.NotNull(await UploadAsync(job.Id, building.Id, "max.csv", new byte[MaxBytes]));
    }

    [Fact]
    public async Task UploadAsync_EmptyFile_Throws()
    {
        var (job, building) = await CreateJobWithBuildingAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() => UploadAsync(job.Id, building.Id, "empty.csv", []));
    }

    [Fact]
    public async Task UploadAsync_SameNameTwice_GetsUniqueIdsAndStoredNames()
    {
        var (job, building) = await CreateJobWithBuildingAsync();

        var first = (await UploadAsync(job.Id, building.Id, "devices.xlsx", [1]))!;
        var second = (await UploadAsync(job.Id, building.Id, "devices.xlsx", [2]))!;

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.StoredFileName, second.StoredFileName);
        Assert.Matches("^[0-9a-f]{32}\\.xlsx$", first.StoredFileName);
        Assert.Equal(2, Directory.EnumerateFiles(_harness.UploadsDirectory).Count());
    }

    [Theory]
    [InlineData("../../evil.csv", "evil.csv")]
    [InlineData("..\\..\\Windows\\evil.csv", "evil.csv")]
    [InlineData("C:\\Users\\lead\\Desktop\\devices.xlsx", "devices.xlsx")]
    [InlineData("/etc/devices.csv", "devices.csv")]
    public async Task UploadAsync_PathInFileName_KeepsOnlyNameAndStoresInsideUploadsFolder(string name, string expected)
    {
        var (job, building) = await CreateJobWithBuildingAsync();

        var file = (await UploadAsync(job.Id, building.Id, name, [1]))!;

        Assert.Equal(expected, file.OriginalFileName);
        var storedFile = Assert.Single(Directory.EnumerateFiles(_harness.UploadsDirectory));
        Assert.Equal(file.StoredFileName, Path.GetFileName(storedFile));
        Assert.Single(Directory.EnumerateFiles(_dir.Path, "*", SearchOption.AllDirectories), p => p.EndsWith(".csv") || p.EndsWith(".xlsx"));
    }

    [Fact]
    public async Task UploadAsync_UnknownJobOrBuilding_ReturnsNull()
    {
        var (job, _) = await CreateJobWithBuildingAsync();

        Assert.Null(await UploadAsync(job.Id, Guid.NewGuid(), "a.csv", [1]));
        Assert.Null(await UploadAsync(Guid.NewGuid(), Guid.NewGuid(), "a.csv", [1]));
    }

    [Fact]
    public async Task OpenDownloadAsync_UploadedFile_ReturnsIdenticalBytesAndOriginalName()
    {
        var (job, building) = await CreateJobWithBuildingAsync();
        var bytes = Enumerable.Range(0, 500).Select(i => (byte)i).ToArray();
        var file = (await UploadAsync(job.Id, building.Id, "Building A devices.xlsx", bytes))!;

        var download = await Files.OpenDownloadAsync(job.Id, building.Id, file.Id);

        Assert.NotNull(download);
        await using var content = download.Content;
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
        Assert.Equal("Building A devices.xlsx", download.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", download.ContentType);
    }

    [Fact]
    public async Task OpenDownloadAsync_FileFromAnotherBuilding_ReturnsNull()
    {
        var (job, a) = await CreateJobWithBuildingAsync();
        var b = (await _harness.JobService.AddBuildingAsync(job.Id, "Building B", null))!;
        var file = (await UploadAsync(job.Id, a.Id, "a.csv", [1]))!;

        Assert.Null(await Files.OpenDownloadAsync(job.Id, b.Id, file.Id));
    }

    [Fact]
    public async Task OpenDownloadAsync_StoredBytesMissing_ReturnsNull()
    {
        var (job, building) = await CreateJobWithBuildingAsync();
        var file = (await UploadAsync(job.Id, building.Id, "a.csv", [1]))!;
        File.Delete(Path.Combine(_harness.UploadsDirectory, file.StoredFileName));

        Assert.Null(await Files.OpenDownloadAsync(job.Id, building.Id, file.Id));
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecordAndStoredFile()
    {
        var (job, building) = await CreateJobWithBuildingAsync();
        var file = (await UploadAsync(job.Id, building.Id, "a.csv", [1]))!;

        Assert.True(await Files.DeleteAsync(job.Id, building.Id, file.Id));

        Assert.Empty((await _harness.JobService.GetAsync(job.Id))!.Buildings[0].UploadedFiles);
        Assert.False(File.Exists(Path.Combine(_harness.UploadsDirectory, file.StoredFileName)));
        Assert.False(await Files.DeleteAsync(job.Id, building.Id, file.Id));
    }
}
