using System;
using System.Dynamic;
using System.Net.Mime;
using Asp.Versioning;
using Asp.Versioning.Builder;
using DevHabit.Api.Controllers;
using DevHabit.Api.Database;
using DevHabit.Api.DTOs.Common;
using DevHabit.Api.DTOs.Habits;
using DevHabit.Api.Entities;
using DevHabit.Api.Extensions;
using DevHabit.Api.Migrations.Application;
using DevHabit.Api.Services;
using DevHabit.Api.Services.Sorting;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;

namespace DevHabit.Api.Endpoints;

public static class HabitEndpoints
{
    public static IEndpointRouteBuilder MapHabitEndpoints(this IEndpointRouteBuilder app)
    {
        ApiVersionSet versionSet = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1.0))
            .HasApiVersion(new ApiVersion(2.0))
            .ReportApiVersions()
            .Build();

        RouteGroupBuilder group = app.MapGroup("habits")
            .WithTags("Habits")
            .WithApiVersionSet(versionSet)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Member))
            .WithOpenApi()
            .WithMetadata(new ProducesAttribute(
                MediaTypeNames.Application.Json,
                CustomMediaTypeNames.Application.JsonV1,
                CustomMediaTypeNames.Application.JsonV2,
                CustomMediaTypeNames.Application.HateoasJson,
                CustomMediaTypeNames.Application.HateoasJsonV1,
                CustomMediaTypeNames.Application.HateoasJsonV2));

        group.MapGet("/", GetHabits)
            .WithName(nameof(GetHabits))
            .Produces<PaginationResult<HabitDto>>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

        group.MapGet("/{id}", GetHabit)
            .WithName(nameof(GetHabit))
            .Produces<HabitWithTagsDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .MapToApiVersion(1.0)
            .WithOpenApi();

        group.MapGet("/{id}", GetHabitV2)
            .WithName(nameof(GetHabitV2))
            .Produces<HabitWithTagsDtoV2>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .HasApiVersion(2.0)
            .WithOpenApi();

        group.MapPost("/", CreateHabit)
            .WithName(nameof(CreateHabit))
            .Produces<HabitDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

        group.MapPut("/{id}", UpdateHabit)
            .WithName(nameof(UpdateHabit))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        group.MapPatch("/{id}", PatchHabit)
            .WithName(nameof(PatchHabit))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        group.MapDelete("/{id}", DeleteHabit)
            .WithName(nameof(DeleteHabit))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        return app;
    }

    /// <summary>
    /// Gets a paginated list of habits for the authenticated user
    /// </summary>
    /// <returns>A paginated list of habits</returns>
    public static async Task<IResult> GetHabits(
        HttpContext context,
        UserContext userContext,
        ApplicationDbContext dbContext,
        SortMappingProvider sortMappingProvider,
        DataShapingService dataShapingService,
        LinkService linkService)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        // Extract query parameters
        var query = new HabitsQueryParameters
        {
            Page = context.Request.Query.TryGetValue("page", out StringValues page)
                && int.TryParse(page, out int p) ? p : 1,

            PageSize = context.Request.Query.TryGetValue("pageSize", out StringValues pageSize) &&
                int.TryParse(pageSize, out int ps) ? ps : 10,

            Fields = context.Request.Query["fields"].ToString(),

            Search = context.Request.Query["q"].ToString(),

            Sort = context.Request.Query["sort"].ToString(),

            Type = context.Request.Query.TryGetValue("type", out StringValues type) &&
                Enum.TryParse(type, out HabitType t) ? t : null,

            Status = context.Request.Query.TryGetValue("status", out StringValues status) &&
                Enum.TryParse(status, out HabitStatus s) ? s : null,

            Accept = context.Request.Headers.Accept.ToString()
        };

        if (!sortMappingProvider.ValidateMappings<HabitDto, Habit>(query.Sort))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"The provided sort parameter isn't valid: '{query.Sort}'");
        }

        if (!dataShapingService.Validate<HabitDto>(query.Fields))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"The provided data shaping fields aren't valid: '{query.Fields}'");
        }

        query.Search ??= query.Search?.Trim().ToLower();

        SortMapping[] sortMappings = sortMappingProvider.GetMappings<HabitDto, Habit>();

        IQueryable<HabitDto> habitsQuery = dbContext
            .Habits
            .Where(h => h.UserId == userId)
            .Where(h => query.Search == null ||
                        h.Name.ToLower().Contains(query.Search) ||
                        h.Description != null && h.Description.ToLower().Contains(query.Search))
            .Where(h => query.Type == null || h.Type == query.Type)
            .Where(h => query.Status == null || h.Status == query.Status)
            .ApplySort(query.Sort, sortMappings)
            .Select(HabitQueries.ProjectToDto());

        int totalCount = await habitsQuery.CountAsync();

        List<HabitDto> habits = await habitsQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        var paginationResult = new PaginationResult<ExpandoObject>
        {
            Items = dataShapingService.ShapeCollectionData(
                    habits,
                    query.Fields,
                    query.IncludeLinks ? h => CreateLinksForHabit(h.Id, query.Fields, linkService) : null),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount
        };

        if (query.IncludeLinks)
        {
            paginationResult.Links = CreateLinksForHabits(
            query,
            paginationResult.HasNextPage,
            paginationResult.HasPreviousPage,
            linkService);
        }

        return TypedResults.Extensions.OkWithContentNegotiation(paginationResult);
    }

    /// <summary>
    /// Gets a specific habit by ID (API v1)
    /// </summary>
    /// <returns>The requested habit</returns>
    private static async Task<IResult> GetHabit(
        string id,
        HttpContext context,
        UserContext userContext,
        ApplicationDbContext dbContext,
        DataShapingService dataShapingService,
        LinkService linkService)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        // Extract query parameters
        var query = new HabitsQueryParameters
        {
            Fields = context.Request.Query["fields"].ToString(),
            Accept = context.Request.Headers.Accept.ToString()
        };

        if (!dataShapingService.Validate<HabitWithTagsDto>(query.Fields))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"The provided data shaping fields aren't valid: '{query.Fields}'");
        }

        HabitWithTagsDto? habit = await dbContext.Habits
            .Where(h => h.Id == id && h.UserId == userId)
            .Select(HabitQueries.ProjectToDtoWithTags())
            .FirstOrDefaultAsync();

        if (habit == null)
        {
            return Results.NotFound();
        }

        ExpandoObject shapedHabitDto = dataShapingService.ShapeData(habit, query.Fields);

        if (query.IncludeLinks)
        {
            List<LinkDto> links = CreateLinksForHabit(id, query.Fields, linkService);

            shapedHabitDto.TryAdd("links", links);
        }

        return TypedResults.Extensions.OkWithContentNegotiation(shapedHabitDto);
    }

    /// <summary>
    /// Gets a specific habit by ID (API v2)
    /// </summary>
    /// <returns>The requested habit (v2 representation)</returns>
    private static async Task<IResult> GetHabitV2(
        string id,
        HttpContext context,
        UserContext userContext,
        ApplicationDbContext dbContext,
        DataShapingService dataShapingService,
        LinkService linkService)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        // Extract query parameters
        var query = new HabitsQueryParameters
        {
            Fields = context.Request.Query["fields"].ToString(),
            Accept = context.Request.Headers.Accept.ToString()
        };

        if (!dataShapingService.Validate<HabitWithTagsDtoV2>(query.Fields))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"The provided data shaping fields aren't valid: '{query.Fields}'");
        }

        HabitWithTagsDtoV2? habit = await dbContext.Habits
            .Where(h => h.Id == id && h.UserId == userId)
            .Select(HabitQueries.ProjectToDtoWithTagsV2())
            .FirstOrDefaultAsync();

        if (habit == null)
        {
            return Results.NotFound();
        }

        ExpandoObject shapedHabitDto = dataShapingService.ShapeData(habit, query.Fields);

        if (query.IncludeLinks)
        {
            List<LinkDto> links = CreateLinksForHabit(id, query.Fields, linkService);

            shapedHabitDto.TryAdd("links", links);
        }

        return TypedResults.Extensions.OkWithContentNegotiation(shapedHabitDto);
    }

    /// <summary>
    /// Creates a new habit for the authenticated user
    /// </summary>
    /// <returns>The created habit</returns>
    private static async Task<IResult> CreateHabit(
        CreateHabitDto createHabitDto,
        HttpContext context,
        UserContext userContext,
        ApplicationDbContext dbContext,
        IValidator<CreateHabitDto> validator,
        LinkService linkService)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        await validator.ValidateAndThrowAsync(createHabitDto);

        Habit habit = createHabitDto.ToEntity(userId);

        dbContext.Habits.Add(habit);

        await dbContext.SaveChangesAsync();

        HabitDto dto = habit.ToDto();

        var acceptHeader = new AcceptHeaderDto
        {
            Accept = context.Request.Headers.Accept.ToString()
        };

        if (acceptHeader.IncludeLinks)
        {
            dto.Links = CreateLinksForHabit(dto.Id, null, linkService);
        }

        return TypedResults.CreatedAtRoute(dto, nameof(GetHabit), new { dto.Id });
    }

    /// <summary>
    /// Updates an existing habit
    /// </summary>
    /// <returns>No content on success</returns>
    private static async Task<IResult> UpdateHabit(
        UserContext userContext,
        ApplicationDbContext dbContext,
        string id, 
        UpdateHabitDto updateHabitDto)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        Habit? habit = await dbContext.Habits
            .FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId);

        if (habit is null)
        {
            return Results.NotFound();
        }

        habit.UpdateFromDto(updateHabitDto);

        await dbContext.SaveChangesAsync();

        return Results.NoContent();
    }

    /// <summary>
    /// Partially updates a habit using JSON Patch
    /// </summary>
    /// <returns>No content on success</returns>
    private static async Task<IResult> PatchHabit(
        HttpContext context,
        UserContext userContext,
        ApplicationDbContext dbContext,
        string id)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        Habit? habit = await dbContext.Habits
            .FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId);

        if (habit is null)
        {
            return Results.NotFound();
        }

        HabitDto habitDto = habit.ToDto();

        using var streamReader = new StreamReader(context.Request.Body);
        JsonPatchDocument<HabitDto> patchDocument = JsonConvert
            .DeserializeObject<JsonPatchDocument<HabitDto>>(await streamReader.ReadToEndAsync())!;

        patchDocument.ApplyTo(habitDto);

        // Manual validation of DTO

        habit.Name = habitDto.Name;
        habit.Description = habitDto.Description;
        habit.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        return Results.NoContent();
    }

    /// <summary>
    /// Deletes a habit by ID
    /// </summary>
    /// <returns>No content on success</returns>
    private static async Task<IResult> DeleteHabit(
        UserContext userContext,
        ApplicationDbContext dbContext,
        string id)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        Habit? habit = await dbContext.Habits
            .FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId);

        if (habit is null)
        {
            return Results.NotFound();
        }

        dbContext.Habits.Remove(habit);

        await dbContext.SaveChangesAsync();

        return Results.NoContent();
    }

    private static List<LinkDto> CreateLinksForHabits(
        HabitsQueryParameters parameters,
        bool hasNextPage,
        bool hasPreviousPage,
        LinkService linkService)
    {
        List<LinkDto> links = [
            linkService.Create(nameof(GetHabits), "self", HttpMethods.Get, new
            {
                page = parameters.Page,
                pageSize = parameters.PageSize,
                fields = parameters.Fields,
                q = parameters.Search,
                sort = parameters.Sort,
                type = parameters.Type,
                status = parameters.Status
            }),
            linkService.Create(nameof(CreateHabit), "create", HttpMethods.Post)
        ];

        if (hasNextPage)
        {
            links.Add(
                linkService.Create(nameof(GetHabits), "next-page", HttpMethods.Get, new
                {
                    page = parameters.Page + 1,
                    pageSize = parameters.PageSize,
                    fields = parameters.Fields,
                    q = parameters.Search,
                    sort = parameters.Sort,
                    type = parameters.Type,
                    status = parameters.Status
                })
            );
        }

        if (hasPreviousPage)
        {
            links.Add(
                linkService.Create(nameof(GetHabits), "previous-page", HttpMethods.Get, new
                {
                    page = parameters.Page - 1,
                    pageSize = parameters.PageSize,
                    fields = parameters.Fields,
                    q = parameters.Search,
                    sort = parameters.Sort,
                    type = parameters.Type,
                    status = parameters.Status
                })
            );
        }

        return links;
    }

    private static List<LinkDto> CreateLinksForHabit(string id, string? fields, LinkService linkService)
    {
        List<LinkDto> links = [
            linkService.Create(nameof(GetHabit), "self", HttpMethods.Get, new { id, fields }),
            linkService.Create(nameof(UpdateHabit), "update", HttpMethods.Put, new { id }),
            linkService.Create(nameof(PatchHabit), "partial-update", HttpMethods.Patch, new { id }),
            linkService.Create(nameof(DeleteHabit), "delete", HttpMethods.Delete, new { id }),
            linkService.Create(nameof(
                HabitTagsController.UpsertHabitTags),
                "upsert-tags",
                HttpMethods.Put,
                new { habitId = id },
                HabitTagsController.Name)
        ];

        return links;
    }
}
