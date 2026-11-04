using System.Net.Mail;

namespace JobHunterAI.Domain;

public sealed record EmailAddress
{
    private EmailAddress() { }

    public EmailAddress(string value)
    {
        string address = DomainGuard.Required(value, nameof(value));
        if (!MailAddress.TryCreate(address, out MailAddress? parsed) ||
            !string.Equals(parsed.Address, address, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A valid email address is required.", nameof(value));
        }

        Value = address;
    }

    public string Value { get; private set; } = string.Empty;

    public override string ToString() => Value;
}
