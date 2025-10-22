// FILE: SignalTracker/Program.cs

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using SignalTracker.Models;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Controllers/Views
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllersWithViews().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = null;
});

// Cache + Session (cross-site cookies)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(300);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.None; // allow cross-site
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // must be HTTPS
});

// CORS (must match your frontend origins exactly)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "https://singnaltracker.netlify.app"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Database
var serverVersion = new MySqlServerVersion(new Version(8, 0, 29));
var connectionString = builder.Configuration.GetConnectionString("MySqlConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, serverVersion)
);

// Authentication (cookie) — cross-site compatible
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "SignalTracker.AuthCookie";
        options.Cookie.SameSite = SameSiteMode.None; // allow cross-site
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // must be HTTPS
        options.ExpireTimeSpan = TimeSpan.FromMinutes(300);
        options.SlidingExpiration = true;

        // For APIs, return 401/403 instead of HTML redirects
        options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });

builder.Services.AddAuthorization();

// Optional but recommended: persist data protection keys (prevents cookie invalidation on restarts)
// Set env var DATAPROTECTION_KEYS_PATH to a persistent folder on your host (e.g., /var/dp-keys)
var dpKeysPath = builder.Configuration["DataProtection:KeysPath"]
                 ?? Environment.GetEnvironmentVariable("DATAPROTECTION_KEYS_PATH");
if (!string.IsNullOrWhiteSpace(dpKeysPath))
{
    Directory.CreateDirectory(dpKeysPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
        .SetApplicationName("SignalTracker");
}

// If you need DI for CommonFunction
builder.Services.AddScoped<CommonFunction>();

builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 100_000_000; // 100 MB or higher
});
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 100_000_000; // or null for unlimited
});


var app = builder.Build();

// Proxy/HTTPS/Headers
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor
});

// Static + Routing + Middleware order
app.UseStaticFiles();
app.UseRouting();

app.UseCors("AllowReactApp");

app.UseSession();
app.UseAuthentication();

app.UseAuthorization();

// MVC endpoints
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

app.Run();