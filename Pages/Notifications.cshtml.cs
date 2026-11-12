using GrabLog.Models;
using GrabLog.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrabLog.Pages;

public class NotificationsModel : PageModel
{
    private readonly NotificationService _notifs;
    public NotificationsModel(NotificationService notifs) => _notifs = notifs;

    public List<AppNotification> Items { get; set; } = new();
    public bool IsAdmin { get; set; }
    public string UserName { get; set; } = "";
    public int OpId { get; set; }
    public string Initials { get; set; } = "—";
    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var email = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(email)) return RedirectToPage("/Index");

        IsAdmin = HttpContext.Session.GetString("UserRole") == "admin";
        UserName = HttpContext.Session.GetString("UserName") ?? "User";
        OpId = Math.Abs(email.GetHashCode()) % 100000;
        Initials = string.Concat(UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpper(p[0])));
        var role = IsAdmin ? "admin" : "member";

        try
        {
            Items = (await _notifs.GetRecentAsync(50))
                .Where(n => n.ForRole == "all" || n.ForRole == role).ToList();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostMarkAllAsync()
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            return RedirectToPage("/Index");
        await _notifs.MarkAllReadAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMarkReadAsync(string id)
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            return RedirectToPage("/Index");
        await _notifs.MarkReadAsync(id);
        return RedirectToPage();
    }
}
