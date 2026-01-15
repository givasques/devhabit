using DevHabit.Api.Entities;

namespace DevHabit.Api.DTOs.Tags;

public static class TagQueries
{
    public static System.Linq.Expressions.Expression<Func<Tag, TagDto>> ProjectToDto()
    {
        return t => new TagDto
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            CreatedAtUtc = t.CreatedAtUtc,
            UpdatedAtUtc = t.UpdatedAtUtc == null ? null : t.UpdatedAtUtc
        };
    }
}
