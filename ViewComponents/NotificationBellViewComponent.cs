using GrabLog.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrabLog.ViewComponents;

public class NotificationBellViewComponent : ViewComponent
{
    private readonly NotificationService _notifs;

    public NotificationBellViewComponent(NotificationService notifs) => _notifs = notifs;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var email = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(email))
            return View(new BellModel { Count = 0, Items = new() });

        var role = HttpContext.Session.GetString("UserRole") ?? "member";
        try
        {
            var items = await _notifs.GetRecentAsync(15);
            items = items.Where(n => n.ForRole == "all" || n.ForRole == role).ToList();
            var unread = items.Count(n => !n.IsRead);
            return View(new BellModel { Count = unread, Items = items });
        }
        catch
        {
            return View(new BellModel { Count = 0, Items = new() });
        }
    }

    public class BellModel
    {
        public int Count { get; set; }
        public List<GrabLog.Models.AppNotification> Items { get; set; } = new();
    }
}
