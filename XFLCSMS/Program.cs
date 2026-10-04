global using Microsoft.EntityFrameworkCore;
global using XFLCSMS.Models;
global using XFLCSMS.Data;
global using XFLCSMS.Services.EmailService;
using XFLCSMS.EmailService;
using XFLCSMS.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    // The project has <Nullable>enable</Nullable>. Without this switch every non-nullable
    // reference property (navigation collections, Designation, Department, ...) is treated
    // as [Required] by model binding, so ModelState.IsValid can never be true for most forms.
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(option =>
{
    option.IdleTimeout = TimeSpan.FromMinutes(10);
    option.Cookie.HttpOnly = true;
    option.Cookie.IsEssential = true;
});

builder.Services.AddScoped<IEmailServices, EmailService>();
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

app.UseRouting();

app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=RegisterLogin}/{action=Login}/{id?}");

app.Run();
