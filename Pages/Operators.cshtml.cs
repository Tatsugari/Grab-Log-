using GrabLog.Models;
using GrabLog.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrabLog.Pages;

public class OperatorsModel : PageModel
{
    private readonly SupabaseClient _db;

    public OperatorsModel(SupabaseClient db) => _db = db;

    public string UserName { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public string Initials { get; set; } = "—";
    public int OpId { get; set; }
    public bool IsAdmin { get; set; }
    public List<Operator> AllMembers { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        UserEmail = HttpContext.Session.GetString("UserEmail") ?? "";
        if (string.IsNullOrEmpty(UserEmail))
            return RedirectToPage("/Index");

        UserName = HttpContext.Session.GetString("UserName") ?? "User";
        IsAdmin = HttpContext.Session.GetString("UserRole") == "admin";
        OpId = Math.Abs(UserEmail.GetHashCode()) % 100000;
        Initials = string.Concat(UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpper(p[0])));

        if (IsAdmin)
        {
            try
            {
                AllMembers = await _db.GetAsync<Operator>("operators", "select=*&order=name.asc");
            }
            catch { /* ignore if fails */ }
        }
        return Page();
    }

    public IActionResult OnPostLogout()
    {
        HttpContext.Session.Clear();
        return RedirectToPage("/Index");
    }
}
