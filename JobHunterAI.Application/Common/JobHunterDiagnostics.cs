using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace JobHunterAI.Application.Common;

public static class JobHunterDiagnostics
{
	public const string Name = "JobHunterAI";
	public static readonly ActivitySource Activities = new(Name);
	private static readonly Meter Meter = new(Name, "1.0.0");
	private static readonly Counter<long> Operations = Meter.CreateCounter<long>("jobhunter.operations", "{operation}");
	private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("jobhunter.operation.duration", "s");
	private static readonly Counter<long> Requests = Meter.CreateCounter<long>("jobhunter.http.requests", "{request}");
	private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>("jobhunter.http.duration", "s");

	public static void RecordOperation(string category, string operation, string outcome, TimeSpan elapsed)
	{
		TagList tags = new() { { "category", category }, { "operation", operation }, { "outcome", outcome } };
		Operations.Add(1, tags);
		Duration.Record(elapsed.TotalSeconds, tags);
	}

	public static void RecordRequest(string host, string method, string route, int statusCode, TimeSpan elapsed)
	{
		TagList tags = new() { { "host", host }, { "http.request.method", method },
			{ "http.route", route }, { "http.response.status_code", statusCode } };
		Requests.Add(1, tags);
		RequestDuration.Record(elapsed.TotalSeconds, tags);
	}
}
