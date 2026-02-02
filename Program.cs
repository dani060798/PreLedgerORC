using System.IO;
using Microsoft.EntityFrameworkCore;
using PreLedgerORC.Data;
using PreLedgerORC.Services;

static string FindProjectRoot(string startDir)
{
    var dir = new DirectoryInfo(startDir);
    while (dir != null)
    {
        if (dir.GetFiles("*.csproj").Any())
            return dir.FullName;
        dir = dir.Parent;
    }
    return startDir;
}

var projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());

// IMPORTANT: set roots via WebApplicationOptions (required by minimal hosting)
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = projectRoot,
    WebRootPath = Path.Combine(projectRoot, "wwwroot")
});

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Razor Pages
builder.Services.AddRazorPages();

// App services
builder.Services.AddSingleton<AppPaths>();              // uses IWebHostEnvironment + ILogger<AppPaths>
builder.Services.AddScoped<CustomerFilesService>();
builder.Services.AddSingleton<HtmlSanitizerService>();

// SQLite
var dbDir = Path.Combine(projectRoot, "App_Data");
Directory.CreateDirectory(dbDir);
var dbPath = Path.Combine(dbDir, "prehledgerorc.db");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath}");
});

var app = builder.Build();

// Log effective paths (good sanity check)
app.Logger.LogInformation("ProjectRoot: {root}", projectRoot);
app.Logger.LogInformation("ContentRootPath: {root}", app.Environment.ContentRootPath);
app.Logger.LogInformation("WebRootPath: {root}", app.Environment.WebRootPath);
app.Logger.LogInformation("SQLite DB: {db}", dbPath);

// Ensure DB exists
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseExceptionHandler("/Error");

// Static files MUST be enabled (now it will use correct wwwroot)
app.UseStaticFiles();

app.UseRouting();
app.MapRazorPages();

app.Run();
