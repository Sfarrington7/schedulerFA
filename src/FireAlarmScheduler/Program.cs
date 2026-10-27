// Purpose: Application startup. Registers MVC, the EF Core (SQL Server) database, ASP.NET Core Identity with
// role-based authorization, local file storage, and the scheduling services, then configures the pipeline.
using FireAlarmScheduler.Data;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

// The factory serves the repositories (one short context per operation); Identity uses the scoped context it also registers.
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Role changes, locks, and deletions reach signed-in users within a minute instead of the default 30.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.SlidingExpiration = true;
});

// Every page requires sign-in unless marked [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(AppPolicies.ManageSchedule, policy => policy.RequireRole(AppPolicies.ManageScheduleRoles))
    .AddPolicy(AppPolicies.ManageUsers, policy => policy.RequireRole(AppPolicies.ManageUsersRoles))
    .AddPolicy(AppPolicies.ViewSchedule, policy => policy.RequireRole(AppRoles.All));

// Storage paths come from configuration and resolve against the content root, never wwwroot.
builder.Services.AddSingleton<IFileStorage>(services =>
    new LocalFileStorage(Path.Combine(builder.Environment.ContentRootPath,
        services.GetRequiredService<IOptions<StorageOptions>>().Value.UploadsDirectory)));

builder.Services.AddSingleton<ITechnicianRepository, EfTechnicianRepository>();
builder.Services.AddSingleton<IJobRepository, EfJobRepository>();
builder.Services.AddScoped<TechnicianService>();
builder.Services.AddScoped<JobService>();
builder.Services.AddScoped<BuildingFileService>();
builder.Services.AddScoped<UserAdministrationService>();

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// CSS and scripts must load on the sign-in page, so they are exempt from the sign-in fallback policy.
app.MapStaticAssets().AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
