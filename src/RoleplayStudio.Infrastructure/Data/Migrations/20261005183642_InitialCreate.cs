using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace RoleplayStudio.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Age = table.Column<int>(type: "integer", nullable: false),
                    Gender = table.Column<string>(type: "text", nullable: true),
                    ShortDescription = table.Column<string>(type: "text", nullable: true),
                    Personality = table.Column<string>(type: "text", nullable: true),
                    SpeechStyle = table.Column<string>(type: "text", nullable: true),
                    Backstory = table.Column<string>(type: "text", nullable: true),
                    Boundaries = table.Column<string>(type: "text", nullable: true),
                    ImageTags = table.Column<string>(type: "text", nullable: true),
                    ImageSeed = table.Column<long>(type: "bigint", nullable: true),
                    AvatarImageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferenceImageId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Appearance = table.Column<string>(type: "jsonb", nullable: false),
                    DefaultOutfit = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Images",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    NegativePrompt = table.Column<string>(type: "text", nullable: true),
                    SourceMemoryIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: true),
                    Seed = table.Column<long>(type: "bigint", nullable: true),
                    StoragePath = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Images", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    BaseUrl = table.Column<string>(type: "text", nullable: true),
                    ModelId = table.Column<string>(type: "text", nullable: false),
                    ApiKeySetting = table.Column<string>(type: "text", nullable: true),
                    Temperature = table.Column<double>(type: "double precision", nullable: true),
                    MaxOutputTokens = table.Column<int>(type: "integer", nullable: true),
                    ContextWindow = table.Column<int>(type: "integer", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Personas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Age = table.Column<int>(type: "integer", nullable: true),
                    Gender = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Personality = table.Column<string>(type: "text", nullable: true),
                    AvatarImageId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Appearance = table.Column<string>(type: "jsonb", nullable: false),
                    DefaultOutfit = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Personas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Chatbots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Tagline = table.Column<string>(type: "text", nullable: true),
                    WorldDescription = table.Column<string>(type: "text", nullable: true),
                    ToneAndRules = table.Column<string>(type: "text", nullable: true),
                    ImageStylePreset = table.Column<string>(type: "text", nullable: true),
                    DefaultChatModelProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chatbots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chatbots_ModelProfiles_DefaultChatModelProfileId",
                        column: x => x.DefaultChatModelProfileId,
                        principalTable: "ModelProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ChatbotCharacter",
                columns: table => new
                {
                    ChatbotId = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatbotCharacter", x => new { x.ChatbotId, x.CharacterId });
                    table.ForeignKey(
                        name: "FK_ChatbotCharacter_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatbotCharacter_Chatbots_ChatbotId",
                        column: x => x.ChatbotId,
                        principalTable: "Chatbots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Scenarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatbotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Premise = table.Column<string>(type: "text", nullable: true),
                    StartingLocation = table.Column<string>(type: "text", nullable: true),
                    OpeningMessage = table.Column<string>(type: "text", nullable: true),
                    Goals = table.Column<string>(type: "text", nullable: true),
                    StartingCharacterIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scenarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scenarios_Chatbots_ChatbotId",
                        column: x => x.ChatbotId,
                        principalTable: "Chatbots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatModelProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Scene = table.Column<string>(type: "jsonb", nullable: false),
                    Summary = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatSessions_ModelProfiles_ChatModelProfileId",
                        column: x => x.ChatModelProfileId,
                        principalTable: "ModelProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChatSessions_Personas_PersonaId",
                        column: x => x.PersonaId,
                        principalTable: "Personas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatSessions_Scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "Scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CharacterStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppearanceChanges = table.Column<string>(type: "text", nullable: true),
                    RelationshipToPersona = table.Column<string>(type: "text", nullable: true),
                    Affinity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CurrentOutfit = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterStates_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CharacterStates_ChatSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Memories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(768)", nullable: true),
                    Importance = table.Column<int>(type: "integer", nullable: false),
                    RelatedCharacterIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    SourceFromSequence = table.Column<long>(type: "bigint", nullable: true),
                    SourceToSequence = table.Column<long>(type: "bigint", nullable: true),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRecalledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Memories_ChatSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    SpeakerCharacterId = table.Column<Guid>(type: "uuid", nullable: true),
                    SpeakerName = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    TokenCount = table.Column<int>(type: "integer", nullable: false),
                    ImageId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_ChatSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Characters_OwnerId",
                table: "Characters",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterStates_CharacterId",
                table: "CharacterStates",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterStates_SessionId_CharacterId",
                table: "CharacterStates",
                columns: new[] { "SessionId", "CharacterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatbotCharacter_CharacterId",
                table: "ChatbotCharacter",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_Chatbots_DefaultChatModelProfileId",
                table: "Chatbots",
                column: "DefaultChatModelProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Chatbots_OwnerId",
                table: "Chatbots",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_ChatModelProfileId",
                table: "ChatSessions",
                column: "ChatModelProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_OwnerId_LastActivityAt",
                table: "ChatSessions",
                columns: new[] { "OwnerId", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_PersonaId",
                table: "ChatSessions",
                column: "PersonaId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_ScenarioId",
                table: "ChatSessions",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Images_CharacterId",
                table: "Images",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_Images_OwnerId",
                table: "Images",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Images_SessionId",
                table: "Images",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Memories_Embedding",
                table: "Memories",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Memories_SessionId",
                table: "Memories",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SessionId_Sequence",
                table: "Messages",
                columns: new[] { "SessionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelProfiles_OwnerId_Role",
                table: "ModelProfiles",
                columns: new[] { "OwnerId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_Personas_OwnerId",
                table: "Personas",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Scenarios_ChatbotId",
                table: "Scenarios",
                column: "ChatbotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "CharacterStates");

            migrationBuilder.DropTable(
                name: "ChatbotCharacter");

            migrationBuilder.DropTable(
                name: "Images");

            migrationBuilder.DropTable(
                name: "Memories");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "ChatSessions");

            migrationBuilder.DropTable(
                name: "Personas");

            migrationBuilder.DropTable(
                name: "Scenarios");

            migrationBuilder.DropTable(
                name: "Chatbots");

            migrationBuilder.DropTable(
                name: "ModelProfiles");
        }
    }
}
