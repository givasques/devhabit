using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DevHabit.Api.DTOs.GitHub;
using DevHabit.IntegrationTests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using System.Net;
using System.Net.Mime;
using Newtonsoft.Json;
using System.Net.Http.Json;

namespace DevHabit.IntegrationTests.Tests;

public sealed class GitHubTests(DevHabitWebAppFactory factory) : IntegrationTestFixture(factory)
{
    private const string TestAccessToken = "gho_test123456789";

    private static readonly GitHubUserProfileDto User = new(
        Login: "testuser",
        Name: "Test User",
        AvatarUrl: "https://github.com/testuser.png",
        Bio: "Test bio",
        PublicRepos: 10,
        Followers: 20,
        Following: 30);

    [Fact]
    public async Task GetProfile_ShouldReturnUserProfile_WhenAccessTokenIsValid()
    {
        // Arrange
        WireMockServer
            .Given(Request
                .Create()
                .WithPath("/user")
                .WithHeader("Authorization", $"Bearer {TestAccessToken}")
                .UsingGet())
            .RespondWith(Response
                .Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", MediaTypeNames.Application.Json)
                .WithBodyAsJson(User));

        HttpClient client = await CreateAuthenticatedClientAsync();

        var dto = new StoreGitHubAccessTokenDto
        {
            AccessToken = TestAccessToken,
            ExpiresInDays = 30
        };
        await client.PutAsJsonAsync(Routes.GitHub.StoreAccessToken, dto);

        // Act
        HttpResponseMessage response = await client.GetAsync(Routes.GitHub.GetProfile);
        response.EnsureSuccessStatusCode();

        // Assert
        var profile = JsonConvert.DeserializeObject<GitHubUserProfileDto>(
            await response.Content.ReadAsStringAsync());

        Assert.NotNull(profile);
        Assert.Equivalent(User, profile);
    }
}
