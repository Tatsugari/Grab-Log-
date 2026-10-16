using GrabLog.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrabLog.Pages;

public class IndexModel : PageModel
{
    private readonly AuthService _auth;

    public IndexModel(AuthService auth) => _auth = auth;

    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Pin { get; set; } = "";
    [BindProperty] public string Role { get; set; } = "member";
    public string Mode { get; set; } = "member";
    public string Message { get; set; } = "";

    public IActionResult OnGet(string? mode)
    {
        if (!string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            return RedirectToPage("/Inventory");

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
            HttpContext.Session.SetString("UserEmail", _auth.Current!.Email);
            HttpContext.Session.SetString("UserName", _auth.Current.Name);
            HttpContext.Session.SetString("UserRole", _auth.Current.Role ?? "member");
            return RedirectToPage("/Inventory");
        }
        catch (Exception ex)
        {
            Message = "Login failed: " + ex.Message;
            return Page();
        }
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
            HttpContext.Session.SetString("UserEmail", _auth.Current!.Email);
            HttpContext.Session.SetString("UserName", _auth.Current.Name);
            HttpContext.Session.SetString("UserRole", _auth.Current.Role ?? role);
            return RedirectToPage("/Inventory");
        }
        catch (Exception ex)
        {
            Message = "Register failed: " + ex.Message;
            return Page();
        }
    }
}
