using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoleplayStudio.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace RoleplayStudio.Tests.Support;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    public IDbContextFactory<ApplicationDbContext> DbFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DbFactory = new TestDbContextFactory(DesignTimeDbContextFactory.CreateOptions(_container.GetConnectionString()));

        await using var db = await DbFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    /// <summary>Options for a database of its own in the same container, which migrating creates.</summary>
    public DbContextOptions<ApplicationDbContext> SeparateDatabase(string name) =>
        DesignTimeDbContextFactory.CreateOptions(new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString);

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
