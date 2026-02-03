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

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = projectRoot,
    WebRootPath = Path.Combine(projectRoot, "wwwroot")
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddRazorPages();

// App services
builder.Services.AddSingleton<AppPaths>();
builder.Services.AddScoped<CustomerFilesService>();

builder.Services.AddSingleton<DocumentStorageService>();

builder.Services.AddSingleton<IDocumentPipelineQueue, DocumentPipelineQueue>();
builder.Services.AddHostedService<DocumentPipelineHostedService>();

// SQLite
var dbDir = Path.Combine(projectRoot, "App_Data");
Directory.CreateDirectory(dbDir);
var dbPath = Path.Combine(dbDir, "prehledgerorc.db");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath}");
});

var app = builder.Build();

app.Logger.LogInformation("ProjectRoot: {root}", projectRoot);
app.Logger.LogInformation("ContentRootPath: {root}", app.Environment.ContentRootPath);
app.Logger.LogInformation("WebRootPath: {root}", app.Environment.WebRootPath);
app.Logger.LogInformation("SQLite DB: {db}", dbPath);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // IMPORTANT: EnsureCreated won't add new tables into an existing DB schema.
    // If you already had a DB file, delete it once or switch to migrations.
    db.Database.EnsureCreated();
}

app.UseExceptionHandler("/Error");
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Run();
