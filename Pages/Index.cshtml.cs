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
    public string Mode { get; set; } = "login";
    public string Message { get; set; } = "";
    public bool CodeSent { get; set; }

    private static bool SkipOtpFlag =>
        string.Equals(Environment.GetEnvironmentVariable("SKIP_EMAIL_OTP"), "true", StringComparison.OrdinalIgnoreCase);

    private bool ShouldSkipOtp => SkipOtpFlag || !_email.IsConfigured();

    public IActionResult OnGet(string? mode)
    {
        if (!string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            return RedirectToPage("/Inventory");

        if (mode is "welcome" or "clear" or "login")
        {
            ClearPending();
            Mode = mode == "welcome" ? "welcome" : "login";
            return Page();
        }

        if (!string.IsNullOrEmpty(HttpContext.Session.GetString("PendingEmail")))
        {
            Mode = "verify";
            Email = HttpContext.Session.GetString("PendingEmail") ?? "";
            CodeSent = true;
            return Page();
        }

        Mode = "login";
        return Page();
    }

    private void FinishLogin(string email, string name, string role)
    {
        HttpContext.Session.SetString("UserEmail", email);
        HttpContext.Session.SetString("UserName", name);
        HttpContext.Session.SetString("UserRole", role);
        ClearPending();
    }

    public async Task<IActionResult> OnPostLoginAsync()
    {
        Mode = "login";
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Pin))
        {
            Message = "Enter email and PIN.";
            return Page();
        }
        try
        {
            // No role passed — database decides admin vs member
            var ok = await _auth.LoginAsync(Email, Pin, null);
            if (!ok)
            {
                Message = "Invalid email or PIN.";
                return Page();
            }

            var role = _auth.Current!.Role ?? "member";

            if (ShouldSkipOtp)
            {
                FinishLogin(_auth.Current.Email, _auth.Current.Name, role);
                return RedirectToPage("/Inventory");
            }

            var code = Random.Shared.Next(100000, 999999).ToString();
            HttpContext.Session.SetString("PendingEmail", _auth.Current.Email);
            HttpContext.Session.SetString("PendingName", _auth.Current.Name);
            HttpContext.Session.SetString("PendingRole", role);
            HttpContext.Session.SetString("PendingCode", code);
            HttpContext.Session.SetString("PendingCodeExp", DateTime.UtcNow.AddMinutes(10).ToString("o"));

            try
            {
                await _email.SendVerificationCodeAsync(_auth.Current.Email, code, _auth.Current.Name);
                Message = "We sent a 6-digit code to your Gmail.";
            }
            catch
            {
                FinishLogin(_auth.Current.Email, _auth.Current.Name, role);
                return RedirectToPage("/Inventory");
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
            ClearPending();
            Message = "Session expired. Sign in again.";
            Mode = "login";
            return Page();
        }

        if (!DateTime.TryParse(expStr, out var exp) || DateTime.UtcNow > exp)
        {
            ClearPending();
            Message = "Code expired. Sign in again.";
            Mode = "login";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Code) || Code.Trim() != pending)
        {
            Message = "Incorrect code.";
            return Page();
        }

        FinishLogin(
            HttpContext.Session.GetString("PendingEmail")!,
            HttpContext.Session.GetString("PendingName") ?? "User",
            HttpContext.Session.GetString("PendingRole") ?? "member");
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
            Message = "Session expired.";
            Mode = "login";
            return Page();
        }

        var code = Random.Shared.Next(100000, 999999).ToString();
        HttpContext.Session.SetString("PendingCode", code);
        HttpContext.Session.SetString("PendingCodeExp", DateTime.UtcNow.AddMinutes(10).ToString("o"));
        try
        {
            await _email.SendVerificationCodeAsync(email, code, name);
            Message = "New code sent.";
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
            // New signups are always members; promote to admin in DB / Member Registry
            var ok = await _auth.RegisterAsync(name, Email, Pin, "member");
            if (!ok)
            {
                Message = "Account already exists. Sign in instead.";
                Mode = "login";
                return Page();
            }

            if (ShouldSkipOtp)
            {
                FinishLogin(_auth.Current!.Email, _auth.Current.Name, "member");
                return RedirectToPage("/Inventory");
            }

            var code = Random.Shared.Next(100000, 999999).ToString();
            HttpContext.Session.SetString("PendingEmail", _auth.Current!.Email);
            HttpContext.Session.SetString("PendingName", _auth.Current.Name);
            HttpContext.Session.SetString("PendingRole", "member");
            HttpContext.Session.SetString("PendingCode", code);
            HttpContext.Session.SetString("PendingCodeExp", DateTime.UtcNow.AddMinutes(10).ToString("o"));
            try
            {
                await _email.SendVerificationCodeAsync(_auth.Current.Email, code, _auth.Current.Name);
                Message = "Enter the code from Gmail.";
                Mode = "verify";
                CodeSent = true;
                Email = _auth.Current.Email;
                return Page();
            }
            catch
            {
                FinishLogin(_auth.Current.Email, _auth.Current.Name, "member");
                return RedirectToPage("/Inventory");
            }
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
