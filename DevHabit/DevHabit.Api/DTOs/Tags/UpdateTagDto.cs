namespace DevHabit.Api.DTOs.Tags;

public class UpdateTagDto
{
    public required string Name { get; set; }
    public string? Description { get; set; }
}
