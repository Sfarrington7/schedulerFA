// Purpose: Builds the real repositories and services (including Identity) over a throwaway SQL Server
// database tied to a temporary folder for service-level tests. Creating a second harness on the same folder simulates
// an application restart. The database schema comes from the app's real EF Core migrations.
using FireAlarmScheduler.Data;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FireAlarmScheduler.Tests.TestSupport;

public sealed class ServiceHarness
{
    public static readonly DateTimeOffset Now = new(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);

    public ServiceHarness(string rootDirectory, long maxFileSizeBytes = 1024)
    {
        UploadsDirectory = Path.Combine(rootDirectory, "uploads");

        var connectionString = TestDatabase.ConnectionStringFor(TestDatabase.NameFor(rootDirectory));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContextFactory<AppDbContext>(options => options.UseSqlServer(connectionString));
        services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        services.AddSingleton<ITechnicianRepository, EfTechnicianRepository>();
        services.AddSingleton<IJobRepository, EfJobRepository>();
        services.AddSingleton<IPtoRepository, EfPtoRepository>();
        services.AddScoped<UserAdministrationService>();

        var provider = services.BuildServiceProvider();
        DatabaseInitializer.InitializeAsync(provider).GetAwaiter().GetResult();
        var scope = provider.CreateScope();

        ContextFactory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        Technicians = provider.GetRequiredService<ITechnicianRepository>();
        Jobs = provider.GetRequiredService<IJobRepository>();
        PtoRequests = provider.GetRequiredService<IPtoRepository>();
        FileStorage = new LocalFileStorage(UploadsDirectory);
        UserManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        UserAdmin = scope.ServiceProvider.GetRequiredService<UserAdministrationService>();

        TechnicianService = new TechnicianService(Technicians);
        JobService = new JobService(Jobs, Technicians, FileStorage);
        PtoService = new PtoService(PtoRequests, Technicians, new FixedTimeProvider(Now));
        ConflictService = new ScheduleConflictService(Jobs, PtoRequests);
        FileService = new BuildingFileService(Jobs, FileStorage,
            Options.Create(new StorageOptions { MaxFileSizeBytes = maxFileSizeBytes }), new FixedTimeProvider(Now));
    }

    public string UploadsDirectory { get; }
    public IDbContextFactory<AppDbContext> ContextFactory { get; }
    public ITechnicianRepository Technicians { get; }
    public IJobRepository Jobs { get; }
    public IPtoRepository PtoRequests { get; }
    public IFileStorage FileStorage { get; }
    public UserManager<ApplicationUser> UserManager { get; }
    public UserAdministrationService UserAdmin { get; }
    public TechnicianService TechnicianService { get; }
    public JobService JobService { get; }
    public BuildingFileService FileService { get; }
    public PtoService PtoService { get; }
    public ScheduleConflictService ConflictService { get; }

    /// <summary>A full-day PTO input from <paramref name="first"/> to <paramref name="last"/> (12:00 AM – 11:59 PM).</summary>
    public static PtoInput FullDays(DateOnly first, DateOnly last, string? reason = null) =>
        new(first, new TimeOnly(0, 0), last, new TimeOnly(23, 59), reason);

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

    /// <summary>Creates an account directly (bypassing the admin rules) with the given roles.</summary>
    public async Task<ApplicationUser> CreateUserAsync(string email, string[] roles, Guid? technicianId = null)
    {
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = email, TechnicianId = technicianId };
        var created = await UserManager.CreateAsync(user, AppFactory.TestPassword);
        Assert.True(created.Succeeded, string.Join(" ", created.Errors.Select(e => e.Description)));
        Assert.True((await UserManager.AddToRolesAsync(user, roles)).Succeeded);
        return user;
    }
}
