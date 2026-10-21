using System.Net;
using System.Net.Mail;

namespace GrabLog.Services;

public class EmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _log;

    public EmailService(IConfiguration config, ILogger<EmailService> log)
    {
        _config = config;
        _log = log;
    }

    public bool IsConfigured()
    {
        var user = _config["SMTP_USER"] ?? Environment.GetEnvironmentVariable("SMTP_USER");
        var pass = _config["SMTP_PASS"] ?? Environment.GetEnvironmentVariable("SMTP_PASS");
        return !string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass);
    }

    public async Task SendVerificationCodeAsync(string toEmail, string code, string userName)
    {
        var host = _config["SMTP_HOST"] ?? Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.gmail.com";
        var portStr = _config["SMTP_PORT"] ?? Environment.GetEnvironmentVariable("SMTP_PORT") ?? "587";
        var user = _config["SMTP_USER"] ?? Environment.GetEnvironmentVariable("SMTP_USER") ?? "";
        var pass = _config["SMTP_PASS"] ?? Environment.GetEnvironmentVariable("SMTP_PASS") ?? "";
        var from = _config["SMTP_FROM"] ?? Environment.GetEnvironmentVariable("SMTP_FROM") ?? user;

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            throw new Exception("Email not configured. Set SMTP_USER and SMTP_PASS (Gmail App Password) on Railway.");

        using var client = new SmtpClient(host, int.Parse(portStr))
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(user, pass)
        };

        var body =
            $"Hello {userName},\n\n" +
            $"Your GRAB & LOG verification code is:\n\n" +
            $"    {code}\n\n" +
            $"This code expires in 10 minutes.\n" +
            $"If you did not try to sign in, ignore this email.\n\n" +
            $"— GRAB & LOG Tool Room";

        using var msg = new MailMessage(from, toEmail)
        {
            Subject = "GrabLog verification code: " + code,
            Body = body
        };

        await client.SendMailAsync(msg);
        _log.LogInformation("Verification code sent to {Email}", toEmail);
    }
}
