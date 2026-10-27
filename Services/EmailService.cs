using System.Net;
using System.Net.Mail;

namespace GrabLog.Services;

public class EmailService
{
    private readonly ILogger<EmailService> _log;

    public EmailService(ILogger<EmailService> log) => _log = log;

    private static string Env(string key) =>
        (Environment.GetEnvironmentVariable(key) ?? "").Trim();

    public bool IsConfigured()
    {
        var user = Env("SMTP_USER");
        var pass = Env("SMTP_PASS").Replace(" ", "");
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            return false;
        if (pass.Contains("see below", StringComparison.OrdinalIgnoreCase))
            return false;
        if (pass.Contains("App Password", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    public async Task SendVerificationCodeAsync(string toEmail, string code, string userName)
    {
        var host = Env("SMTP_HOST");
        if (string.IsNullOrWhiteSpace(host)) host = "smtp.gmail.com";
        var portStr = Env("SMTP_PORT");
        if (string.IsNullOrWhiteSpace(portStr)) portStr = "587";
        var user = Env("SMTP_USER");
        var pass = Env("SMTP_PASS").Replace(" ", "");
        var from = Env("SMTP_FROM");
        if (string.IsNullOrWhiteSpace(from)) from = user;

        _log.LogWarning("GrabLog OTP for {Email}: {Code}", toEmail, code);
        _log.LogInformation("SMTP configured={Configured} userLen={UserLen} passLen={PassLen}",
            IsConfigured(), user.Length, pass.Length);

        if (!IsConfigured())
            throw new Exception("SMTP not configured. Set SMTP_USER and SMTP_PASS (Gmail App Password) on Railway.");

        var body =
            $"Hello {userName},\n\n" +
            $"Your GRAB & LOG verification code is:\n\n" +
            $"    {code}\n\n" +
            $"This code expires in 10 minutes.\n\n— GRAB & LOG Tool Room";

        Exception? last = null;
        foreach (var port in new[] { int.Parse(portStr), 587, 465 }.Distinct())
        {
            try
            {
                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(user, pass),
                    Timeout = 20000
                };
                using var msg = new MailMessage(from, toEmail)
                {
                    Subject = "GrabLog verification code: " + code,
                    Body = body
                };
                await client.SendMailAsync(msg);
                _log.LogInformation("OTP email sent via {Host}:{Port}", host, port);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                _log.LogError(ex, "SMTP failed {Host}:{Port}", host, port);
            }
        }

        throw new Exception("Email send failed: " + (last?.Message ?? "unknown"));
    }
}
