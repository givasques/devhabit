using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using DevHabit.Api.DTOs.Auth;
using DevHabit.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Components.Routing;

namespace DevHabit.IntegrationTests.Tests;

public sealed class AuthenticationTests(DevHabitWebAppFactory factory) : IntegrationTestFixture(factory)
{
    [Fact]
    public async Task Register_ShouldSucceed_WithValidParameters()
    {
        // Arrange
        var dto = new RegisterUserDto
        {
            Name = "register@test.com",
            Email = "register@test.com",
            Password = "Test123!",
            ConfirmPassword = "Test123!"
        };
        HttpClient client = CreateClient();

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(Routes.Auth.Register, dto);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    }

    [Fact]
    public async Task Register_ShouldReturnAcessTokens_WithValidParameters()
    {
        // Arrange
        var dto = new RegisterUserDto
        {
            Name = "register1@test.com",
            Email = "register1@test.com",
            Password = "Test123!",
            ConfirmPassword = "Test123!"
        };
        HttpClient client = CreateClient();

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(Routes.Auth.Register, dto);
        response.EnsureSuccessStatusCode();


        // Assert
        AccessTokensDto? accessTokensDto = await response.Content.ReadFromJsonAsync<AccessTokensDto>();
        Assert.NotNull(accessTokensDto);
    }
}
