global using Microsoft.EntityFrameworkCore;
global using XFLCSMS.Models;
global using XFLCSMS.Data;
global using XFLCSMS.Services.EmailService;
using XFLCSMS.EmailService;
using Microsoft.AspNetCore.DataProtection;
using XFLCSMS.Infrastructure;
using XFLCSMS.Services;
using XFLCSMS.Services.Notify;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    // The project has <Nullable>enable</Nullable>. Without this switch every non-nullable
    // reference property (navigation collections, Designation, Department, ...) is treated
    // as [Required] by model binding, so ModelState.IsValid can never be true for most forms.
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;

    // Every POST / PUT / DELETE must carry the anti-forgery token: forms get it from the <form> tag helper,
    // fetch() calls send it as a header (see wwwroot/js/app.js). Another site can then no longer make a
    // signed-in user's browser delete a ticket or change a password.
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add(new XFLCSMS.Infrastructure.AntiforgeryFailureFilter());
});

// "Session": { "CookieSecure": "auto" | "always" }. "auto" marks the cookies as https-only whenever the site is
// opened over https and still lets an office use it over plain http; "always" refuses to send them over http at all
// (right for a site that is only ever reached over https).
var cookieSecurity = string.Equals(builder.Configuration["Session:CookieSecure"], "always", StringComparison.OrdinalIgnoreCase)
    ? CookieSecurePolicy.Always
    : CookieSecurePolicy.SameAsRequest;

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(option =>
{
    // minutes without a click after which a sign-in ends ("Session": { "IdleMinutes": 10 } in appsettings.json)
    option.IdleTimeout = TimeSpan.FromMinutes(Math.Clamp(builder.Configuration.GetValue("Session:IdleMinutes", 10), 1, 720));
    option.Cookie.HttpOnly = true;
    option.Cookie.IsEssential = true;
    // Lax: the cookie comes along when somebody opens a ticket from the link in an e-mail, but not with requests
    // another site makes in the background.
    option.Cookie.SameSite = SameSiteMode.Lax;
    option.Cookie.SecurePolicy = cookieSecurity;
});

// the anti-forgery cookie follows the same rule as the session cookie
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = cookieSecurity;
});

// Guessing passwords and tokens: Services/AttemptGuard.cs, used by RegisterLoginController
builder.Services.AddSingleton<AttemptGuard>();
builder.Services.AddSingleton<SignInLimits>();

// Uploads: one save (a ticket form with its files) may be as large as all its files together are allowed to be.
// The limit per file and the check of the content are in TicketService.SaveAttachmentsAsync.
var uploadLimits = new UploadLimits(builder.Configuration);
builder.Services.AddSingleton(uploadLimits);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = uploadLimits.MaxRequestBytes;
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = uploadLimits.MaxRequestBytes);
builder.Services.Configure<IISServerOptions>(options => options.MaxRequestBodySize = uploadLimits.MaxRequestBytes);

// The keys that protect cookies and the secrets stored from the settings pages (mail password, SMS key) are kept
// in App_Data/keys, next to the application: they survive a restart and do not depend on the Windows profile of
// the account the site runs under. Keep the folder out of version control.
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("XFLCSMS")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));
if (OperatingSystem.IsWindows())
{
    // On Windows the key files are themselves encrypted for this computer. After a move to another server the
    // stored mail password and SMS key have to be entered once more (the settings page says so).
    dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
}

// Settings changed from the pages of the application (table AppSettings), kept in memory.
builder.Services.AddSingleton<SettingsStore>();

// Notifications: open pages listen through the hub; e-mail and SMS are queued in the database and sent by the worker.
builder.Services.AddSingleton<NotificationHub>();
builder.Services.AddSingleton<NotificationWorker>();
builder.Services.AddHostedService(services => services.GetRequiredService<NotificationWorker>());
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<SmsSender>();
builder.Services.AddScoped<DemoDataService>();
// The SMS gateway is called at an address an administrator entered: no redirects are followed (the answer of
// the address itself counts), and the request line is kept out of the log (it can carry the key and phone numbers).
builder.Services.AddHttpClient("sms", client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Logging.AddFilter("System.Net.Http.HttpClient.sms", LogLevel.Warning);

builder.Services.AddScoped<IEmailServices, EmailService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<SystemHealthService>();

// The last warnings and errors are kept in memory for the system health page.
builder.Logging.AddProvider(XFLCSMS.Infrastructure.RecentLog.Instance);
builder.Services.AddScoped<TicketService>();

// Connection string lives in appsettings.json -> ConnectionStrings:DefaultConnection
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is missing. Add it under ConnectionStrings in appsettings.json.");
builder.Services.AddDbContext<DataContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

var app = builder.Build();

// Apply migrations and, on an empty database, create the first administrator (see Data/DbInitializer.cs).
DbInitializer.Initialize(app.Services, app.Configuration, app.Logger);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// what a page of this site may do in the browser: Infrastructure/SecurityHeaders.cs
app.UseSecurityHeaders(app.Configuration);

// Ticket attachments live in wwwroot/Uplods. They are handed out by the DownloadAttachment actions
// (which check the session), so the folder itself must not be browsable by URL without signing in.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/" + TicketService.UploadFolderName, StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});

app.UseStaticFiles();

// A bare 404 (ticket not found, wrong address) gets a readable page instead of an empty browser tab.
app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");

app.UseRouting();

app.UseSession();
app.UseAuthorization();

// For monitoring tools: answers "Healthy" (200) or "Unhealthy" (503), nothing else, without signing in.
// The details are on the system health page of the platform admin.
// (Written to the response by hand: Results.Text has no status code parameter in .NET 6.)
app.MapGet("/health", async (HttpContext http, SystemHealthService health) =>
{
    var alive = await health.IsAliveAsync();
    http.Response.StatusCode = alive ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
    http.Response.ContentType = "text/plain";
    await http.Response.WriteAsync(alive ? "Healthy" : "Unhealthy");
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=RegisterLogin}/{action=Login}/{id?}");

app.Run();
