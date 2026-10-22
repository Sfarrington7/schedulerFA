// Purpose: Builds the real repositories and services over a temporary folder for service-level tests.
// Creating a second harness on the same folder simulates an application restart.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using Microsoft.Extensions.Options;

namespace FireAlarmScheduler.Tests.TestSupport;

public sealed class ServiceHarness
{
    public static readonly DateTimeOffset Now = new(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);

    public ServiceHarness(string rootDirectory, long maxFileSizeBytes = 1024)
    {
        UploadsDirectory = Path.Combine(rootDirectory, "uploads");
        Technicians = new JsonTechnicianRepository(new JsonFileStore<Technician>(Path.Combine(rootDirectory, "technicians.json")));
        Jobs = new JsonJobRepository(new JsonFileStore<Job>(Path.Combine(rootDirectory, "jobs.json")));
        FileStorage = new LocalFileStorage(UploadsDirectory);

        TechnicianService = new TechnicianService(Technicians, Jobs);
        JobService = new JobService(Jobs, Technicians, FileStorage);
        FileService = new BuildingFileService(Jobs, FileStorage,
            Options.Create(new StorageOptions { MaxFileSizeBytes = maxFileSizeBytes }), new FixedTimeProvider(Now));
    }

    public string UploadsDirectory { get; }
    public ITechnicianRepository Technicians { get; }
    public IJobRepository Jobs { get; }
    public IFileStorage FileStorage { get; }
    public TechnicianService TechnicianService { get; }
    public JobService JobService { get; }
    public BuildingFileService FileService { get; }

    /// <summary>A valid job input for Nov 2–6, 2026, 8:00–16:00; override fields with a `with` expression.</summary>
    public static JobInput ValidJob(params Guid[] technicianIds) => new(
        Name: "Central Plaza Annual Fire Alarm Inspection",
        CustomerName: "Central Plaza LLC",
        SiteName: "Central Plaza",
        Address: "100 Main Street",
        StartDate: new DateOnly(2026, 11, 2),
        EndDate: new DateOnly(2026, 11, 6),
        StartTime: new TimeOnly(8, 0),
        EndTime: new TimeOnly(16, 0),
        Status: JobStatus.Scheduled,
        Notes: "Check in at security desk.",
        AssignedTechnicianIds: technicianIds);
}
