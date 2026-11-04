using AutoMapper;
using FluentValidation;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using MediatR;
using DomainApplication = JobHunterAI.Domain.Application;

namespace JobHunterAI.Application.Applications;

public sealed record UpdateApplicationStatusCommand(
	Guid ApplicationId, Guid EventId, ApplicationStatus Status, DateTimeOffset OccurredAt, Guid? ApprovedByUserId = null)
	: IRequest<Result<ApplicationDto>>;

public sealed class UpdateApplicationStatusValidator : AbstractValidator<UpdateApplicationStatusCommand>
{
	public UpdateApplicationStatusValidator()
	{
		RuleFor(x => x.ApplicationId).NotEmpty();
		RuleFor(x => x.EventId).NotEmpty();
		RuleFor(x => x.OccurredAt).NotEqual(default(DateTimeOffset));
		RuleFor(x => x.Status).Must(status => Enum.IsDefined(status) && status != ApplicationStatus.Draft);
		RuleFor(x => x).Must(x => x.Status == ApplicationStatus.Approved
			? x.ApprovedByUserId is { } approver && approver != Guid.Empty
			: x.ApprovedByUserId is null)
			.WithMessage("A user identity is required for approval and must not be supplied for other transitions.");
	}
}

public sealed class UpdateApplicationStatusHandler(
	IRepository<DomainApplication> applications, IUnitOfWork unitOfWork, IMapper mapper)
	: IRequestHandler<UpdateApplicationStatusCommand, Result<ApplicationDto>>
{
	public async Task<Result<ApplicationDto>> Handle(UpdateApplicationStatusCommand request, CancellationToken cancellationToken)
	{
		DomainApplication? application = await applications.GetByIdAsync(request.ApplicationId, cancellationToken);
		if (application is null)
		{
			return Result<ApplicationDto>.NotFound("Application not found.");
		}

		if (request.OccurredAt < application.CreatedAt)
		{
			return Result<ApplicationDto>.Conflict("Application events must be chronological.");
		}

		foreach (ApplicationEvent applicationEvent in application.Events)
		{
			if (request.OccurredAt < applicationEvent.OccurredAt)
			{
				return Result<ApplicationDto>.Conflict("Application events must be chronological.");
			}
		}

		try
		{
			switch (request.Status)
			{
				case ApplicationStatus.ReadyForReview:
					application.RequestReview(request.EventId, request.OccurredAt);
					break;
				case ApplicationStatus.Approved when request.ApprovedByUserId is { } userId:
					application.Approve(request.EventId, userId, request.OccurredAt);
					break;
				case ApplicationStatus.Submitted:
					application.Submit(request.EventId, request.OccurredAt);
					break;
				case ApplicationStatus.Withdrawn:
					application.Withdraw(request.EventId, request.OccurredAt);
					break;
				case ApplicationStatus.Rejected:
					application.Reject(request.EventId, request.OccurredAt);
					break;
				case ApplicationStatus.Offer:
					application.RecordOffer(request.EventId, request.OccurredAt);
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(request.Status));
			}
		}
		catch (InvalidOperationException)
		{
			return Result<ApplicationDto>.Conflict("Application status transition is not permitted.");
		}

		await unitOfWork.SaveChangesAsync(cancellationToken);
		return Result<ApplicationDto>.Success(mapper.Map<ApplicationDto>(application));
	}
}
