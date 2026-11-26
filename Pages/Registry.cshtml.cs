using GrabLog.Models;
using GrabLog.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrabLog.Pages;

public class RegistryModel : PageModel
{
    private readonly ToolService _tools;
    public RegistryModel(ToolService tools) => _tools = tools;

    public List<Tool> Tools { get; set; } = new();
    public int Total { get; set; }
    public int InUse { get; set; }
    public int OverdueCount { get; set; }
    public int Disposed { get; set; }
    public bool IsAdmin { get; set; }
    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            return RedirectToPage("/Index");
        IsAdmin = HttpContext.Session.GetString("UserRole") == "admin";
        if (!IsAdmin) return RedirectToPage("/Inventory");

        try
        {
            Tools = await _tools.GetAllAsync();
            Total = Tools.Count;
            InUse = Tools.Count(t => t.Status == "inuse");
            OverdueCount = Tools.Count(t => t.Status == "overdue");
            Disposed = Tools.Count(t => t.Status == "disposed");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Tools = new();
        }
        return Page();
    }

    public async Task<IActionResult> OnPostDisposeAsync(string id)
    {
        if (HttpContext.Session.GetString("UserRole") != "admin") return RedirectToPage("/Inventory");
        await _tools.DisposeAsync(id, HttpContext.Session.GetString("UserName"), HttpContext.Session.GetString("UserEmail"));
        TempData["NotifyTitle"] = "🗑️ Tool disposed";
        TempData["NotifyBody"] = $"Tool {id} was disposed";
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostReturnAsync(string id)
    {
        if (HttpContext.Session.GetString("UserRole") != "admin") return RedirectToPage("/Inventory");
        await _tools.ReturnAsync(id, HttpContext.Session.GetString("UserName"), HttpContext.Session.GetString("UserEmail"));
        TempData["NotifyTitle"] = "✅ Tool returned";
        TempData["NotifyBody"] = $"Tool {id} was returned";
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostBorrowAsync(string id)
    {
        if (HttpContext.Session.GetString("UserRole") != "admin") return RedirectToPage("/Inventory");
        await _tools.BorrowAsync(id, HttpContext.Session.GetString("UserName") ?? "Admin", HttpContext.Session.GetString("UserEmail"));
        TempData["NotifyTitle"] = "📦 Tool borrowed";
        TempData["NotifyBody"] = $"Tool {id} was borrowed";
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostRestoreAsync(string id)
    {
        if (HttpContext.Session.GetString("UserRole") != "admin") return RedirectToPage("/Inventory");
        await _tools.RestoreAsync(id, "Locker A-00", HttpContext.Session.GetString("UserName"), HttpContext.Session.GetString("UserEmail"));
        TempData["NotifyTitle"] = "♻️ Tool restored";
        TempData["NotifyBody"] = $"Tool {id} restored to available";
        return RedirectToPage();
    }
}
