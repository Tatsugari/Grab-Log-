using GrabLog.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrabLog.Pages;

public class IndexModel : PageModel
{
    private readonly AuthService _auth;
    private readonly EmailService _email;

    public IndexModel(AuthService auth, EmailService email)
    {
        _auth = auth;
        _email = email;
    }

    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Pin { get; set; } = "";
    [BindProperty] public string Role { get; set; } = "member";
    [BindProperty] public string Code { get; set; } = "";
    public string Mode { get; set; } = "member";
    public string Message { get; set; } = "";
    public bool CodeSent { get; set; }

    public IActionResult OnGet(string? mode)
    {
        if (!string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            return RedirectToPage("/Inventory");

        if (mode == "verify" || !string.IsNullOrEmpty(HttpContext.Session.GetString("PendingEmail")))
        {
            Mode = "verify";
            Email = HttpContext.Session.GetString("PendingEmail") ?? "";
            CodeSent = true;
            return Page();
        }

        Mode = mode switch
        {
            "admin" => "admin",
            "welcome" => "welcome",
            _ => "member"
        };
        return Page();
    }

    public async Task<IActionResult> OnPostLoginAsync()
    {
        var required = Role == "admin" ? "admin" : "member";
        Mode = required;

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Pin))
        {
            Message = "Enter email and PIN.";
            return Page();
        }
        try
        {
            var ok = await _auth.LoginAsync(Email, Pin, required);
            if (!ok)
            {
                Message = required == "admin"
                    ? "Invalid admin credentials. Create an admin account first."
                    : "Invalid email or PIN.";
                return Page();
            }

            // Password OK → send email verification code
            var code = Random.Shared.Next(100000, 999999).ToString();
            HttpContext.Session.SetString("PendingEmail", _auth.Current!.Email);
            HttpContext.Session.SetString("PendingName", _auth.Current.Name);
            HttpContext.Session.SetString("PendingRole", _auth.Current.Role ?? required);
            HttpContext.Session.SetString("PendingCode", code);
            HttpContext.Session.SetString("PendingCodeExp", DateTime.UtcNow.AddMinutes(10).ToString("o"));

            try
            {
                await _email.SendVerificationCodeAsync(_auth.Current.Email, code, _auth.Current.Name);
                Message = "We sent a 6-digit code to your Gmail. Enter it below.";
            }
            catch (Exception mailEx)
            {
                // If SMTP not set, still allow with code shown (dev) — better fail closed in production message
                Message = "Could not send email: " + mailEx.Message +
                          " Set SMTP_USER and SMTP_PASS (Gmail App Password) on Railway.";
                // Still go to verify so admin can fix SMTP; code is in session
            }

            Mode = "verify";
            CodeSent = true;
            Email = _auth.Current.Email;
            return Page();
        }
        catch (Exception ex)
        {
            Message = "Login failed: " + ex.Message;
            return Page();
        }
    }

    public IActionResult OnPostVerifyAsync()
    {
        Mode = "verify";
        CodeSent = true;
        Email = HttpContext.Session.GetString("PendingEmail") ?? Email;

        var pending = HttpContext.Session.GetString("PendingCode");
        var expStr = HttpContext.Session.GetString("PendingCodeExp");
        if (string.IsNullOrEmpty(pending) || string.IsNullOrEmpty(expStr))
        {
            Message = "Session expired. Sign in again.";
            Mode = "member";
            return Page();
        }

        if (!DateTime.TryParse(expStr, out var exp) || DateTime.UtcNow > exp)
        {
            ClearPending();
            Message = "Code expired. Sign in again to get a new code.";
            Mode = "member";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Code) || Code.Trim() != pending)
        {
            Message = "Incorrect code. Check your Gmail and try again.";
            return Page();
        }

        // Success
        HttpContext.Session.SetString("UserEmail", HttpContext.Session.GetString("PendingEmail")!);
        HttpContext.Session.SetString("UserName", HttpContext.Session.GetString("PendingName") ?? "User");
        HttpContext.Session.SetString("UserRole", HttpContext.Session.GetString("PendingRole") ?? "member");
        ClearPending();
        return RedirectToPage("/Inventory");
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        Mode = "verify";
        CodeSent = true;
        var email = HttpContext.Session.GetString("PendingEmail");
        var name = HttpContext.Session.GetString("PendingName") ?? "User";
        Email = email ?? "";
        if (string.IsNullOrEmpty(email))
        {
            Message = "Session expired. Sign in again.";
            Mode = "member";
            return Page();
        }

        var code = Random.Shared.Next(100000, 999999).ToString();
        HttpContext.Session.SetString("PendingCode", code);
        HttpContext.Session.SetString("PendingCodeExp", DateTime.UtcNow.AddMinutes(10).ToString("o"));
        try
        {
            await _email.SendVerificationCodeAsync(email, code, name);
            Message = "A new code was sent to your Gmail.";
        }
        catch (Exception ex)
        {
            Message = "Resend failed: " + ex.Message;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostRegisterAsync()
    {
        Mode = "welcome";
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Pin))
        {
            Message = "Enter email and PIN.";
            return Page();
        }
        try
        {
            var name = Email.Contains('@') ? Email.Split('@')[0].Replace(".", " ").Replace("_", " ") : "Operator";
            name = string.Join(" ", name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpper(w[0]) + (w.Length > 1 ? w[1..].ToLower() : "")));
            var role = Role == "admin" ? "admin" : "member";
            var ok = await _auth.RegisterAsync(name, Email, Pin, role);
            if (!ok)
            {
                Message = "Account already exists. Try signing in.";
                Mode = role;
                return Page();
            }
            // After register, also require email code
            var code = Random.Shared.Next(100000, 999999).ToString();
            HttpContext.Session.SetString("PendingEmail", _auth.Current!.Email);
            HttpContext.Session.SetString("PendingName", _auth.Current.Name);
            HttpContext.Session.SetString("PendingRole", _auth.Current.Role ?? role);
            HttpContext.Session.SetString("PendingCode", code);
            HttpContext.Session.SetString("PendingCodeExp", DateTime.UtcNow.AddMinutes(10).ToString("o"));
            try
            {
                await _email.SendVerificationCodeAsync(_auth.Current.Email, code, _auth.Current.Name);
                Message = "Account created. Enter the code we sent to your Gmail.";
            }
            catch (Exception mailEx)
            {
                Message = "Account created but email failed: " + mailEx.Message;
            }
            Mode = "verify";
            CodeSent = true;
            Email = _auth.Current.Email;
            return Page();
        }
        catch (Exception ex)
        {
            Message = "Register failed: " + ex.Message;
            return Page();
        }
    }

    private void ClearPending()
    {
        HttpContext.Session.Remove("PendingEmail");
        HttpContext.Session.Remove("PendingName");
        HttpContext.Session.Remove("PendingRole");
        HttpContext.Session.Remove("PendingCode");
        HttpContext.Session.Remove("PendingCodeExp");
    }
}
