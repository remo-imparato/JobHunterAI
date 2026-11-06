using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunterAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_packages",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResumeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobPostingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyName = table.Column<string>(type: "text", nullable: false),
                    JobTitle = table.Column<string>(type: "text", nullable: false),
                    JobDescription = table.Column<string>(type: "text", nullable: false),
                    ApplicationUrl = table.Column<string>(type: "text", nullable: false),
                    City = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    Arrangement = table.Column<int>(type: "integer", nullable: true),
                    SourceText = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResumeVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CoverLetterId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResumeMarkdown = table.Column<string>(type: "text", nullable: true),
                    CoverLetterMarkdown = table.Column<string>(type: "text", nullable: true),
                    FailureCode = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_packages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_application_packages_candidate_profiles_CandidateProfileId",
                        column: x => x.CandidateProfileId,
                        principalSchema: "public",
                        principalTable: "candidate_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_application_packages_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "public",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_application_packages_cover_letters_CoverLetterId",
                        column: x => x.CoverLetterId,
                        principalSchema: "public",
                        principalTable: "cover_letters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_application_packages_job_postings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalSchema: "public",
                        principalTable: "job_postings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_application_packages_resume_versions_ResumeVersionId",
                        column: x => x.ResumeVersionId,
                        principalSchema: "public",
                        principalTable: "resume_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_application_packages_resume_versions_SourceVersionId",
                        column: x => x.SourceVersionId,
                        principalSchema: "public",
                        principalTable: "resume_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_application_packages_resumes_ResumeId",
                        column: x => x.ResumeId,
                        principalSchema: "public",
                        principalTable: "resumes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_packages_CandidateProfileId_JobPostingId_Resume~",
                schema: "public",
                table: "application_packages",
                columns: new[] { "CandidateProfileId", "JobPostingId", "ResumeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_packages_CompanyId",
                schema: "public",
                table: "application_packages",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_application_packages_CoverLetterId",
                schema: "public",
                table: "application_packages",
                column: "CoverLetterId");

            migrationBuilder.CreateIndex(
                name: "IX_application_packages_JobPostingId",
                schema: "public",
                table: "application_packages",
                column: "JobPostingId");

            migrationBuilder.CreateIndex(
                name: "IX_application_packages_ResumeId",
                schema: "public",
                table: "application_packages",
                column: "ResumeId");

            migrationBuilder.CreateIndex(
                name: "IX_application_packages_ResumeVersionId",
                schema: "public",
                table: "application_packages",
                column: "ResumeVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_application_packages_SourceVersionId",
                schema: "public",
                table: "application_packages",
                column: "SourceVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_packages",
                schema: "public");
        }
    }
}
