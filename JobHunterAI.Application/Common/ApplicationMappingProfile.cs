using AutoMapper;
using JobHunterAI.Domain;
using DomainApplication = JobHunterAI.Domain.Application;

namespace JobHunterAI.Application.Common;

public sealed class ApplicationMappingProfile : Profile
{
	public ApplicationMappingProfile()
	{
		CreateMap<JobLocation, JobLocationDto>();
		CreateMap<JobPosting, JobDto>();
		CreateMap<DomainApplication, ApplicationDto>();
		CreateMap<ResumeVersion, ResumeVersionDto>();
		CreateMap<CoverLetter, CoverLetterDto>();
	}
}
