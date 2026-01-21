using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHabit.Api.Migrations.Application;

/// <inheritdoc />
public partial class Update_Habit : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
        """
        DELETE FROM dev_habit.habit_tags;
        DELETE FROM dev_habit.habits;
        DELETE FROM dev_habit.tags
        """);  

        migrationBuilder.AddColumn<int>(
            name: "automation_source",
            schema: "dev_habit",
            table: "habits",
            type: "integer",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "automation_source",
            schema: "dev_habit",
            table: "habits");
    }
}
