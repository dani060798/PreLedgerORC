using System.IO;

namespace PreLedgerORC.Services;

public class AppPaths
{
    public string ProjectRoot { get; }
    public string AppDataDirectory { get; }
    public string ClientsRootDirectory { get; }

    public AppPaths(IWebHostEnvironment env, ILogger<AppPaths> logger)
    {
        // ContentRootPath == Projektroot beim Start aus VS
        ProjectRoot = env.ContentRootPath;

        AppDataDirectory = Path.Combine(ProjectRoot, "App_Data");
        ClientsRootDirectory = Path.Combine(AppDataDirectory, "Clients");

        logger.LogInformation("ProjectRoot: {ProjectRoot}", ProjectRoot);
        logger.LogInformation("ContentRootPath: {ContentRootPath}", env.ContentRootPath);
        logger.LogInformation("WebRootPath: {WebRootPath}", env.WebRootPath);
        logger.LogInformation("SQLite DB: {DbPath}", Path.Combine(AppDataDirectory, "prehledgerorc.db"));
    }
}
