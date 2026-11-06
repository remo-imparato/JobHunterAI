using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunterAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateUsernames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_job_match_scores_candidate_profiles_CandidateProfileId",
                schema: "public",
                table: "job_match_scores");

            migrationBuilder.AddColumn<string>(
                name: "Username",
                schema: "public",
                table: "candidate_profiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE public.candidate_profiles
                SET "Username" = 'candidate-' || replace("Id"::text, '-', '');
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                schema: "public",
                table: "candidate_profiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_candidate_profiles_Username",
                schema: "public",
                table: "candidate_profiles",
                column: "Username",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_candidate_profiles_Username",
                schema: "public",
                table: "candidate_profiles",
                sql: "\"Username\" ~ '^[a-z0-9._-]{1,64}$'");

            migrationBuilder.AddForeignKey(
                name: "FK_job_match_scores_candidate_profiles_CandidateProfileId",
                schema: "public",
                table: "job_match_scores",
                column: "CandidateProfileId",
                principalSchema: "public",
                principalTable: "candidate_profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_job_match_scores_candidate_profiles_CandidateProfileId",
                schema: "public",
                table: "job_match_scores");

            migrationBuilder.DropIndex(
                name: "IX_candidate_profiles_Username",
                schema: "public",
                table: "candidate_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_candidate_profiles_Username",
                schema: "public",
                table: "candidate_profiles");

            migrationBuilder.DropColumn(
                name: "Username",
                schema: "public",
                table: "candidate_profiles");

            migrationBuilder.AddForeignKey(
                name: "FK_job_match_scores_candidate_profiles_CandidateProfileId",
                schema: "public",
                table: "job_match_scores",
                column: "CandidateProfileId",
                principalSchema: "public",
                principalTable: "candidate_profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
