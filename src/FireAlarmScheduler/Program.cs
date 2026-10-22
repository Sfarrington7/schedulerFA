// Purpose: Application startup. Registers MVC, JSON-file repositories, local file storage,
// and the scheduling services, then configures the request pipeline.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);

// Storage paths come from configuration and resolve against the content root, never wwwroot.
string ResolveStoragePath(IServiceProvider services, Func<StorageOptions, string> select) =>
    Path.Combine(builder.Environment.ContentRootPath, select(services.GetRequiredService<IOptions<StorageOptions>>().Value));

builder.Services.AddSingleton(services =>
    new JsonFileStore<Technician>(Path.Combine(ResolveStoragePath(services, o => o.DataDirectory), "technicians.json")));
builder.Services.AddSingleton(services =>
    new JsonFileStore<Job>(Path.Combine(ResolveStoragePath(services, o => o.DataDirectory), "jobs.json")));
builder.Services.AddSingleton<IFileStorage>(services =>
    new LocalFileStorage(ResolveStoragePath(services, o => o.UploadsDirectory)));

builder.Services.AddSingleton<ITechnicianRepository, JsonTechnicianRepository>();
builder.Services.AddSingleton<IJobRepository, JsonJobRepository>();
builder.Services.AddScoped<TechnicianService>();
builder.Services.AddScoped<JobService>();
builder.Services.AddScoped<BuildingFileService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
