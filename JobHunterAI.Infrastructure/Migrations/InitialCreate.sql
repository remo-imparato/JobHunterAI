CREATE TABLE IF NOT EXISTS public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE TABLE public.candidate_profiles (
        "Id" uuid NOT NULL,
        "FullName" character varying(200) NOT NULL,
        "Email" character varying(320) NOT NULL,
        "Headline" character varying(300) NOT NULL,
        CONSTRAINT "PK_candidate_profiles" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE TABLE public.companies (
        "Id" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "WebsiteUrl" character varying(2048),
        CONSTRAINT "PK_companies" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE TABLE public.certifications (
        "Id" uuid NOT NULL,
        "CandidateProfileId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Issuer" character varying(200) NOT NULL,
        "ObtainedOn" date NOT NULL,
        "ExpiresOn" date,
        CONSTRAINT "PK_certifications" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_certifications_candidate_profiles_CandidateProfileId" FOREIGN KEY ("CandidateProfileId") REFERENCES public.candidate_profiles ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE TABLE public.education_records (
        "Id" uuid NOT NULL,
        "CandidateProfileId" uuid NOT NULL,
        "Institution" character varying(200) NOT NULL,
        "Qualification" character varying(200) NOT NULL,
        "Start" date NOT NULL,
        "End" date,
        CONSTRAINT "PK_education_records" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_education_records_candidate_profiles_CandidateProfileId" FOREIGN KEY ("CandidateProfileId") REFERENCES public.candidate_profiles ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE TABLE public.resumes (
        "Id" uuid NOT NULL,
        "CandidateProfileId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        CONSTRAINT "PK_resumes" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_resumes_candidate_profiles_CandidateProfileId" FOREIGN KEY ("CandidateProfileId") REFERENCES public.candidate_profiles ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE TABLE public.skills (
        "Id" uuid NOT NULL,
        "CandidateProfileId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        CONSTRAINT "PK_skills" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_skills_candidate_profiles_CandidateProfileId" FOREIGN KEY ("CandidateProfileId") REFERENCES public.candidate_profiles ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE TABLE public.work_experiences (
        "Id" uuid NOT NULL,
        "CandidateProfileId" uuid NOT NULL,
        "Employer" character varying(200) NOT NULL,
        "Title" character varying(200) NOT NULL,
        "Start" date NOT NULL,
        "End" date,
        "Description" text NOT NULL,
        CONSTRAINT "PK_work_experiences" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_work_experiences_candidate_profiles_CandidateProfileId" FOREIGN KEY ("CandidateProfileId") REFERENCES public.candidate_profiles ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE TABLE public.job_postings (
        "Id" uuid NOT NULL,
        "CompanyId" uuid NOT NULL,
        "ProviderName" character varying(100) NOT NULL,
        "ExternalId" character varying(300) NOT NULL,
        "Title" character varying(300) NOT NULL,
        "Description" text NOT NULL,
        "City" character varying(200),
        "Country" character varying(200),
        "IsRemote" boolean NOT NULL,
        "ApplicationUrl" character varying(2048) NOT NULL,
        "PostedAt" timestamp with time zone,
        CONSTRAINT "PK_job_postings" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_job_postings_companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES public.companies ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE TABLE public.applications (
        "Id" uuid NOT NULL,
        "CandidateProfileId" uuid NOT NULL,
        "JobPostingId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Status" integer NOT NULL,
        "ApprovedByUserId" uuid,
        "ApprovedAt" timestamp with time zone,
        CONSTRAINT "PK_applications" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_applications_candidate_profiles_CandidateProfileId" FOREIGN KEY ("CandidateProfileId") REFERENCES public.candidate_profiles ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_applications_job_postings_JobPostingId" FOREIGN KEY ("JobPostingId") REFERENCES public.job_postings ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE TABLE public.cover_letters (
        "Id" uuid NOT NULL,
        "CandidateProfileId" uuid NOT NULL,
        "JobPostingId" uuid NOT NULL,
        "ContentMarkdown" text NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_cover_letters" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_cover_letters_candidate_profiles_CandidateProfileId" FOREIGN KEY ("CandidateProfileId") REFERENCES public.candidate_profiles ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_cover_letters_job_postings_JobPostingId" FOREIGN KEY ("JobPostingId") REFERENCES public.job_postings ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE TABLE public.resume_versions (
        "Id" uuid NOT NULL,
        "ResumeId" uuid NOT NULL,
        "VersionNumber" integer NOT NULL,
        "ContentMarkdown" text NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "JobPostingId" uuid,
        CONSTRAINT "PK_resume_versions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_resume_versions_job_postings_JobPostingId" FOREIGN KEY ("JobPostingId") REFERENCES public.job_postings ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_resume_versions_resumes_ResumeId" FOREIGN KEY ("ResumeId") REFERENCES public.resumes ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE TABLE public.agent_executions (
        "Id" uuid NOT NULL,
        "CandidateProfileId" uuid NOT NULL,
        "ApplicationId" uuid,
        "AgentName" character varying(200) NOT NULL,
        "Status" integer NOT NULL,
        "StartedAt" timestamp with time zone NOT NULL,
        "CompletedAt" timestamp with time zone,
        "FailureCode" character varying(100),
        CONSTRAINT "PK_agent_executions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_agent_executions_applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES public.applications ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_agent_executions_candidate_profiles_CandidateProfileId" FOREIGN KEY ("CandidateProfileId") REFERENCES public.candidate_profiles ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE TABLE public.application_events (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "FromStatus" integer NOT NULL,
        "ToStatus" integer NOT NULL,
        "Description" character varying(500) NOT NULL,
        "OccurredAt" timestamp with time zone NOT NULL,
        "ActorId" uuid,
        CONSTRAINT "PK_application_events" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_application_events_applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES public.applications ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_agent_executions_ApplicationId" ON public.agent_executions ("ApplicationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_agent_executions_CandidateProfileId_StartedAt" ON public.agent_executions ("CandidateProfileId", "StartedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_application_events_ApplicationId_OccurredAt" ON public.application_events ("ApplicationId", "OccurredAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_applications_CandidateProfileId_JobPostingId" ON public.applications ("CandidateProfileId", "JobPostingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_applications_JobPostingId" ON public.applications ("JobPostingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_applications_Status" ON public.applications ("Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_certifications_CandidateProfileId" ON public.certifications ("CandidateProfileId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_companies_Name" ON public.companies ("Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_cover_letters_CandidateProfileId_JobPostingId" ON public.cover_letters ("CandidateProfileId", "JobPostingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_cover_letters_JobPostingId" ON public.cover_letters ("JobPostingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_education_records_CandidateProfileId" ON public.education_records ("CandidateProfileId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_job_postings_CompanyId" ON public.job_postings ("CompanyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_job_postings_PostedAt" ON public.job_postings ("PostedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_job_postings_ProviderName_ExternalId" ON public.job_postings ("ProviderName", "ExternalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_resume_versions_JobPostingId" ON public.resume_versions ("JobPostingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_resume_versions_ResumeId_VersionNumber" ON public.resume_versions ("ResumeId", "VersionNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_resumes_CandidateProfileId" ON public.resumes ("CandidateProfileId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_skills_CandidateProfileId_Name" ON public.skills ("CandidateProfileId", "Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    CREATE INDEX "IX_work_experiences_CandidateProfileId" ON public.work_experiences ("CandidateProfileId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261104162518_InitialCreate') THEN
    INSERT INTO public."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261104162518_InitialCreate', '10.0.14');
    END IF;
END $EF$;
COMMIT;

