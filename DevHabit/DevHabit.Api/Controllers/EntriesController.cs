using System.Dynamic;
using System.Net.Mime;
using Asp.Versioning;
using DevHabit.Api.Database;
using DevHabit.Api.DTOs.Common;
using DevHabit.Api.DTOs.Entries;
using DevHabit.Api.Entities;
using DevHabit.Api.Services;
using DevHabit.Api.Services.Sorting;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DevHabit.Api.Controllers;

[EnableRateLimiting("default")]
[Authorize(Roles = Roles.Member)]
[ApiController]
[Route("entries")]
[ApiVersion(1.0)]
[Produces(
    MediaTypeNames.Application.Json,
    CustomMediaTypeNames.Application.JsonV1,
    CustomMediaTypeNames.Application.HateoasJson,
    CustomMediaTypeNames.Application.HateoasJsonV1)]
public sealed class EntriesController(
    ApplicationDbContext dbContext,
    LinkService linkService,
    UserContext userContext) : ControllerBase
{
    /// <summary>
    /// Returns a paginated list of entries using offset pagination.
    /// Supports filtering, sorting, data shaping and optional HATEOAS links.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginationResult<ExpandoObject>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEntries(
        [FromQuery] EntriesQueryParameters query,
        SortMappingProvider sortMappingProvider,
        DataShapingService dataShapingService)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        if (!sortMappingProvider.ValidateMappings<EntryDto, Entry>(query.Sort))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"The provided sort parameter isn't valid: '{query.Sort}'");
        }

        if (!dataShapingService.Validate<EntryDto>(query.Fields))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"The provided data shaping fields aren't valid: '{query.Fields}'");
        }

        SortMapping[] sortMappings = sortMappingProvider.GetMappings<EntryDto, Entry>();

        IQueryable<Entry> entriesQuery = dbContext.Entries
            .Where(e => e.UserId == userId)
            .Where(e => query.HabitId == null || e.HabitId == query.HabitId)
            .Where(e => query.FromDate == null || e.Date >= query.FromDate)
            .Where(e => query.ToDate == null || e.Date <= query.ToDate)
            .Where(e => query.Source == null || e.Source == query.Source)
            .Where(e => query.IsArchived == null || e.IsArchived == query.IsArchived);

        int totalCount = await entriesQuery.CountAsync();

        List<EntryDto> entries = await entriesQuery
            .ApplySort(query.Sort, sortMappings)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(EntryQueries.ProjectToDto())
            .ToListAsync();

        var paginationResult = new PaginationResult<ExpandoObject>
        {
            Items = dataShapingService.ShapeCollectionData(
                entries,
                query.Fields,
                query.IncludeLinks ? e => CreateLinksForEntry(e.Id, query.Fields, e.IsArchived) : null),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount
        };

        if (query.IncludeLinks)
        {
            paginationResult.Links = CreateLinksForEntries(
                query,
                paginationResult.HasNextPage,
                paginationResult.HasPreviousPage);
        }

        return Ok(paginationResult);
    }

    /// <summary>
    /// Returns entries using cursor-based pagination for efficient large datasets.
    /// </summary>
    [HttpGet("cursor")]
    [ProducesResponseType(typeof(CollectionResponse<ExpandoObject>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEntriesCursor(
        [FromQuery] EntriesCursorQueryParameters query,
        DataShapingService dataShapingService)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        if (!dataShapingService.Validate<EntryDto>(query.Fields))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"The provided data shaping fields aren't valid: '{query.Fields}'");
        }

        IQueryable<Entry> entriesQuery = dbContext.Entries
            .Where(e => e.UserId == userId)
            .Where(e => query.HabitId == null || e.HabitId == query.HabitId)
            .Where(e => query.FromDate == null || e.Date >= query.FromDate)
            .Where(e => query.ToDate == null || e.Date <= query.ToDate)
            .Where(e => query.Source == null || e.Source == query.Source)
            .Where(e => query.IsArchived == null || e.IsArchived == query.IsArchived);

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var cursor = EntryCursorDto.Decode(query.Cursor);
            if (cursor is not null)
            {
                entriesQuery = entriesQuery.Where(e =>
                    e.Date < cursor.Date ||
                    e.Date == cursor.Date && string.Compare(e.Id, cursor.Id) < 0);
            }
        }

        List<EntryDto> entries = await entriesQuery
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .Take(query.Limit + 1)
            .Select(EntryQueries.ProjectToDto())
            .ToListAsync();

        bool hasNextPage = entries.Count > query.Limit;
        string? nextCursor = null;

        if (hasNextPage)
        {
            EntryDto lastEntry = entries[^1];
            nextCursor = EntryCursorDto.Encode(lastEntry.Id, lastEntry.Date);
            entries.RemoveAt(entries.Count - 1);
        }

        var collectionResponse = new CollectionResponse<ExpandoObject>
        {
            Items = dataShapingService.ShapeCollectionData(
                entries,
                query.Fields,
                query.IncludeLinks ? e => CreateLinksForEntry(e.Id, query.Fields, e.IsArchived) : null)
        };

        if (query.IncludeLinks)
        {
            collectionResponse.Links = CreateLinksForEntriesCursor(query, nextCursor);
        }

        return Ok(collectionResponse);
    }

    /// <summary>
    /// Returns a single entry by ID for the authenticated user.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ExpandoObject), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEntry(
        string id,
        [FromQuery] EntryQueryParameters query,
        DataShapingService dataShapingService)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        if (!dataShapingService.Validate<EntryDto>(query.Fields))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"The provided data shaping fields aren't valid: '{query.Fields}'");
        }

        EntryDto? entry = await dbContext.Entries
            .Where(e => e.Id == id && e.UserId == userId)
            .Select(EntryQueries.ProjectToDto())
            .FirstOrDefaultAsync();

        if (entry is null)
        {
            return NotFound();
        }

        ExpandoObject shapedEntryDto = dataShapingService.ShapeData(entry, query.Fields);

        if (query.IncludeLinks)
        {
            ((IDictionary<string, object?>)shapedEntryDto)[nameof(ILinksResponse.Links)] =
                CreateLinksForEntry(id, query.Fields, entry.IsArchived);
        }

        return Ok(shapedEntryDto);
    }

    /// <summary>
    /// Creates a new entry for a habit.
    /// </summary>
    [HttpPost]
    [IdempotentRequest]
    [ProducesResponseType(typeof(EntryDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<EntryDto>> CreateEntry(
        CreateEntryDto createEntryDto,
        [FromHeader] AcceptHeaderDto acceptHeader,
        IValidator<CreateEntryDto> validator)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        await validator.ValidateAndThrowAsync(createEntryDto);

        Habit? habit = await dbContext.Habits
            .FirstOrDefaultAsync(h => h.Id == createEntryDto.HabitId && h.UserId == userId);

        if (habit is null)
        {
            return Problem(
                detail: $"Habit with ID '{createEntryDto.HabitId}' does not exist.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Entry entry = createEntryDto.ToEntity(userId, habit);

        dbContext.Entries.Add(entry);
        await dbContext.SaveChangesAsync();

        EntryDto entryDto = entry.ToDto();

        if (acceptHeader.IncludeLinks)
        {
            entryDto.Links = CreateLinksForEntry(entry.Id, null, entry.IsArchived);
        }

        return CreatedAtAction(nameof(GetEntry), new { id = entryDto.Id }, entryDto);
    }

    /// <summary>
    /// Creates multiple entries in a single request.
    /// </summary>
    [HttpPost("batch")]
    [ProducesResponseType(typeof(List<EntryDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<List<EntryDto>>> CreateEntryBatch(
        CreateEntryBatchDto createEntryBatchDto,
        [FromHeader] AcceptHeaderDto acceptHeader,
        IValidator<CreateEntryBatchDto> validator)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        await validator.ValidateAndThrowAsync(createEntryBatchDto);

        var habitIds = createEntryBatchDto.Entries
            .Select(e => e.HabitId)
            .ToHashSet();

        List<Habit> existingHabits = await dbContext.Habits
            .Where(h => habitIds.Contains(h.Id) && h.UserId == userId)
            .ToListAsync();

        if (existingHabits.Count != habitIds.Count)
        {
            return Problem(
                detail: "One or more habit IDs is invalid",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var entries = createEntryBatchDto.Entries
            .Select(dto => dto.ToEntity(userId, existingHabits.First(h => h.Id == dto.HabitId)))
            .ToList();

        dbContext.Entries.AddRange(entries);
        await dbContext.SaveChangesAsync();

        var entryDtos = entries.Select(e => e.ToDto()).ToList();

        if (acceptHeader.IncludeLinks)
        {
            foreach (EntryDto entryDto in entryDtos)
            {
                entryDto.Links = CreateLinksForEntry(entryDto.Id, null, entryDto.IsArchived);
            }
        }

        return CreatedAtAction(nameof(GetEntries), entryDtos);
    }

    /// <summary>
    /// Updates an existing entry.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateEntry(
        string id,
        UpdateEntryDto updateEntryDto,
        IValidator<UpdateEntryDto> validator)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        await validator.ValidateAndThrowAsync(updateEntryDto);

        Entry? entry = await dbContext.Entries
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

        if (entry is null)
        {
            return NotFound();
        }

        entry.UpdateFromDto(updateEntryDto);
        await dbContext.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Archives an entry (soft delete behavior).
    /// </summary>
    [HttpPut("{id}/archive")]
    public async Task<ActionResult> ArchiveEntry(string id)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        Entry? entry = await dbContext.Entries
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

        if (entry is null)
        {
            return NotFound();
        }

        entry.IsArchived = true;
        entry.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Restores an archived entry.
    /// </summary>
    [HttpPut("{id}/un-archive")]
    public async Task<ActionResult> UnArchiveEntry(string id)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        Entry? entry = await dbContext.Entries
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

        if (entry is null)
        {
            return NotFound();
        }

        entry.IsArchived = false;
        entry.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Permanently deletes an entry.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteEntry(string id)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        Entry? entry = await dbContext.Entries
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

        if (entry is null)
        {
            return NotFound();
        }

        dbContext.Entries.Remove(entry);
        await dbContext.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Returns statistics such as daily counts and streaks for the user’s entries.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(EntryStatsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EntryStatsDto>> GetStats()
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var entries = await dbContext.Entries
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.Date)
            .Select(e => new { e.Date })
            .ToListAsync();

        if (!entries.Any())
        {
            return Ok(new EntryStatsDto
            {
                DailyStats = [],
                TotalEntries = 0,
                CurrentStreak = 0,
                LongestStreak = 0
            });
        }

        var dailyStats = entries
            .GroupBy(e => e.Date)
            .Select(g => new DailyStatsDto
            {
                Date = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(s => s.Date)
            .ToList();

        int totalEntries = entries.Count;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dates = entries.Select(e => e.Date).Distinct().OrderBy(d => d).ToList();

        int currentStreak = 0;
        int longestStreak = 0;
        int currentCount = 0;

        for (int i = dates.Count - 1; i >= 0; i--)
        {
            if (i == dates.Count - 1)
            {
                if (dates[i] == today)
                {
                    currentStreak = 1;
                }
                else
                {
                    break;
                }
            }
            else if (dates[i].AddDays(1) == dates[i + 1])
            {
                currentStreak++;
            }
            else
            {
                break;
            }
        }

        for (int i = 0; i < dates.Count; i++)
        {
            if (i == 0 || dates[i] == dates[i - 1].AddDays(1))
            {
                currentCount++;
                longestStreak = Math.Max(longestStreak, currentCount);
            }
            else
            {
                currentCount = 1;
            }
        }

        return Ok(new EntryStatsDto
        {
            DailyStats = dailyStats,
            TotalEntries = totalEntries,
            CurrentStreak = currentStreak,
            LongestStreak = longestStreak
        });
    }

    /// <summary>
    /// Builds HATEOAS links for paginated entry collections.
    /// </summary>
    private List<LinkDto> CreateLinksForEntries(
        EntriesQueryParameters parameters,
        bool hasNextPage,
        bool hasPreviousPage)
    {
        List<LinkDto> links =
        [
            linkService.Create(nameof(GetEntries), "self", HttpMethods.Get, new
            {
                page = parameters.Page,
                pageSize = parameters.PageSize,
                fields = parameters.Fields,
                sort = parameters.Sort,
                habitId = parameters.HabitId,
                fromDate = parameters.FromDate,
                toDate = parameters.ToDate,
                source = parameters.Source,
                isArchived = parameters.IsArchived
            }),
            linkService.Create(nameof(GetStats), "stats", HttpMethods.Get),
            linkService.Create(nameof(CreateEntry), "create", HttpMethods.Post),
            linkService.Create(nameof(CreateEntryBatch), "create-batch", HttpMethods.Post)
        ];

        if (hasNextPage)
        {
            links.Add(linkService.Create(nameof(GetEntries), "next-page", HttpMethods.Get, new
            {
                page = parameters.Page + 1,
                pageSize = parameters.PageSize,
                fields = parameters.Fields,
                sort = parameters.Sort,
                habitId = parameters.HabitId,
                fromDate = parameters.FromDate,
                toDate = parameters.ToDate,
                source = parameters.Source,
                isArchived = parameters.IsArchived
            }));
        }

        if (hasPreviousPage)
        {
            links.Add(linkService.Create(nameof(GetEntries), "previous-page", HttpMethods.Get, new
            {
                page = parameters.Page - 1,
                pageSize = parameters.PageSize,
                fields = parameters.Fields,
                sort = parameters.Sort,
                habitId = parameters.HabitId,
                fromDate = parameters.FromDate,
                toDate = parameters.ToDate,
                source = parameters.Source,
                isArchived = parameters.IsArchived
            }));
        }

        return links;
    }

    /// <summary>
    /// Builds HATEOAS links for a single entry resource.
    /// </summary>
    private List<LinkDto> CreateLinksForEntry(string id, string? fields, bool isArchived)
    {
        List<LinkDto> links =
        [
            linkService.Create(nameof(GetEntry), "self", HttpMethods.Get, new { id, fields }),
            linkService.Create(nameof(UpdateEntry), "update", HttpMethods.Put, new { id }),
            isArchived ?
                linkService.Create(nameof(UnArchiveEntry), "un-archive", HttpMethods.Put, new { id }) :
                linkService.Create(nameof(ArchiveEntry), "archive", HttpMethods.Put, new { id }),
            linkService.Create(nameof(DeleteEntry), "delete", HttpMethods.Delete, new { id })
        ];

        return links;
    }

    /// <summary>
    /// Builds HATEOAS links for cursor-based entry collections.
    /// </summary>
    private List<LinkDto> CreateLinksForEntriesCursor(EntriesCursorQueryParameters parameters, string? nextCursor)
    {
        List<LinkDto> links =
        [
            linkService.Create(nameof(GetEntriesCursor), "self", HttpMethods.Get, new
            {
                cursor = parameters.Cursor,
                limit = parameters.Limit,
                fields = parameters.Fields,
                habitId = parameters.HabitId,
                fromDate = parameters.FromDate,
                toDate = parameters.ToDate,
                source = parameters.Source,
                isArchived = parameters.IsArchived
            }),
            linkService.Create(nameof(GetStats), "stats", HttpMethods.Get),
            linkService.Create(nameof(CreateEntry), "create", HttpMethods.Post),
            linkService.Create(nameof(CreateEntryBatch), "create-batch", HttpMethods.Post)
        ];

        if (!string.IsNullOrWhiteSpace(nextCursor))
        {
            links.Add(linkService.Create(nameof(GetEntriesCursor), "next-page", HttpMethods.Get, new
            {
                cursor = nextCursor,
                limit = parameters.Limit,
                fields = parameters.Fields,
                habitId = parameters.HabitId,
                fromDate = parameters.FromDate,
                toDate = parameters.ToDate,
                source = parameters.Source,
                isArchived = parameters.IsArchived
            }));
        }

        return links;
    }
}
