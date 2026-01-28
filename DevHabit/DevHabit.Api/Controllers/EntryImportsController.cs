using System.Net.Mime;
using Asp.Versioning;
using DevHabit.Api.Database;
using DevHabit.Api.DTOs.Common;
using DevHabit.Api.DTOs.EntryImports;
using DevHabit.Api.Entities;
using DevHabit.Api.Jobs;
using DevHabit.Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace DevHabit.Api.Controllers;

[Authorize(Roles = Roles.Member)]
[ApiController]
[Route("entries/imports")]
[ApiVersion(1.0)]
[Produces(
    MediaTypeNames.Application.Json,
    CustomMediaTypeNames.Application.JsonV1,
    CustomMediaTypeNames.Application.HateoasJson,
    CustomMediaTypeNames.Application.HateoasJsonV1)]
public sealed class EntryImportsController(
    ApplicationDbContext dbContext,
    ISchedulerFactory schedulerFactory,
    LinkService linkService,
    UserContext userContext) : ControllerBase
{
    /// <summary>
    /// Creates a new entry import job from an uploaded file and schedules background processing.
    /// </summary>
    /// <param name="createImportJobDto">Import job creation data including the file to process</param>
    /// <param name="acceptHeader">Controls HATEOAS link generation</param>
    /// <param name="validator">Validator for the import job request</param>
    /// <returns>The created import job details</returns>
    [HttpPost]
    [ProducesResponseType(typeof(EntryImportJobDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EntryImportJobDto>> CreateImportJob(
        [FromForm] CreateEntryImportJobDto createImportJobDto,
        [FromHeader] AcceptHeaderDto acceptHeader,
        IValidator<CreateEntryImportJobDto> validator)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        await validator.ValidateAsync(createImportJobDto);

        using var memoryStream = new MemoryStream();
        await createImportJobDto.File.CopyToAsync(memoryStream);

        var importJob = new EntryImportJob
        {
            Id = EntryImportJob.NewId(),
            UserId = userId,
            Status = EntryImportStatus.Pending,
            FileName = createImportJobDto.File.FileName,
            FileContent = memoryStream.ToArray(),
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.EntryImportJobs.Add(importJob);
        await dbContext.SaveChangesAsync();

        IScheduler scheduler = await schedulerFactory.GetScheduler();

        IJobDetail jobDetail = JobBuilder.Create<ProcessEntryImportJob>()
            .WithIdentity($"process-entry-import-{importJob.Id}")
            .UsingJobData("importJobId", importJob.Id)
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity($"process-entry-import-trigger-{importJob.Id}")
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(jobDetail, trigger);

        EntryImportJobDto importJobDto = importJob.ToDto();

        if (acceptHeader.IncludeLinks)
        {
            importJobDto.Links = CreateLinksForImportJob(importJob.Id);
        }

        return CreatedAtAction(nameof(GetImportJob), new { id = importJobDto.Id }, importJobDto);
    }

    /// <summary>
    /// Returns a paginated list of import jobs for the authenticated user.
    /// </summary>
    /// <param name="acceptHeader">Controls HATEOAS link generation</param>
    /// <param name="page">Page number for pagination</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <returns>A paginated list of import jobs</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PaginationResult<EntryImportJobDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginationResult<EntryImportJobDto>>> GetImportJobs(
        [FromHeader] AcceptHeaderDto acceptHeader,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        IQueryable<EntryImportJob> query = dbContext.EntryImportJobs
            .Where(j => j.UserId == userId)
            .OrderByDescending(j => j.CreatedAtUtc);

        int totalCount = await query.CountAsync();

        List<EntryImportJobDto> importJobDtos = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(EntryImportQueries.ProjectToDto())
            .ToListAsync();

        if (acceptHeader.IncludeLinks)
        {
            foreach (EntryImportJobDto dto in importJobDtos)
            {
                dto.Links = CreateLinksForImportJob(dto.Id);
            }
        }

        var result = new PaginationResult<EntryImportJobDto>
        {
            Items = importJobDtos,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        if (acceptHeader.IncludeLinks)
        {
            result.Links = CreateLinksForImportJobs(page, pageSize, result.HasNextPage, result.HasPreviousPage);
        }

        return Ok(result);
    }

    /// <summary>
    /// Returns details of a specific import job by ID.
    /// </summary>
    /// <param name="id">The import job identifier</param>
    /// <param name="acceptHeader">Controls HATEOAS link generation</param>
    /// <returns>The import job details</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(EntryImportJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EntryImportJobDto>> GetImportJob(
        string id,
        [FromHeader] AcceptHeaderDto acceptHeader)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        EntryImportJobDto? importJob = await dbContext.EntryImportJobs
            .Where(j => j.Id == id && j.UserId == userId)
            .Select(EntryImportQueries.ProjectToDto())
            .FirstOrDefaultAsync();

        if (importJob is null)
        {
            return NotFound();
        }

        if (acceptHeader.IncludeLinks)
        {
            importJob.Links = CreateLinksForImportJob(id);
        }

        return Ok(importJob);
    }

    /// <summary>
    /// Builds HATEOAS links for a single import job resource.
    /// </summary>
    private List<LinkDto> CreateLinksForImportJob(string id)
    {
        return
        [
            linkService.Create(nameof(GetImportJob), "self", HttpMethods.Get, new { id })
        ];
    }

    /// <summary>
    /// Builds HATEOAS links for paginated import job collections.
    /// </summary>
    private List<LinkDto> CreateLinksForImportJobs(int page, int pageSize, bool hasNextPage, bool hasPreviousPage)
    {
        var links = new List<LinkDto>
        {
            linkService.Create(nameof(GetImportJobs), "self", HttpMethods.Get, new { page, pageSize })
        };

        if (hasNextPage)
        {
            links.Add(linkService.Create(nameof(GetImportJobs), "next-page", HttpMethods.Get, new
            {
                page = page + 1,
                pageSize
            }));
        }

        if (hasPreviousPage)
        {
            links.Add(linkService.Create(nameof(GetImportJobs), "previous-page", HttpMethods.Get, new
            {
                page = page - 1,
                pageSize
            }));
        }

        return links;
    }
}
