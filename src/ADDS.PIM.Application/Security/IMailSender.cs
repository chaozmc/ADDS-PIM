using ADDS.PIM.Contracts.Administration.V1;

namespace ADDS.PIM.Application.Security;

public sealed record MailMessageSendRequest(string SmtpHost, int SmtpPort, string SenderAddress, SmtpTlsMode TlsMode,
    string? Username, string? Password, IReadOnlyList<string> ToAddresses, string Subject, string TextBody,
    IReadOnlyList<string>? CcAddresses = null, IReadOnlyList<string>? BccAddresses = null);

public sealed record MailSendOutcome(bool Succeeded, string? ErrorMessage);

/// <summary>Sends an email over SMTP. Used both for the admin "send test email" action and for real outbound
/// notifications - the Infrastructure implementation owns the actual SMTP client dependency (MailKit);
/// Application only knows this port.</summary>
public interface IMailSender
{
    Task<MailSendOutcome> SendAsync(MailMessageSendRequest request, CancellationToken cancellationToken);
}
