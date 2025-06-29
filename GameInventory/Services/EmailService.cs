
using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Options;
namespace GameInventory.Services;
public class EmailService : IEmailService
{
    private readonly EmailSettings _emailSettings;

    public EmailService(IOptions<EmailSettings> emailSettings)
    {
        _emailSettings = emailSettings.Value;
    }

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_emailSettings.SenderName, _emailSettings.SenderEmail));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new TextPart("plain") { Text = body };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(_emailSettings.SmtpServer, _emailSettings.SmtpPort, false);
        await smtp.AuthenticateAsync(_emailSettings.SenderEmail, _emailSettings.Password);
        await smtp.SendAsync(msg);
        await smtp.DisconnectAsync(true);
    }
}