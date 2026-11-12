using GrabLog.Models;
using GrabLog.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrabLog.Pages;

public class InventoryModel : PageModel
{
    private readonly ToolService _tools;

    public InventoryModel(ToolService tools) => _tools = tools;

    public string UserName { get; set; } = "";
    public string UserRole { get; set; } = "member";
    public bool IsAdmin => UserRole == "admin";
    public int OpId { get; set; }
    public string Filter { get; set; } = "all";
    public string Search { get; set; } = "";
    public List<Tool> Tools { get; set; } = new();
    public int Total { get; set; }
    public int Available { get; set; }
    public int InUse { get; set; }
    public int Overdue { get; set; }
    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(string? filter, string? q)
    {
        var email = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(email))
            return RedirectToPage("/Index");

        UserName = HttpContext.Session.GetString("UserName") ?? "User";
        UserRole = HttpContext.Session.GetString("UserRole") ?? "member";
        OpId = Math.Abs(email.GetHashCode()) % 100000;
        Filter = filter ?? "all";
        Search = q ?? "";

        try
        {
            var all = await _tools.GetAllAsync();
            Total = all.Count;
            Available = all.Count(t => t.Status == "available");
            InUse = all.Count(t => t.Status == "inuse");
            Overdue = all.Count(t => t.Status == "overdue");

            var list = await _tools.FilterAsync(Filter);
            if (!string.IsNullOrWhiteSpace(Search))
            {
                var s = Search.Trim().ToLowerInvariant();
                list = list.Where(t =>
                    (t.Name ?? "").ToLowerInvariant().Contains(s) ||
                    (t.Id ?? "").ToLowerInvariant().Contains(s) ||
                    (t.Location ?? "").ToLowerInvariant().Contains(s) ||
                    (t.LoggedTo ?? "").ToLowerInvariant().Contains(s) ||
                    (t.Status ?? "").ToLowerInvariant().Contains(s)
                ).ToList();
            }
            Tools = list;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Tools = new();
        }
        return Page();
    }

    public async Task<IActionResult> OnPostBorrowAsync(string id)
    {
        var name = HttpContext.Session.GetString("UserName") ?? "User";
        var email = HttpContext.Session.GetString("UserEmail");
        try
        {
            await _tools.BorrowAsync(id, name, email);
            TempData["NotifyTitle"] = "📦 Tool borrowed";
            TempData["NotifyBody"] = $"{name} borrowed tool {id}";
            TempData["NotifyTag"] = "borrow-" + id + "-" + DateTime.UtcNow.Ticks;
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToPage(new { filter = Request.Query["filter"].ToString(), q = Request.Query["q"].ToString() });
    }

    public async Task<IActionResult> OnPostReturnAsync(string id)
    {
        var name = HttpContext.Session.GetString("UserName") ?? "User";
        var email = HttpContext.Session.GetString("UserEmail");
        try
        {
            await _tools.ReturnAsync(id, name, email);
            TempData["NotifyTitle"] = "✅ Tool returned";
            TempData["NotifyBody"] = $"{name} returned tool {id}";
            TempData["NotifyTag"] = "return-" + id + "-" + DateTime.UtcNow.Ticks;
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToPage(new { filter = Request.Query["filter"].ToString(), q = Request.Query["q"].ToString() });
    }
}
