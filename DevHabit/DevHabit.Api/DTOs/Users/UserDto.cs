using DevHabit.Api.DTOs.Common;

namespace DevHabit.Api.DTOs.Users;

public class UserDto
{
    public string Id { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public List<LinkDto> Links { get; set; }
}
