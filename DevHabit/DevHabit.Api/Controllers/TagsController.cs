using System.Net.Mime;
using DevHabit.Api.Database;
using DevHabit.Api.DTOs.Common;
using DevHabit.Api.DTOs.Tags;
using DevHabit.Api.Entities;
using DevHabit.Api.Services;
using DevHabit.Api.Settings;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevHabit.Api.Controllers;

[ResponseCache(Duration = 120)]
[Authorize(Roles = Roles.Member)]
[ApiController]
[Route("tags")]
[Produces(
    MediaTypeNames.Application.Json,
    CustomMediaTypeNames.Application.JsonV1,
    CustomMediaTypeNames.Application.HateoasJson,
    CustomMediaTypeNames.Application.HateoasJsonV1)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class TagsController(
    ApplicationDbContext dbContext,
    UserContext userContext,
    LinkService linkService) : ControllerBase
{
    /// <summary>
    /// Gets all tags for the authenticated user
    /// </summary>
    /// <param name="acceptHeader">Controls HATEOAS link generation</param>
    /// <param name="options">Tag configuration options</param>
    /// <returns>A collection of tags</returns>
    [HttpGet]
    [ProducesResponseType<TagsCollectionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TagsCollectionDto>> GetTags(
        [FromHeader] AcceptHeaderDto acceptHeader,
        IOptions<TagsOptions> options)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        List<TagDto> tags = await dbContext
            .Tags
            .Where(t => t.UserId == userId)
            .Select(TagQueries.ProjectToDto())
            .ToListAsync();

        TagsCollectionDto tagsCollectionDto = new() { Items = tags };

        if (acceptHeader.IncludeLinks)
        {
            tagsCollectionDto.Links = CreateLinksForTags(tags.Count, options.Value.MaxAllowedTags);
            foreach (TagDto tagDto in tagsCollectionDto.Items)
            {
                tagDto.Links = CreateLinksForTag(tagDto.Id);
            }
        }

        return Ok(tagsCollectionDto);
    }

    /// <summary>
    /// Gets a specific tag by ID
    /// </summary>
    /// <param name="id">The tag identifier</param>
    /// <returns>The requested tag</returns>
    [HttpGet("{id}")]
    [ProducesResponseType<TagDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TagDto>> GetTag(string id)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        TagDto? tagDto = await dbContext
                    .Tags
                    .Where(t => t.Id == id && t.UserId == userId)
                    .Select(TagQueries.ProjectToDto())
                    .FirstOrDefaultAsync();

        if (tagDto is null)
        {
            return NotFound();
        }

        return Ok(tagDto);
    }

    /// <summary>
    /// Creates a new tag for the authenticated user
    /// </summary>
    /// <param name="createTagDto">The tag creation details</param>
    /// <param name="validator">Validator for the create request</param>
    /// <returns>The created tag</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> CreateTag(
        CreateTagDto createTagDto,
        IValidator<CreateTagDto> validator)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        await validator.ValidateAndThrowAsync(createTagDto);

        Tag tag = createTagDto.ToEntity(userId);

        if (await dbContext.Tags.AnyAsync(t => t.Name == tag.Name))
        {
            return Problem(
                detail: $"The tag '{tag.Name}' alredy exists",
                statusCode: StatusCodes.Status409Conflict);
        }

        dbContext.Tags.Add(tag);

        await dbContext.SaveChangesAsync();

        TagDto tagDto = tag.ToDto();

        return CreatedAtAction(nameof(GetTag), new { tagDto.Id }, tagDto);
    }

    /// <summary>
    /// Updates an existing tag
    /// </summary>
    /// <param name="id">The tag identifier</param>
    /// <param name="updateTagDto">The updated tag data</param>
    /// <param name="eTagStore">ETag store for concurrency control</param>
    /// <returns>No content on success</returns>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UpdateTag(string id, UpdateTagDto updateTagDto, InMemoryETagStore eTagStore)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        Tag? tag = await dbContext.Tags.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (tag is null)
        {
            return NotFound();
        }

        tag.UpdateFromDto(updateTagDto);

        await dbContext.SaveChangesAsync();
        eTagStore.SetETag(Request.Path.Value!, tag.ToDto());

        return NoContent();
    }

    /// <summary>
    /// Deletes a tag by ID
    /// </summary>
    /// <param name="id">The tag identifier</param>
    /// <returns>No content on success</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteTag(string id)
    {
        string? userId = await userContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        Tag? tag = await dbContext.Tags.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (tag is null)
        {
            return NotFound();
        }

        dbContext.Tags.Remove(tag);

        await dbContext.SaveChangesAsync();

        return NoContent();
    }

    private List<LinkDto> CreateLinksForTags(int tagsCount, int maxAllowedTags)
    {
        List<LinkDto> links =
        [
            linkService.Create(nameof(GetTags), "self", HttpMethods.Get),
        ];

        if (tagsCount < maxAllowedTags)
        {
            links.Add(linkService.Create(nameof(CreateTag), "create", HttpMethods.Post));
        }

        return links;
    }

    private List<LinkDto> CreateLinksForTag(string id)
    {
        List<LinkDto> links =
        [
            linkService.Create(nameof(GetTag), "self", HttpMethods.Get, new{ id }),
            linkService.Create(nameof(UpdateTag), "update", HttpMethods.Put, new { id }),
            linkService.Create(nameof(DeleteTag), "delete", HttpMethods.Delete, new { id })
        ];

        return links;
    }
}
