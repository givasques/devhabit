namespace DevHabit.Api.DTOs.Tags;

public class UpdateTagDto
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
