using Microsoft.AspNetCore.Identity;
using RoleplayStudio.Infrastructure.Data;

namespace RoleplayStudio.Web.Security;

public static class DevelopmentUserSeeder
{
    public static async Task SeedDevelopmentUserAsync(this IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DevelopmentUserSeeder));
        var email = configuration["DevelopmentUser:Email"];
        var password = configuration["DevelopmentUser:Password"];
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            return;
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var result = await userManager.CreateAsync(new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true }, password);
        if (result.Succeeded)
        {
            logger.LogInformation("Seeded the development user");
        }
        else
        {
            logger.LogWarning("Could not seed the development user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Code)));
        }
    }
}
