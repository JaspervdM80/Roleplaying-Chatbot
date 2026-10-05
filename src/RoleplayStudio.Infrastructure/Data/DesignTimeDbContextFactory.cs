using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RoleplayStudio.Infrastructure.Data;

/// <summary>Used by <c>dotnet ef</c> only; at runtime the connection string comes from Aspire.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=roleplaydb", npgsql => npgsql.UseVector())
            .Options;
        return new ApplicationDbContext(options);
    }
}
