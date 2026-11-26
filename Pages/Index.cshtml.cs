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

        Mode = "login";
        return Page();
    }

    private void FinishLogin(string email, string name, string role, string? rfid = null)
    {
        HttpContext.Session.SetString("UserEmail", email);
        HttpContext.Session.SetString("UserName", name);
        HttpContext.Session.SetString("UserRole", role);
        if (!string.IsNullOrWhiteSpace(rfid))
            HttpContext.Session.SetString("UserRfid", rfid.Trim().ToUpperInvariant());
        else
            HttpContext.Session.Remove("UserRfid");
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
        if (Pin.Trim().Length < 8)
        {
            Message = "PIN must be at least 8 characters (example: 12345678).";
            return Page();
        }

        try
        {
            // Hard timeout so login never spins forever
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var loginTask = _auth.LoginAsync(Email.Trim(), Pin, null);
            var done = await Task.WhenAny(loginTask, Task.Delay(Timeout.Infinite, cts.Token));
            if (done != loginTask)
            {
                Message = "Login timed out (database). Check Supabase connection.";
                return Page();
            }

            var ok = await loginTask;
            if (!ok)
            {
                Message = "Invalid email or PIN.";
                return Page();
            }

            var role = _auth.Current!.Role ?? "member";
            // No Gmail wait — go straight in
            FinishLogin(_auth.Current.Email, _auth.Current.Name, role, _auth.Current.RfidUid);
            return RedirectToPage("/Inventory");
        }
        catch (Exception ex)
        {
            Message = "Login failed: " + ex.Message;
            return Page();
        }
    }

    public IActionResult OnPostVerifyAsync()
    {
        // OTP disabled for stability — redirect to normal login
        return RedirectToPage("/Index", new { mode = "login" });
    }

    public IActionResult OnPostResendAsync()
    {
        return RedirectToPage("/Index", new { mode = "login" });
    }

    public async Task<IActionResult> OnPostRegisterAsync()
    {
        Mode = "welcome";
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Pin))
        {
            Message = "Enter email and PIN.";
            return Page();
        }
        if (Pin.Trim().Length < 8)
        {
            Message = "PIN must be at least 8 characters (example: 12345678).";
            return Page();
        }
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var name = Email.Contains('@') ? Email.Split('@')[0] : "User";
            var regTask = _auth.RegisterAsync(name, Email.Trim(), Pin.Trim(), "member");
            var done = await Task.WhenAny(regTask, Task.Delay(Timeout.Infinite, cts.Token));
            if (done != regTask)
            {
                Message = "Register timed out. Check Supabase.";
                return Page();
            }
            var ok = await regTask;
            if (!ok)
            {
                Message = "Account may already exist. Try Sign in.";
                Mode = "login";
                return Page();
            }
            FinishLogin(_auth.Current!.Email, _auth.Current.Name, "member", _auth.Current.RfidUid);
            return RedirectToPage("/Inventory");
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
