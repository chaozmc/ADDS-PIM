using ADDS.PIM.Application.Security;
using ADDS.PIM.Contracts.Administration.V1;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace ADDS.PIM.Infrastructure.Mail;

/// <summary>Sends an email via MailKit. Never throws to the caller - connection, TLS and authentication failures
/// are all reported as a failed <see cref="MailSendOutcome"/> so callers (admin UI test action, notification
/// dispatcher) can show/record a message without leaking transport exception details.</summary>
public sealed class SmtpMailSender(ILogger<SmtpMailSender> logger) : IMailSender
{
    public async Task<MailSendOutcome> SendAsync(MailMessageSendRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(request.SenderAddress));
            foreach (var recipient in request.ToAddresses)
            {
                message.To.Add(MailboxAddress.Parse(recipient));
            }
            foreach (var recipient in request.CcAddresses ?? [])
            {
                message.Cc.Add(MailboxAddress.Parse(recipient));
            }
            foreach (var recipient in request.BccAddresses ?? [])
            {
                message.Bcc.Add(MailboxAddress.Parse(recipient));
            }
            message.Subject = request.Subject;
            message.Body = new TextPart("plain") { Text = request.TextBody };

            using var client = new SmtpClient();
            var secureSocketOptions = request.TlsMode switch
            {
                SmtpTlsMode.Implicit => SecureSocketOptions.SslOnConnect,
                SmtpTlsMode.Explicit => SecureSocketOptions.StartTls,
                _ => SecureSocketOptions.None,
            };
            await client.ConnectAsync(request.SmtpHost, request.SmtpPort, secureSocketOptions, cancellationToken);
            if (!string.IsNullOrEmpty(request.Username))
            {
                await client.AuthenticateAsync(request.Username, request.Password ?? string.Empty, cancellationToken);
            }
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return new MailSendOutcome(true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Mail send failed for host {SmtpHost}:{SmtpPort}.", request.SmtpHost, request.SmtpPort);
            return new MailSendOutcome(false, "Die Verbindung oder der Versand ist fehlgeschlagen. Details siehe Server-Log.");
        }
    }
}
