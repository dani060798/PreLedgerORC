using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace PreLedgerORC.Services;

public class AppPaths
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AppPaths> _logger;

    public AppPaths(IWebHostEnvironment env, ILogger<AppPaths> logger)
    {
        _env = env;
        _logger = logger;

        ProjectRoot = _env.ContentRootPath;

        // Central data root in project folder
        DataRootDirectory = Path.Combine(ProjectRoot, "Data");
        Directory.CreateDirectory(DataRootDirectory);

        // Existing customers root (keep compatibility)
        ClientsRootDirectory = Path.Combine(DataRootDirectory, "Clients");
        Directory.CreateDirectory(ClientsRootDirectory);

        _logger.LogInformation("ProjectRoot: {root}", ProjectRoot);
        _logger.LogInformation("DataRootDirectory: {dir}", DataRootDirectory);
        _logger.LogInformation("ClientsRootDirectory: {dir}", ClientsRootDirectory);
    }

    public string ProjectRoot { get; }

    /// <summary>
    /// ProjectRoot/Data
    /// </summary>
    public string DataRootDirectory { get; }

    /// <summary>
    /// ProjectRoot/Data/Clients (existing notes/folders)
    /// </summary>
    public string ClientsRootDirectory { get; }
}
