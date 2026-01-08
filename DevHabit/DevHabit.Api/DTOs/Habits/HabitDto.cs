using DevHabit.Api.Database.Entities;

namespace DevHabit.Api.DTOs.Habits;

public sealed record HabitsCollectionDto
{
    public List<HabitDto> Data { get; set; }
}

public sealed record HabitDto
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required HabitType Type { get; set; }
    public required FrequencyDto Frequency { get; set; }
    public required TargetDto Target { get; set; }
    public required HabitStatus Status { get; set; }
    public required bool IsArchived { get; set; }
    public DateOnly? EndDate { get; set; }
    public MilestoneDto? Milestone { get; set; }
    public required DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? LastCompletedAtUtc { get; set; }
}

public sealed class FrequencyDto
{
    public required FrequencyType Type { get; set; }
    public required int TimesPerPeriod { get; set; }
}

public sealed class TargetDto
{
    public required int Value { get; set; }
    public required string Unit { get; set; }
}

public sealed class MilestoneDto
{
    public required int Target { get; set; }
    public required int Current { get; set; }
}

