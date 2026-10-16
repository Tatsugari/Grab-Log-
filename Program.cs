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

app.Run();
