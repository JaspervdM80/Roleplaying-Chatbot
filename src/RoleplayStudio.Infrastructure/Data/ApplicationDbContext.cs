using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Media;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Domain.Models;

namespace RoleplayStudio.Infrastructure.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Persona> Personas => Set<Persona>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<Chatbot> Chatbots => Set<Chatbot>();
    public DbSet<Scenario> Scenarios => Set<Scenario>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<CharacterState> CharacterStates => Set<CharacterState>();
    public DbSet<MemoryEntry> Memories => Set<MemoryEntry>();
    public DbSet<GeneratedImage> Images => Set<GeneratedImage>();
    public DbSet<ModelProfile> ModelProfiles => Set<ModelProfile>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<MessageRole>().HaveConversion<string>();
        configurationBuilder.Properties<MemoryType>().HaveConversion<string>();
        configurationBuilder.Properties<ModelRole>().HaveConversion<string>();
        configurationBuilder.Properties<ProviderKind>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasPostgresExtension("vector");

        builder.Entity<Character>(e =>
        {
            e.HasIndex(x => x.OwnerId);
            e.Property(x => x.Name).HasMaxLength(100);
            e.ToTable(t => t.HasCheckConstraint("CK_Characters_Age", $"\"Age\" >= {Character.MinimumAge}"));
            e.OwnsOne(x => x.Appearance, o => o.ToJson());
            e.OwnsOne(x => x.DefaultOutfit, o => o.ToJson());
        });

        builder.Entity<Persona>(e =>
        {
            e.HasIndex(x => x.OwnerId);
            e.Property(x => x.Name).HasMaxLength(100);
            e.OwnsOne(x => x.Appearance, o => o.ToJson());
            e.OwnsOne(x => x.DefaultOutfit, o => o.ToJson());
        });

        builder.Entity<Chatbot>(e =>
        {
            e.HasIndex(x => x.OwnerId);
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasMany(x => x.Scenarios).WithOne(x => x.Chatbot).HasForeignKey(x => x.ChatbotId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ModelProfile>().WithMany().HasForeignKey(x => x.DefaultChatModelProfileId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ChatbotCharacter>(e =>
        {
            e.HasKey(x => new { x.ChatbotId, x.CharacterId });
            e.HasOne<Chatbot>().WithMany(x => x.Cast).HasForeignKey(x => x.ChatbotId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Character).WithMany().HasForeignKey(x => x.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Scenario>(e => e.Property(x => x.Title).HasMaxLength(200));

        builder.Entity<ChatSession>(e =>
        {
            e.HasIndex(x => new { x.OwnerId, x.LastActivityAt });
            e.HasOne(x => x.Scenario).WithMany().HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Persona).WithMany().HasForeignKey(x => x.PersonaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ModelProfile>().WithMany().HasForeignKey(x => x.ChatModelProfileId).OnDelete(DeleteBehavior.SetNull);
            e.OwnsOne(x => x.Scene, o => o.ToJson());
            e.OwnsOne(x => x.Summary, o => o.ToJson());
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.CharacterStates).WithOne().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Message>(e => e.HasIndex(x => new { x.SessionId, x.Sequence }).IsUnique());

        builder.Entity<CharacterState>(e =>
        {
            e.HasIndex(x => new { x.SessionId, x.CharacterId }).IsUnique();
            e.HasOne(x => x.Character).WithMany().HasForeignKey(x => x.CharacterId).OnDelete(DeleteBehavior.Cascade);
            e.OwnsOne(x => x.CurrentOutfit, o => o.ToJson());
        });

        builder.Entity<MemoryEntry>(e =>
        {
            e.HasIndex(x => x.SessionId);
            e.HasOne<ChatSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Embedding).HasColumnType($"vector({MemoryEntry.EmbeddingDimensions})");
            e.HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
        });

        builder.Entity<GeneratedImage>(e =>
        {
            e.HasIndex(x => x.OwnerId);
            e.HasIndex(x => x.SessionId);
            e.HasIndex(x => x.CharacterId);
        });

        builder.Entity<ModelProfile>(e =>
        {
            e.HasIndex(x => new { x.OwnerId, x.Role });
            e.Property(x => x.Name).HasMaxLength(100);
        });
    }
}
