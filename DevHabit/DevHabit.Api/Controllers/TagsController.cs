using DevHabit.Api.Database;
using DevHabit.Api.Database.Entities;
using DevHabit.Api.DTOs.Tags;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DevHabit.Api.Controllers;

[ApiController]
[Route("tags")]
public sealed class TagsController(ApplicationDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TagsCollectionDto>> GetTags()
    {
        List<TagDto> tags = await dbContext
            .Tags
            .Select(TagQueries.ProjectToDto())
            .ToListAsync();

        TagsCollectionDto tagsCollectionDto = new() { Data = tags };

        return Ok(tagsCollectionDto);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TagDto>> GetTag(string id)
    {
        TagDto? tagDto = await dbContext
                    .Tags
                    .Where(t => t.Id == id)
                    .Select(TagQueries.ProjectToDto())
                    .FirstOrDefaultAsync();

        if (tagDto is null)
        {
            return NotFound();
        }

        return Ok(tagDto);
    }

    [HttpPost]
    public async Task<ActionResult> CreateTag(CreateTagDto dto)
    {
        Tag tag = dto.ToEntity();

        if (await dbContext.Tags.AnyAsync(t => t.Name == tag.Name))
        {
            return Conflict($"The tag '{tag.Name}' alredy exists");
        }

        dbContext.Tags.Add(tag);

        await dbContext.SaveChangesAsync();

        TagDto tagDto = tag.ToDto();

        return CreatedAtAction(nameof(GetTag), new { tagDto.Id }, tagDto);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateTag(string id, UpdateTagDto updateTagDto)
    {
        Tag? tag = await dbContext.Tags.FirstOrDefaultAsync(t => t.Id == id);

        if (tag is null)
        {
            return NotFound();
        }

        tag.UpdateFromDto(updateTagDto);

        await dbContext.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteTag(string id)
    {
        Tag? tag = await dbContext.Tags.FirstOrDefaultAsync(t => t.Id == id);

        if (tag is null)
        {
            return NotFound();
        }

        dbContext.Tags.Remove(tag);

        await dbContext.SaveChangesAsync();

        return NoContent();
    }
}
