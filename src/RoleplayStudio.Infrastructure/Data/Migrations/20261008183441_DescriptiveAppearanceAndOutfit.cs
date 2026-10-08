using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoleplayStudio.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DescriptiveAppearanceAndOutfit : Migration
    {
        private static readonly (string Table, string Column, bool IsOutfit)[] Columns =
        [
            ("Characters", "Appearance", false),
            ("Characters", "DefaultOutfit", true),
            ("Personas", "Appearance", false),
            ("Personas", "DefaultOutfit", true),
            ("CharacterStates", "CurrentOutfit", true),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, column, isOutfit) in Columns)
            {
                var text = isOutfit ? OutfitText(column) : AppearanceText(column);
                migrationBuilder.Sql($"""ALTER TABLE "{table}" ALTER COLUMN "{column}" DROP NOT NULL, ALTER COLUMN "{column}" TYPE text USING {text};""");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, column, _) in Columns)
            {
                migrationBuilder.Sql($$"""ALTER TABLE "{{table}}" ALTER COLUMN "{{column}}" TYPE jsonb USING CASE WHEN "{{column}}" IS NULL THEN '{}'::jsonb ELSE jsonb_build_object('Notes', "{{column}}") END, ALTER COLUMN "{{column}}" SET NOT NULL;""");
            }
        }

        private static string AppearanceText(string column) => NonEmpty(Lines("E'\\n'",
            Labelled("Body", column, "BodyType"),
            Labelled("Height", column, "Height"),
            Labelled("Skin", column, "SkinTone"),
            Labelled("Face", column, "Face"),
            Labelled("Hair", column, "Hair"),
            Labelled("Eyes", column, "Eyes"),
            Labelled("Distinguishing features", column, "DistinguishingFeatures"),
            Field(column, "Notes")));

        private static string OutfitText(string column) => NonEmpty(Lines("E'\\n'",
            NonEmpty(Lines("', '", Field(column, "Top"), Field(column, "Bottom"), Field(column, "Footwear"), Field(column, "Accessories"))),
            Field(column, "Notes")));

        private static string Field(string column, string key) => $"""NULLIF(btrim("{column}" ->> '{key}'), '')""";

        private static string Labelled(string label, string column, string key) => $"'{label}: ' || {Field(column, key)}";

        // concat_ws skips nulls, so a field never set leaves no empty line or stray comma.
        private static string Lines(string separator, params string[] parts) => $"concat_ws({separator}, {string.Join(", ", parts)})";

        private static string NonEmpty(string text) => $"NULLIF({text}, '')";
    }
}
