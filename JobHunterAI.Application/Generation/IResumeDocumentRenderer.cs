namespace JobHunterAI.Application.Generation;

public interface IResumeDocumentRenderer
{
	string ToHtml(string markdown, string title);

	byte[] ToDocx(string markdown);
}
