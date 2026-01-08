namespace DevHabit.Api.DTOs.Tags;

public class CreateTagDto
{
    public required string Name { get; set; }
    public string? Description { get; set; }
}
