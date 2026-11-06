using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunterAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkArrangement",
                schema: "public",
                table: "job_postings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "job_discovery_runs",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriteriaSnapshot = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExaminedJobs = table.Column<int>(type: "integer", nullable: false),
                    Warnings = table.Column<string[]>(type: "text[]", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_discovery_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_job_discovery_runs_candidate_profiles_CandidateProfileId",
                        column: x => x.CandidateProfileId,
                        principalSchema: "public",
                        principalTable: "candidate_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_discovery_results",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobPostingId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_discovery_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_job_discovery_results_job_discovery_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "public",
                        principalTable: "job_discovery_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_job_discovery_results_job_postings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalSchema: "public",
                        principalTable: "job_postings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_discovery_results_JobPostingId",
                schema: "public",
                table: "job_discovery_results",
                column: "JobPostingId");

            migrationBuilder.CreateIndex(
                name: "IX_job_discovery_results_RunId_JobPostingId",
                schema: "public",
                table: "job_discovery_results",
                columns: new[] { "RunId", "JobPostingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_discovery_runs_CandidateProfileId_StartedAt",
                schema: "public",
                table: "job_discovery_runs",
                columns: new[] { "CandidateProfileId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_discovery_results",
                schema: "public");

            migrationBuilder.DropTable(
                name: "job_discovery_runs",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "WorkArrangement",
                schema: "public",
                table: "job_postings");
        }
    }
}
