namespace JobHunterAI.Application.Common;

public enum ErrorKind
{
	NotFound,
	Conflict
}

public sealed class Result<T>
{
	private Result(T? value, ErrorKind? error, string? message)
	{
		Value = value;
		Error = error;
		Message = message;
	}

	public bool IsSuccess => Error is null;
	public T? Value { get; }
	public ErrorKind? Error { get; }
	public string? Message { get; }

	public static Result<T> Success(T value)
	{
		ArgumentNullException.ThrowIfNull(value);
		return new Result<T>(value, null, null);
	}

	public static Result<T> NotFound(string message) => new(default, ErrorKind.NotFound, message);
	public static Result<T> Conflict(string message) => new(default, ErrorKind.Conflict, message);
}
