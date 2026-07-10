namespace ProjectTango.Application.Common;

/// <summary>A plain-text email to one or more recipients.</summary>
public record EmailMessage(IReadOnlyList<string> To, string Subject, string Body);

/// <summary>Outbound email. The v1 implementation logs (Development); the AWS target is SES
/// (design §3.2), which drops in behind this interface without touching callers.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
