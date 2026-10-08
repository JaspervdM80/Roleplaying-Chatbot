using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Tests.Support;

namespace RoleplayStudio.Tests.Data;

[Collection(PostgresCollection.Name)]
public class MigrationTests(PostgresFixture postgres)
{
    private sealed class Looks
    {
        public string Name { get; set; } = "";
        public string? Appearance { get; set; }
        public string? DefaultOutfit { get; set; }
    }

    [Fact]
    public async Task Looks_and_outfits_written_as_separate_fields_become_readable_text()
    {
        await using var db = new ApplicationDbContext(postgres.SeparateDatabase("descriptive_looks"));
        await db.GetService<IMigrator>().MigrateAsync("20261006135750_AddMemoryExtractionMarker");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Characters" ("Id", "Name", "Age", "OwnerId", "CreatedAt", "UpdatedAt", "Appearance", "DefaultOutfit") VALUES
            (gen_random_uuid(), 'Mira', 30, 'owner', now(), now(), '{{"Hair":"Red curls","Eyes":" Green ","Notes":"Laughs easily"}}', '{{"Top":"An apron","Footwear":"clogs","Notes":"Flour on her sleeves"}}'),
            (gen_random_uuid(), 'Jun', 30, 'owner', now(), now(), '{{}}', '{{"Top":"  ","Bottom":null}}');
            """);

        await db.Database.MigrateAsync();

        var looks = await db.Database.SqlQueryRaw<Looks>("""SELECT "Name", "Appearance", "DefaultOutfit" FROM "Characters" ORDER BY "Name" DESC""").ToListAsync();
        Assert.Equal(("Hair: Red curls\nEyes: Green\nLaughs easily", "An apron, clogs\nFlour on her sleeves"), (looks[0].Appearance, looks[0].DefaultOutfit));
        Assert.Equal(((string?)null, (string?)null), (looks[1].Appearance, looks[1].DefaultOutfit));
    }
}
