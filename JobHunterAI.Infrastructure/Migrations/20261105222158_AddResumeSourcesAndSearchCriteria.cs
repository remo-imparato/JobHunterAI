using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunterAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResumeSourcesAndSearchCriteria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceFileName",
                schema: "public",
                table: "resumes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSha256",
                schema: "public",
                table: "resumes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceText",
                schema: "public",
                table: "resumes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSource",
                schema: "public",
                table: "resume_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                WITH sources AS (
                    SELECT DISTINCT ON ("ResumeId") "Id"
                    FROM public.resume_versions
                    WHERE "JobPostingId" IS NULL
                    ORDER BY "ResumeId", "VersionNumber", "Id"
                )
                UPDATE public.resume_versions AS version
                SET "IsSource" = TRUE
                FROM sources
                WHERE version."Id" = sources."Id";
                """);

            migrationBuilder.CreateTable(
                name: "candidate_search_criteria",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobTitles = table.Column<string[]>(type: "text[]", nullable: false),
                    Locations = table.Column<string[]>(type: "text[]", nullable: false),
                    RequiredKeywords = table.Column<string[]>(type: "text[]", nullable: false),
                    ExcludedKeywords = table.Column<string[]>(type: "text[]", nullable: false),
                    Arrangements = table.Column<int>(type: "integer", nullable: false),
                    IncludeUnknownArrangement = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_candidate_search_criteria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_candidate_search_criteria_candidate_profiles_Id",
                        column: x => x.Id,
                        principalSchema: "public",
                        principalTable: "candidate_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_resumes_CandidateProfileId_SourceSha256",
                schema: "public",
                table: "resumes",
                columns: new[] { "CandidateProfileId", "SourceSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resume_versions_ResumeId",
                schema: "public",
                table: "resume_versions",
                column: "ResumeId",
                unique: true,
                filter: "\"IsSource\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_resume_versions_SourceNotTailored",
                schema: "public",
                table: "resume_versions",
                sql: "NOT \"IsSource\" OR \"JobPostingId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "candidate_search_criteria",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_resumes_CandidateProfileId_SourceSha256",
                schema: "public",
                table: "resumes");

            migrationBuilder.DropIndex(
                name: "IX_resume_versions_ResumeId",
                schema: "public",
                table: "resume_versions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_resume_versions_SourceNotTailored",
                schema: "public",
                table: "resume_versions");

            migrationBuilder.DropColumn(
                name: "SourceFileName",
                schema: "public",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "SourceSha256",
                schema: "public",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "SourceText",
                schema: "public",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "IsSource",
                schema: "public",
                table: "resume_versions");
        }
    }
}
