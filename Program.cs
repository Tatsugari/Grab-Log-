using GrabLog.Models;
using GrabLog.Services;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpClient<SupabaseClient>();
builder.Services.AddScoped<LogService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<ToolService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession();
builder.Services.AddDistributedMemoryCache();

var app = builder.Build();

var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypes });

app.UseRouting();
app.UseSession();
app.MapRazorPages();

// Desktop notification feed
app.MapGet("/api/notifications", async (HttpContext ctx, NotificationService notifs) =>
{
    var email = ctx.Session.GetString("UserEmail");
    if (string.IsNullOrEmpty(email))
        return Results.Unauthorized();

    var role = ctx.Session.GetString("UserRole") ?? "member";
    try
    {
        var items = await notifs.GetRecentAsync(20);
        items = items.Where(n => n.ForRole == "all" || n.ForRole == role).ToList();
        var payload = items.Select(n => new
        {
            id = n.Id?.ToString(),
            type = n.Type,
            title = n.Title,
            message = n.Message,
            is_read = n.IsRead,
            created_at = n.CreatedAt
        });
        return Results.Json(payload);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// ESP32 RFID kiosk → borrow / return
app.MapPost("/api/kiosk/log-transaction", async (HttpContext ctx, KioskTransactionRequest body, ToolService tools) =>
{
    if (string.IsNullOrEmpty(ctx.Session.GetString("UserEmail")))
        return Results.Unauthorized();

    var action = (body.ActionType ?? "").Trim().ToUpperInvariant();
    var userName = string.IsNullOrWhiteSpace(body.UserName)
        ? (ctx.Session.GetString("UserName") ?? "Kiosk")
        : body.UserName.Trim();
    var email = ctx.Session.GetString("UserEmail");
    var cardUid = (body.CardUID ?? "").Trim();

    if (action is not ("BORROW" or "RETURN"))
        return Results.BadRequest(new { error = "actionType must be BORROW or RETURN" });

    try
    {
        var all = await tools.GetAllAsync();
        Tool? tool = null;

        if (!string.IsNullOrWhiteSpace(body.ToolId))
            tool = all.FirstOrDefault(t => t.Id.Equals(body.ToolId, StringComparison.OrdinalIgnoreCase));

        if (tool == null && !string.IsNullOrEmpty(cardUid))
            tool = all.FirstOrDefault(t => t.Id.Equals(cardUid, StringComparison.OrdinalIgnoreCase));

        if (tool == null)
        {
            return Results.Json(new
            {
                ok = false,
                error = "No matching tool for this scan. Select a tool on screen or register the RFID on a tool ID.",
                cardUID = cardUid,
                userName,
                action
            });
        }

        if (action == "BORROW")
        {
            if (tool.Status is "inuse" or "overdue")
                return Results.Json(new { ok = false, error = $"{tool.Name} is already in use.", toolId = tool.Id });
            if (tool.Status == "disposed")
                return Results.Json(new { ok = false, error = $"{tool.Name} is disposed.", toolId = tool.Id });

            await tools.BorrowAsync(tool.Id, userName, email);
            return Results.Json(new
            {
                ok = true,
                message = $"Borrowed {tool.Name} ({tool.Id}) for {userName}",
                toolId = tool.Id,
                toolName = tool.Name,
                action = "BORROW",
                cardUID = cardUid
            });
        }
        else
        {
            if (tool.Status is not ("inuse" or "overdue"))
                return Results.Json(new { ok = false, error = $"{tool.Name} is not currently borrowed.", toolId = tool.Id });

            await tools.ReturnAsync(tool.Id, userName, email);
            return Results.Json(new
            {
                ok = true,
                message = $"Returned {tool.Name} ({tool.Id}) by {userName}",
                toolId = tool.Id,
                toolName = tool.Name,
                action = "RETURN",
                cardUID = cardUid
            });
        }
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message }, statusCode: 500);
    }
});

// Local + Railway/Render (PORT env)
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    app.Run($"http://0.0.0.0:{port}");
else
    app.Run("http://localhost:5000");
