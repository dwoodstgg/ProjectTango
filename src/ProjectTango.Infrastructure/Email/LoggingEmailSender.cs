using Microsoft.Extensions.Logging;
using ProjectTango.Application.Common;

namespace ProjectTango.Infrastructure.Email;

/// <summary>The v1 email sender: it logs the message rather than delivering it, so budget
/// alerts and the like are observable in Development without external credentials. The AWS
/// target is SES (design §3.2) — a real sender replaces this behind <see cref="IEmailSender"/>
/// with no change to callers.</summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "EMAIL → {Recipients}\n  Subject: {Subject}\n  {Body}",
            string.Join(", ", message.To), message.Subject, message.Body);
        return Task.CompletedTask;
    }
}
