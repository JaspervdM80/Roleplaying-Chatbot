using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace RoleplayStudio.Infrastructure.Data;

/// <summary>Used by <c>dotnet ef</c> and tests; at runtime the connection string comes from Aspire.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public static readonly Version IdentitySchemaVersion = IdentitySchemaVersions.Version3;

    public ApplicationDbContext CreateDbContext(string[] args) => new(CreateOptions("Host=localhost;Database=roleplaydb"));

    // Identity reads its schema version from IdentityOptions in the app's services, which tools and tests do not have.
    public static DbContextOptions<ApplicationDbContext> CreateOptions(string connectionString)
    {
        var identityServices = new ServiceCollection()
            .Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchemaVersion)
            .BuildServiceProvider();

        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .UseApplicationServiceProvider(identityServices)
            .Options;
    }
}
