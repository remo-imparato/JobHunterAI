using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunterAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "candidate_profiles",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Headline = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_candidate_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WebsiteUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "certifications",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Issuer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ObtainedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_certifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_certifications_candidate_profiles_CandidateProfileId",
                        column: x => x.CandidateProfileId,
                        principalSchema: "public",
                        principalTable: "candidate_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "education_records",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Institution = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Qualification = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_education_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_education_records_candidate_profiles_CandidateProfileId",
                        column: x => x.CandidateProfileId,
                        principalSchema: "public",
                        principalTable: "candidate_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "resumes",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resumes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_resumes_candidate_profiles_CandidateProfileId",
                        column: x => x.CandidateProfileId,
                        principalSchema: "public",
                        principalTable: "candidate_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "skills",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_skills_candidate_profiles_CandidateProfileId",
                        column: x => x.CandidateProfileId,
                        principalSchema: "public",
                        principalTable: "candidate_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_experiences",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Employer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_experiences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_experiences_candidate_profiles_CandidateProfileId",
                        column: x => x.CandidateProfileId,
                        principalSchema: "public",
                        principalTable: "candidate_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_postings",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    City = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Country = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsRemote = table.Column<bool>(type: "boolean", nullable: false),
                    ApplicationUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_postings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_job_postings_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "public",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "applications",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobPostingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_applications_candidate_profiles_CandidateProfileId",
                        column: x => x.CandidateProfileId,
                        principalSchema: "public",
                        principalTable: "candidate_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_applications_job_postings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalSchema: "public",
                        principalTable: "job_postings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cover_letters",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobPostingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentMarkdown = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cover_letters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cover_letters_candidate_profiles_CandidateProfileId",
                        column: x => x.CandidateProfileId,
                        principalSchema: "public",
                        principalTable: "candidate_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cover_letters_job_postings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalSchema: "public",
                        principalTable: "job_postings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "resume_versions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResumeId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    ContentMarkdown = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    JobPostingId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resume_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_resume_versions_job_postings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalSchema: "public",
                        principalTable: "job_postings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_resume_versions_resumes_ResumeId",
                        column: x => x.ResumeId,
                        principalSchema: "public",
                        principalTable: "resumes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_executions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_executions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_executions_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "public",
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agent_executions_candidate_profiles_CandidateProfileId",
                        column: x => x.CandidateProfileId,
                        principalSchema: "public",
                        principalTable: "candidate_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "application_events",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: false),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_application_events_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "public",
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_executions_ApplicationId",
                schema: "public",
                table: "agent_executions",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_executions_CandidateProfileId_StartedAt",
                schema: "public",
                table: "agent_executions",
                columns: new[] { "CandidateProfileId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_application_events_ApplicationId_OccurredAt",
                schema: "public",
                table: "application_events",
                columns: new[] { "ApplicationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_applications_CandidateProfileId_JobPostingId",
                schema: "public",
                table: "applications",
                columns: new[] { "CandidateProfileId", "JobPostingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_applications_JobPostingId",
                schema: "public",
                table: "applications",
                column: "JobPostingId");

            migrationBuilder.CreateIndex(
                name: "IX_applications_Status",
                schema: "public",
                table: "applications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_certifications_CandidateProfileId",
                schema: "public",
                table: "certifications",
                column: "CandidateProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_companies_Name",
                schema: "public",
                table: "companies",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_cover_letters_CandidateProfileId_JobPostingId",
                schema: "public",
                table: "cover_letters",
                columns: new[] { "CandidateProfileId", "JobPostingId" });

            migrationBuilder.CreateIndex(
                name: "IX_cover_letters_JobPostingId",
                schema: "public",
                table: "cover_letters",
                column: "JobPostingId");

            migrationBuilder.CreateIndex(
                name: "IX_education_records_CandidateProfileId",
                schema: "public",
                table: "education_records",
                column: "CandidateProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_CompanyId",
                schema: "public",
                table: "job_postings",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_PostedAt",
                schema: "public",
                table: "job_postings",
                column: "PostedAt");

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_ProviderName_ExternalId",
                schema: "public",
                table: "job_postings",
                columns: new[] { "ProviderName", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resume_versions_JobPostingId",
                schema: "public",
                table: "resume_versions",
                column: "JobPostingId");

            migrationBuilder.CreateIndex(
                name: "IX_resume_versions_ResumeId_VersionNumber",
                schema: "public",
                table: "resume_versions",
                columns: new[] { "ResumeId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resumes_CandidateProfileId",
                schema: "public",
                table: "resumes",
                column: "CandidateProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_skills_CandidateProfileId_Name",
                schema: "public",
                table: "skills",
                columns: new[] { "CandidateProfileId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_experiences_CandidateProfileId",
                schema: "public",
                table: "work_experiences",
                column: "CandidateProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_executions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "application_events",
                schema: "public");

            migrationBuilder.DropTable(
                name: "certifications",
                schema: "public");

            migrationBuilder.DropTable(
                name: "cover_letters",
                schema: "public");

            migrationBuilder.DropTable(
                name: "education_records",
                schema: "public");

            migrationBuilder.DropTable(
                name: "resume_versions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "skills",
                schema: "public");

            migrationBuilder.DropTable(
                name: "work_experiences",
                schema: "public");

            migrationBuilder.DropTable(
                name: "applications",
                schema: "public");

            migrationBuilder.DropTable(
                name: "resumes",
                schema: "public");

            migrationBuilder.DropTable(
                name: "job_postings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "candidate_profiles",
                schema: "public");

            migrationBuilder.DropTable(
                name: "companies",
                schema: "public");
        }
    }
}
