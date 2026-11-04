using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunterAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobMatchScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_match_scores",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobPostingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Strengths = table.Column<string[]>(type: "text[]", nullable: false),
                    Weaknesses = table.Column<string[]>(type: "text[]", nullable: false),
                    RecommendedKeywords = table.Column<string[]>(type: "text[]", nullable: false),
                    RiskFactors = table.Column<string[]>(type: "text[]", nullable: false),
                    ScoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_match_scores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_job_match_scores_candidate_profiles_CandidateProfileId",
                        column: x => x.CandidateProfileId,
                        principalSchema: "public",
                        principalTable: "candidate_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_job_match_scores_job_postings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalSchema: "public",
                        principalTable: "job_postings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_match_scores_CandidateProfileId_JobPostingId_ScoredAt",
                schema: "public",
                table: "job_match_scores",
                columns: new[] { "CandidateProfileId", "JobPostingId", "ScoredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_job_match_scores_JobPostingId",
                schema: "public",
                table: "job_match_scores",
                column: "JobPostingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_match_scores",
                schema: "public");
        }
    }
}
