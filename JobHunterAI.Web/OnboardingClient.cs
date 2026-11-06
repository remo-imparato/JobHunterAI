using System.Net.Http.Json;
using System.Text.Json;
using JobHunterAI.Application.Resumes;
using JobHunterAI.Application.Candidates;

namespace JobHunterAI.Web;

public sealed class OnboardingClient(IHttpClientFactory factory)
{
    public Task<SearchCriteriaInput> GetCriteriaAsync(Guid candidateId, CancellationToken token) =>
        SendAsync<SearchCriteriaInput>(HttpMethod.Get, $"api/candidates/{candidateId}/search-criteria", null, token);

    public Task<SearchCriteriaInput> SaveCriteriaAsync(Guid candidateId, SearchCriteriaInput input, CancellationToken token) =>
        SendAsync<SearchCriteriaInput>(HttpMethod.Put, $"api/candidates/{candidateId}/search-criteria", JsonContent.Create(input), token);

    public Task<IReadOnlyList<ResumeSummary>> ListResumesAsync(Guid candidateId, int page, CancellationToken token) =>
        SendAsync<IReadOnlyList<ResumeSummary>>(HttpMethod.Get, $"api/candidates/{candidateId}/resumes?page={page}&pageSize=10", null, token);

    public Task<ResumePreview> PreviewAsync(Guid candidateId, Guid resumeId, CancellationToken token) =>
        SendAsync<ResumePreview>(HttpMethod.Get, $"api/candidates/{candidateId}/resumes/{resumeId}", null, token);

    public Task<ResumePreview> ImportAsync(Guid candidateId, Guid resumeId, CancellationToken token) =>
        SendAsync<ResumePreview>(HttpMethod.Post, $"api/candidates/{candidateId}/resumes/{resumeId}/import", null, token);

    public async Task<ResumePreview> UploadAsync(Guid candidateId, Stream stream, string fileName, CancellationToken token)
    {
        using MultipartFormDataContent form = new();
        using StreamContent file = new(stream);
        // Browser streams cannot rewind. Buffer within the upload limit so redirects
        // or transport retries can serialize the file again without re-reading it.
        await file.LoadIntoBufferAsync(ResumeUploadLimits.MaxFileBytes, token);
        form.Add(file, "file", fileName.Replace('\\', '/').Split('/')[^1]);
        return await SendAsync<ResumePreview>(HttpMethod.Post, $"api/candidates/{candidateId}/resumes", form, token);
    }

    public async Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent? content, CancellationToken token)
    {
        using HttpClient client = factory.CreateClient("JobHunterAI.Api");
        client.Timeout = TimeSpan.FromSeconds(150);
        using HttpRequestMessage request = new(method, path) { Content = content };
        using HttpResponseMessage response = await client.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
        {
            string message = "The onboarding service is unavailable. Check API connectivity and database migrations.";
            if ((int)response.StatusCode is 400 or 404 or 409 or 413 or 415)
            {
                try
                {
                    Problem? problem = await response.Content.ReadFromJsonAsync<Problem>(token);
                    message = problem?.Title ?? "The request could not be completed. Check your input and try again.";
                }
                catch (JsonException) { message = "The request could not be completed. Check your input and try again."; }
            }
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T>(token)
            ?? throw new HttpRequestException("The onboarding service returned an empty response.");
    }

    private sealed record Problem(string? Title);
}
