using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using WireMock.Server;

namespace DevHabit.IntegrationTests.Infrastructure;

public sealed class DevHabitWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:17.2")
        .WithDatabase("devhabit")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WireMockServer _wireMockServer;

    public WireMockServer GetWireMockServer() => _wireMockServer;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Database", _postgresContainer.GetConnectionString());
        builder.UseSetting("GitHub:BaseUrl", _wireMockServer.Urls[0]);
        builder.UseSetting("Encryption:Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Quartz.Logging.LogContext.SetCurrentLogProvider(NullLoggerFactory.Instance);
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        _wireMockServer = WireMockServer.Start();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgresContainer.StopAsync();
        _wireMockServer.Stop();
    }
}
