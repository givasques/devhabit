using System.Runtime.CompilerServices;
using DevHabit.Api.Database;
using Microsoft.EntityFrameworkCore;

namespace DevHabit.Api.Extensions;

public static class DatabaseExtensions
{
    public static async Task ApplyMigrationsAsync(this WebApplication app)
    {
        using IServiceScope scope = app.Services.CreateScope();
        await using ApplicationDbContext applicationDbContext = 
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using ApplicationIdentityDbContext identityDbContext = 
            scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();

        try
        {

            await applicationDbContext.Database.MigrateAsync();
            app.Logger.LogInformation("Application Database migrations applied successfully.");

            await identityDbContext.Database.MigrateAsync();
            app.Logger.LogInformation("Identity Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "An error ocurred while applying database migrations.");
            throw;
        }
    }
}
