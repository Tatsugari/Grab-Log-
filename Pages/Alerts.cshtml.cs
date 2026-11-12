using GrabLog.Models;
using GrabLog.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrabLog.Pages;

public class AlertsModel : PageModel
{
    private readonly ToolService _tools;
    public AlertsModel(ToolService tools) => _tools = tools;

    public List<Tool> Overdue { get; set; } = new();
    public bool IsAdmin { get; set; }
    public string UserName { get; set; } = "";
    public int OpId { get; set; }
    public string Initials { get; set; } = "—";

    public async Task<IActionResult> OnGetAsync()
    {
        var email = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(email))
            return RedirectToPage("/Index");

        IsAdmin = HttpContext.Session.GetString("UserRole") == "admin";
        UserName = HttpContext.Session.GetString("UserName") ?? "User";
        OpId = Math.Abs(email.GetHashCode()) % 100000;
        Initials = string.Concat(UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpper(p[0])));
        Overdue = (await _tools.GetAllAsync()).Where(t => t.Status == "overdue").ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostReturnAsync(string id)
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            return RedirectToPage("/Index");
        var name = HttpContext.Session.GetString("UserName") ?? "User";
        var email = HttpContext.Session.GetString("UserEmail");
        await _tools.ReturnAsync(id, name, email);
        return RedirectToPage();
    }
}
