using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace PriceWatcher.Email;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _opt;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> opt, ILogger<SmtpEmailSender> logger)
    {
        _opt = opt.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.Host) ||
            string.IsNullOrWhiteSpace(_opt.FromEmail))
        {
            _logger.LogInformation("SMTP not configured; skip email to {To}. Subject: {Subject}", toEmail, subject);
            return;
        }

        using var msg = new MailMessage
        {
            From = new MailAddress(_opt.FromEmail, _opt.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        msg.To.Add(new MailAddress(toEmail));

        using var client = new SmtpClient(_opt.Host, _opt.Port)
        {
            EnableSsl = _opt.EnableSsl
        };

        if (!string.IsNullOrWhiteSpace(_opt.Username))
        {
            client.Credentials = new NetworkCredential(_opt.Username, _opt.Password);
        }

        // SmtpClient is sync-only; run on threadpool to respect cancellation.
        await Task.Run(() => client.Send(msg), ct);
    }
}

