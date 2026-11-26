using GrabLog.Models;
using GrabLog.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrabLog.Pages;

public class InventoryModel : PageModel
{
    private readonly ToolService _tools;

    public InventoryModel(ToolService tools) => _tools = tools;

    public List<Tool> Tools { get; set; } = new();
    public string Filter { get; set; } = "all";
    public string Search { get; set; } = "";
    public string? Error { get; set; }
    public int Total { get; set; }
    public int Available { get; set; }
    public int InUse { get; set; }
    public int Overdue { get; set; }
    public bool IsAdmin { get; set; }
    public string OpId { get; set; } = "";
    public string? CurrentUserRfid { get; set; }
    public string CurrentUserName { get; set; } = "";
    public string UserName => CurrentUserName;
    public string CurrentUserEmail { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(string? filter, string? q)
    {
        var email = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(email))
            return RedirectToPage("/Index");

        IsAdmin = string.Equals(HttpContext.Session.GetString("UserRole"), "admin", StringComparison.OrdinalIgnoreCase);
        CurrentUserName = HttpContext.Session.GetString("UserName") ?? "";
        CurrentUserEmail = email;
        CurrentUserRfid = HttpContext.Session.GetString("UserRfid");
        OpId = Math.Abs(email.GetHashCode()).ToString()[^4..];

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
        var rfid = HttpContext.Session.GetString("UserRfid");
        try
        {
            await _tools.BorrowAsync(id, name, email, rfid);
            TempData["NotifyTitle"] = "Tool borrowed";
            TempData["NotifyBody"] = $"{name} borrowed tool {id}";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToPage(new { filter = Request.Query["filter"].ToString(), q = Request.Query["q"].ToString() });
    }

    public async Task<IActionResult> OnPostReturnAsync(string id)
    {
        var name = HttpContext.Session.GetString("UserName") ?? "User";
        var email = HttpContext.Session.GetString("UserEmail");
        var role = HttpContext.Session.GetString("UserRole") ?? "member";
        var myRfid = Norm(HttpContext.Session.GetString("UserRfid"));

        try
        {
            var all = await _tools.GetAllAsync();
            var tool = all.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (tool == null)
            {
                TempData["Error"] = "Tool not found.";
                return RedirectToPage();
            }

            var owner = Norm(tool.BorrowedCardUid);
            var isAdmin = role.Equals("admin", StringComparison.OrdinalIgnoreCase);
            if (!isAdmin && !string.IsNullOrEmpty(owner) && owner != myRfid)
            {
                TempData["Error"] = "Access Denied: Only the original borrower can return this tool.";
                return RedirectToPage(new { filter = Request.Query["filter"].ToString(), q = Request.Query["q"].ToString() });
            }

            await _tools.ReturnAsync(id, name, email, myRfid);
            TempData["NotifyTitle"] = "Tool returned";
            TempData["NotifyBody"] = $"{name} returned tool {id}";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToPage(new { filter = Request.Query["filter"].ToString(), q = Request.Query["q"].ToString() });
    }

    private static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return new string(s.Where(c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')).ToArray()).ToUpperInvariant();
    }
}
