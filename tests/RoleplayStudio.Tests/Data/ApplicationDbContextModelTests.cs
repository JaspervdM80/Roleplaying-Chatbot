using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using RoleplayStudio.Domain;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Data;

namespace RoleplayStudio.Tests.Data;

public class ApplicationDbContextModelTests
{
    private static ApplicationDbContext CreateContext() => new DesignTimeDbContextFactory().CreateDbContext([]);

    [Fact]
    public void Memory_embedding_is_a_cosine_indexed_vector()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(MemoryEntry))!;

        Assert.Equal($"vector({MemoryEntry.EmbeddingDimensions})", entity.FindProperty(nameof(MemoryEntry.Embedding))!.GetColumnType());

        var index = entity.GetIndexes().Single(i => i.Properties.Any(p => p.Name == nameof(MemoryEntry.Embedding)));
        Assert.Equal("hnsw", index.FindAnnotation("Npgsql:IndexMethod")?.Value);
    }

    [Theory]
    [InlineData(typeof(Character), nameof(Character.Appearance))]
    [InlineData(typeof(Character), nameof(Character.DefaultOutfit))]
    [InlineData(typeof(ChatSession), nameof(ChatSession.Scene))]
    [InlineData(typeof(CharacterState), nameof(CharacterState.CurrentOutfit))]
    public void Nested_state_is_stored_as_json(Type owner, string navigation)
    {
        using var db = CreateContext();
        var ownedType = db.Model.FindEntityType(owner)!.FindNavigation(navigation)!.TargetEntityType;

        Assert.True(ownedType.IsMappedToJson());
    }

    [Fact]
    public void Every_owned_entity_is_filtered_to_its_owner()
    {
        using var db = CreateContext();
        var owned = db.Model.GetEntityTypes().Where(t => typeof(OwnedEntity).IsAssignableFrom(t.ClrType)).ToList();

        Assert.Contains(owned, t => t.ClrType == typeof(ModelProfile));
        Assert.All(owned, t => Assert.NotEmpty(t.GetDeclaredQueryFilters()));
    }
}
