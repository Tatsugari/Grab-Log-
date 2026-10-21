using GrabLog.Models;
using GrabLog.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpClient<SupabaseClient>();
builder.Services.AddScoped<LogService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<ToolService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();

// Session cookies that work on Railway HTTPS
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(12);
    options.Cookie.Name = ".GrabLog.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// Railway / reverse proxy
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = ctx =>
    {
        // allow CSS/JS to refresh after deploy
        if (ctx.File.Name.EndsWith(".css") || ctx.File.Name.EndsWith(".js"))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, must-revalidate";
        }
    }
});

app.UseRouting();
app.UseSession();
app.MapRazorPages();

app.MapGet("/api/notifications", async (HttpContext ctx, NotificationService notifs) =>
{
    var email = ctx.Session.GetString("UserEmail");
    if (string.IsNullOrEmpty(email))
        return Results.Json(new { error = "Not logged in" }, statusCode: 401);

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
    var email = ctx.Session.GetString("UserEmail");
    if (string.IsNullOrEmpty(email))
    {
        return Results.Json(new
        {
            ok = false,
            error = "Session expired or not logged in. Refresh the page, log in again, then Connect Kiosk."
        }, statusCode: 401);
    }

    var action = (body.ActionType ?? "").Trim().ToUpperInvariant();
    var userName = string.IsNullOrWhiteSpace(body.UserName)
        ? (ctx.Session.GetString("UserName") ?? "Kiosk")
        : body.UserName.Trim();
    var cardUid = (body.CardUID ?? "").Trim();

    if (action is not ("BORROW" or "RETURN"))
        return Results.Json(new { ok = false, error = "actionType must be BORROW or RETURN" }, statusCode: 400);

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
                error = "No matching tool. Click BORROW or RETURN on a tool card first, then scan.",
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

            await tools.BorrowAsync(tool.Id, userName, email, cardUid);
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

            // Only the same RFID that borrowed can return
            var ownerCard = (tool.BorrowedCardUid ?? "").Trim();
            if (!string.IsNullOrEmpty(ownerCard) && !string.IsNullOrEmpty(cardUid)
                && !ownerCard.Equals(cardUid, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new
                {
                    ok = false,
                    error = $"Access denied. Only {tool.LoggedTo ?? "the borrower"} (card who borrowed this tool) can return it.",
                    toolId = tool.Id,
                    owner = tool.LoggedTo,
                    scannedCard = cardUid
                });
            }

            await tools.ReturnAsync(tool.Id, userName, email, cardUid);
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


// Get who borrowed a tool (card UID) — used for ARM_RETURN:uid
app.MapGet("/api/kiosk/tool-owner", async (HttpContext ctx, string? toolId, ToolService tools) =>
{
    if (string.IsNullOrEmpty(ctx.Session.GetString("UserEmail")))
        return Results.Json(new { error = "Not logged in" }, statusCode: 401);

    if (string.IsNullOrWhiteSpace(toolId))
        return Results.Json(new { error = "toolId required" }, statusCode: 400);

    var all = await tools.GetAllAsync();
    var tool = all.FirstOrDefault(x => x.Id.Equals(toolId, StringComparison.OrdinalIgnoreCase));
    if (tool == null)
        return Results.Json(new { error = "Tool not found" }, statusCode: 404);

    return Results.Json(new
    {
        toolId = tool.Id,
        status = tool.Status,
        loggedTo = tool.LoggedTo,
        borrowedCardUid = tool.BorrowedCardUid
    });
});

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    app.Run($"http://0.0.0.0:{port}");
else
    app.Run("http://localhost:5000");
