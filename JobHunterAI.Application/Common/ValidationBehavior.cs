using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace JobHunterAI.Application.Common;

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
	: IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
	public async Task<TResponse> Handle(
		TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
	{
		List<ValidationFailure> failures = [];
		foreach (IValidator<TRequest> validator in validators)
		{
			ValidationResult result = await validator.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken);
			foreach (ValidationFailure failure in result.Errors)
			{
				failures.Add(failure);
			}
		}

		if (failures.Count > 0)
		{
			throw new ValidationException(failures);
		}

		return await next();
	}
}
