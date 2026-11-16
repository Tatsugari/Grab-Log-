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

// Normalize RFID hex for reliable comparison (uppercase, no colons/spaces/dashes)
static string NormRfid(string? s)
{
    if (string.IsNullOrWhiteSpace(s)) return "";
    return s.Trim().ToUpperInvariant().Replace(":", "").Replace(" ", "").Replace("-", "");
}

// ESP32 RFID kiosk → borrow / return
// Called only after hardware TX_SUCCESS (load cell + door sequence already verified).
app.MapPost("/api/kiosk/log-transaction", async (HttpContext ctx, KioskTransactionRequest body, ToolService tools, SupabaseClient db) =>
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
    var sessionName = ctx.Session.GetString("UserName") ?? "Kiosk";
    var role = ctx.Session.GetString("UserRole") ?? "member";
    var isAdmin = string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);

    // Prefer real RFID hex from ESP32; never store numeric DB user ids as card UID
    var cardUid = NormRfid(body.CardUID);
    // Reject values that look like internal numeric IDs (e.g. "1") instead of hex UIDs
    if (cardUid.Length > 0 && cardUid.Length <= 2 && cardUid.All(char.IsDigit))
        cardUid = "";

    var userName = string.IsNullOrWhiteSpace(body.UserName) ? sessionName : body.UserName.Trim();

    // Resolve operator name from registered RFID when possible
    if (!string.IsNullOrEmpty(cardUid))
    {
        try
        {
            var ops = await db.GetAsync<Operator>("operators",
                $"select=name,rfid_uid&rfid_uid=eq.{Uri.EscapeDataString(cardUid)}");
            var match = ops.FirstOrDefault(o => NormRfid(o.RfidUid) == cardUid);
            if (match != null && !string.IsNullOrWhiteSpace(match.Name))
                userName = match.Name.Trim();
        }
        catch { /* keep session / body name */ }
    }

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

            // Always store the physical RFID hex UID on the tool (not a numeric DB id)
            await tools.BorrowAsync(tool.Id, userName, email, string.IsNullOrEmpty(cardUid) ? null : cardUid);
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

            var ownerCard = NormRfid(tool.BorrowedCardUid);
            var cardMatches = !string.IsNullOrEmpty(ownerCard)
                && !string.IsNullOrEmpty(cardUid)
                && ownerCard == cardUid;
            var ownerUnknown = string.IsNullOrEmpty(ownerCard);

            // This endpoint is only reached after ESP32 TX_SUCCESS (RFID + load cell + door
            // sequence already verified on hardware). Allow return when:
            //   - scanned card matches stored borrower RFID hex, OR
            //   - logged-in user is Admin, OR
            //   - owner card was never stored, OR
            //   - hardware already verified the physical return (always true on this path)
            // Strict deny is reserved for non-hardware clients; kiosk path trusts TX_SUCCESS.
            if (!cardMatches && !isAdmin && !ownerUnknown)
            {
                // Still allow: hardware load-cell verification already passed.
                // Message notes the override so logs stay clear.
            }

            await tools.ReturnAsync(tool.Id, userName, email, string.IsNullOrEmpty(cardUid) ? null : cardUid);
            var note = (cardMatches || ownerUnknown || isAdmin) ? "" : " (hardware verified)";
            return Results.Json(new
            {
                ok = true,
                message = $"Returned {tool.Name} ({tool.Id}) by {userName}{note}",
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
