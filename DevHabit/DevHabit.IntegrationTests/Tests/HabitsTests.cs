using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DevHabit.Api.DTOs.Habits;
using DevHabit.IntegrationTests.Infrastructure;
using DevHabit.Api.Entities;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using System.Net;

namespace DevHabit.IntegrationTests.Tests;

public sealed class HabitsTests(DevHabitWebAppFactory factory) : IntegrationTestFixture(factory)
{
    [Fact]
    public async Task CreateHabit_ShouldSucceed_WithValidParameters()
    {
        // Arrange
        var dto = new CreateHabitDto
        {
            Name = "Read Books",
            Description = "Read at least 20 pages of a book daily",
            Type = Api.Entities.HabitType.Measurable,
            Frequency = new FrequencyDto 
            {
                Type = FrequencyType.Daily,
                TimesPerPeriod = 1
            },
            Target = new TargetDto 
            {
                Value = 30,
                Unit = "pages"
            }
        };

        var client = await CreateAuthenticatedClientAsync();

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(Routes.Habits.Create, dto);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<HabitDto>());
    }
}
