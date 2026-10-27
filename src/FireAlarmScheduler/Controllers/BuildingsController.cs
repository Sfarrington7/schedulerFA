// Purpose: Building pages within a job: add, edit, and remove buildings, and upload, download,
// and delete each building's spreadsheets. Downloads are also used by the technician view, where a
// technician may only download files from jobs they are assigned to.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

[Route("Jobs/{jobId:guid}/Buildings")]
public class BuildingsController : Controller
{
    private readonly JobService _jobService;
    private readonly BuildingFileService _fileService;
    private readonly UserManager<ApplicationUser> _userManager;

    public BuildingsController(JobService jobService, BuildingFileService fileService, UserManager<ApplicationUser> userManager)
    {
        _jobService = jobService;
        _fileService = fileService;
        _userManager = userManager;
    }

    [HttpPost(""), ValidateAntiForgeryToken, Authorize(Policy = AppPolicies.ManageSchedule)]
    public async Task<IActionResult> Create(Guid jobId, [Bind(Prefix = nameof(JobDetailsViewModel.NewBuilding))] BuildingFormModel form)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Enter a building name.";
            return RedirectToAction("Details", "Jobs", new { id = jobId });
        }

        try
        {
            var building = await _jobService.AddBuildingAsync(jobId, form.Name, form.Notes);
            if (building is null)
            {
                return NotFound();
            }

            TempData["Message"] = $"Added {building.Name}.";
        }
        catch (ValidationFailedException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction("Details", "Jobs", new { id = jobId });
    }

    [HttpGet("{buildingId:guid}"), Authorize(Policy = AppPolicies.ManageSchedule)]
    public async Task<IActionResult> Details(Guid jobId, Guid buildingId)
    {
        var job = await _jobService.GetAsync(jobId);
        var building = job?.Buildings.FirstOrDefault(b => b.Id == buildingId);
        if (job is null || building is null)
        {
            return NotFound();
        }

        return View(new BuildingDetailsViewModel
        {
            JobId = job.Id,
            JobName = job.Name,
            Building = BuildingViewModel.FromEntity(building),
            AllowedExtensionsText = string.Join(", ", BuildingFileService.AllowedExtensions),
            MaxFileSizeMegabytes = _fileService.MaxFileSizeBytes / (1024 * 1024)
        });
    }

    [HttpGet("{buildingId:guid}/Edit"), Authorize(Policy = AppPolicies.ManageSchedule)]
    public async Task<IActionResult> Edit(Guid jobId, Guid buildingId)
    {
        var job = await _jobService.GetAsync(jobId);
        var building = job?.Buildings.FirstOrDefault(b => b.Id == buildingId);
        if (job is null || building is null)
        {
            return NotFound();
        }

        return View(new BuildingFormModel { JobId = jobId, BuildingId = buildingId, JobName = job.Name, Name = building.Name, Notes = building.Notes });
    }

    [HttpPost("{buildingId:guid}/Edit"), ValidateAntiForgeryToken, Authorize(Policy = AppPolicies.ManageSchedule)]
    public async Task<IActionResult> Edit(Guid jobId, Guid buildingId, BuildingFormModel form)
    {
        form.JobId = jobId;
        form.BuildingId = buildingId;
        if (ModelState.IsValid)
        {
            try
            {
                if (!await _jobService.UpdateBuildingAsync(jobId, buildingId, form.Name, form.Notes))
                {
                    return NotFound();
                }

                TempData["Message"] = "Building updated.";
                return RedirectToAction(nameof(Details), new { jobId, buildingId });
            }
            catch (ValidationFailedException ex)
            {
                ModelState.AddErrors(ex);
            }
        }

        form.JobName = (await _jobService.GetAsync(jobId))?.Name ?? "";
        return View(form);
    }

    [HttpPost("{buildingId:guid}/Delete"), ValidateAntiForgeryToken, Authorize(Policy = AppPolicies.ManageSchedule)]
    public async Task<IActionResult> Delete(Guid jobId, Guid buildingId)
    {
        if (!await _jobService.DeleteBuildingAsync(jobId, buildingId))
        {
            return NotFound();
        }

        TempData["Message"] = "Building and its files deleted.";
        return RedirectToAction("Details", "Jobs", new { id = jobId });
    }

    /// <summary>
    /// Stores each selected file on the building. Valid files are saved even if others in the
    /// same batch are rejected; every rejection is reported back to the page.
    /// </summary>
    [HttpPost("{buildingId:guid}/Files"), ValidateAntiForgeryToken, Authorize(Policy = AppPolicies.ManageSchedule)]
    [RequestSizeLimit(StorageOptions.MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = StorageOptions.MaxUploadRequestBytes)]
    public async Task<IActionResult> Upload(Guid jobId, Guid buildingId, List<IFormFile> files)
    {
        if (files.Count == 0)
        {
            TempData["Error"] = "Choose at least one file to upload.";
            return RedirectToAction(nameof(Details), new { jobId, buildingId });
        }

        var uploaded = 0;
        var errors = new List<string>();
        foreach (var file in files)
        {
            try
            {
                await using var stream = file.OpenReadStream();
                if (await _fileService.UploadAsync(jobId, buildingId, file.FileName, stream, file.Length) is null)
                {
                    return NotFound();
                }

                uploaded++;
            }
            catch (ValidationFailedException ex)
            {
                errors.Add(ex.Message);
            }
        }

        if (uploaded > 0)
        {
            TempData["Message"] = uploaded == 1 ? "Uploaded 1 file." : $"Uploaded {uploaded} files.";
        }

        if (errors.Count > 0)
        {
            TempData["Error"] = string.Join(" ", errors);
        }

        return RedirectToAction(nameof(Details), new { jobId, buildingId });
    }

    /// <summary>
    /// Streams a stored spreadsheet under its original name. Technicians get 404 for jobs they are not
    /// assigned to, so they can't probe other jobs' files.
    /// </summary>
    [HttpGet("{buildingId:guid}/Files/{fileId:guid}"), Authorize(Policy = AppPolicies.ViewSchedule)]
    public async Task<IActionResult> Download(Guid jobId, Guid buildingId, Guid fileId)
    {
        if (!User.CanManageSchedule())
        {
            var technicianId = await _userManager.GetLinkedTechnicianIdAsync(User);
            var job = await _jobService.GetAsync(jobId);
            if (technicianId is null || job is null || !job.AssignedTechnicianIds.Contains(technicianId.Value))
            {
                return NotFound();
            }
        }

        var download = await _fileService.OpenDownloadAsync(jobId, buildingId, fileId);
        if (download is null)
        {
            return NotFound();
        }

        return File(download.Content, download.ContentType, download.FileName);
    }

    [HttpPost("{buildingId:guid}/Files/{fileId:guid}/Delete"), ValidateAntiForgeryToken, Authorize(Policy = AppPolicies.ManageSchedule)]
    public async Task<IActionResult> DeleteFile(Guid jobId, Guid buildingId, Guid fileId)
    {
        if (!await _fileService.DeleteAsync(jobId, buildingId, fileId))
        {
            return NotFound();
        }

        TempData["Message"] = "File deleted.";
        return RedirectToAction(nameof(Details), new { jobId, buildingId });
    }
}
