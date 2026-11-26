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
        if (pass.Contains("16-char", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    public async Task SendVerificationCodeAsync(string toEmail, string code, string userName)
    {
        var host = string.IsNullOrWhiteSpace(Env("SMTP_HOST")) ? "smtp.gmail.com" : Env("SMTP_HOST");
        var portStr = string.IsNullOrWhiteSpace(Env("SMTP_PORT")) ? "587" : Env("SMTP_PORT");
        var user = Env("SMTP_USER");
        var pass = Env("SMTP_PASS").Replace(" ", "");
        var from = string.IsNullOrWhiteSpace(Env("SMTP_FROM")) ? user : Env("SMTP_FROM");

        // Always log so you can read code from Railway logs if email fails
        _log.LogWarning("GrabLog OTP for {Email}: {Code}", toEmail, code);

        if (!IsConfigured())
            throw new Exception("SMTP not configured. Set SMTP_USER and SMTP_PASS (Gmail App Password) on Railway Variables, then Deploy.");

        var body =
            $"Hello {userName},\n\n" +
            $"Your GRAB & LOG verification code is:\n\n" +
            $"    {code}\n\n" +
            $"This code expires in 10 minutes.\n\n— GRAB & LOG Tool Room";

        if (!int.TryParse(portStr, out var port)) port = 587;

        try
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(user, pass),
                Timeout = 8000 // fail fast so login does not spin forever
            };
            using var msg = new MailMessage(from, toEmail)
            {
                Subject = "GrabLog verification code: " + code,
                Body = body
            };
            await client.SendMailAsync(msg);
            _log.LogInformation("OTP email sent to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SMTP failed");
            throw new Exception(
                "Could not send Gmail code. Check App Password and Railway SMTP variables. " +
                "Or set SKIP_EMAIL_OTP=true to login without email. " +
                "Railway logs also show: GrabLog OTP. Detail: " + ex.Message);
        }
    }
}
