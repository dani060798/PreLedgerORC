using Microsoft.AspNetCore.StaticFiles;
using PreLedgerORC.Models;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace PreLedgerORC.Services;

public class DocumentStorageService
{
    private readonly AppPaths _paths;
    private readonly CustomerFilesService _files;
    private readonly FileExtensionContentTypeProvider _contentTypes = new();

    // 30 MB default
    public const long MaxUploadBytes = 30L * 1024L * 1024L;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".pdf"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "application/pdf"
    };

    public DocumentStorageService(AppPaths paths, CustomerFilesService files)
    {
        _paths = paths;
        _files = files;
    }

    public bool TryGetContentType(string fileName, out string contentType)
    {
        if (_contentTypes.TryGetContentType(fileName, out contentType!))
            return true;

        contentType = "application/octet-stream";
        return false;
    }

    public void ValidateUpload(IFormFile file)
    {
        if (file == null) throw new InvalidOperationException("No file.");
        if (file.Length <= 0) throw new InvalidOperationException("Empty file.");
        if (file.Length > MaxUploadBytes) throw new InvalidOperationException($"File too large (max {MaxUploadBytes / (1024 * 1024)} MB).");

        var ext = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException("File type not allowed.");

        var ct = (file.ContentType ?? "").Trim();
        if (!AllowedContentTypes.Contains(ct))
        {
            // Some browsers may send octet-stream for PDFs; allow if extension is allowed
            if (!(ct.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase) && ext == ".pdf"))
                throw new InvalidOperationException("Content-Type not allowed.");
        }
    }

    /// <summary>
    /// Returns a project-root-relative path (slash-separated).
    /// (Legacy path builder – kept for compatibility.)
    /// </summary>
    public string BuildStoredRelativePath(int customerId, string folderIdOrRoot, DateTime createdAtUtc, Guid documentId, string originalExt)
    {
        // IMPORTANT: never use "root" as a physical folder
        folderIdOrRoot = (folderIdOrRoot ?? "").Trim();
        if (string.IsNullOrWhiteSpace(folderIdOrRoot) || folderIdOrRoot.Equals("root", StringComparison.OrdinalIgnoreCase))
            folderIdOrRoot = "Dokumente";

        var day = createdAtUtc.ToString("yyyy-MM-dd");
        folderIdOrRoot = NormalizeFolderId(folderIdOrRoot);

        // Kundenordner (Data/Clients/<id_name>) ermitteln und relativ zum ProjectRoot machen
        var customerAbs = _files.ResolveCustomerDirectoryById(customerId);
        var customerRel = Path.GetRelativePath(_paths.ProjectRoot, customerAbs);

        var rel = Path.Combine(customerRel, folderIdOrRoot, "Documents", day, documentId.ToString("D"), "original" + originalExt);
        return rel.Replace('\\', '/');
    }

    /// <summary>
    /// Stores directly inside the folder with the given filename.
    /// Returns a project-root-relative path (slash-separated).
    /// </summary>
    public string BuildStoredRelativePathForFilename(int customerId, string folderId, string fileName)
    {
        folderId = (folderId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(folderId) || folderId.Equals("root", StringComparison.OrdinalIgnoreCase))
            folderId = "Dokumente";

        folderId = NormalizeFolderId(folderId);

        var customerAbs = _files.ResolveCustomerDirectoryById(customerId);
        var customerRel = Path.GetRelativePath(_paths.ProjectRoot, customerAbs);

        var rel = Path.Combine(customerRel, folderId, fileName);
        return rel.Replace('\\', '/');
    }

    public string GetAbsolutePathFromRelative(string relativePath)
    {
        relativePath = (relativePath ?? "").Replace('\\', '/').TrimStart('/');
        var abs = Path.GetFullPath(Path.Combine(_paths.ProjectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var projectRootFull = Path.GetFullPath(_paths.ProjectRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!abs.StartsWith(projectRootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid path traversal.");

        return abs;
    }

    public string GetDocumentDirectoryFromStoredPath(string storedRelativePath)
    {
        var abs = GetAbsolutePathFromRelative(storedRelativePath);
        return Directory.GetParent(abs)!.FullName;
    }

    public async Task SaveToStoredPathAsync(IFormFile file, string storedRelativePath, CancellationToken ct)
    {
        var abs = GetAbsolutePathFromRelative(storedRelativePath);
        var dir = Path.GetDirectoryName(abs)!;
        Directory.CreateDirectory(dir);

        // Write file
        await using var fs = new FileStream(abs, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await file.CopyToAsync(fs, ct);
        await fs.FlushAsync(ct);
    }

    private static string NormalizeFolderId(string folderIdOrRoot)
    {
        folderIdOrRoot = (folderIdOrRoot ?? "").Trim();

        if (string.IsNullOrWhiteSpace(folderIdOrRoot))
            return "Dokumente";

        folderIdOrRoot = folderIdOrRoot.Replace('\\', '/').Trim('/');
        if (folderIdOrRoot.Length == 0) return "Dokumente";

        // collapse dangerous dots
        folderIdOrRoot = Regex.Replace(folderIdOrRoot, @"\.\.+", ".");

        // ✅ FIX: allow unicode letters/numbers (umlaute etc.)
        // allow: letters, numbers, underscore, dash, slash
        folderIdOrRoot = Regex.Replace(folderIdOrRoot, @"[^\p{L}\p{N}_\-\/]", "_");

        // prevent traversal segments
        var parts = folderIdOrRoot.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != "." && p != "..")
            .ToArray();

        return parts.Length == 0 ? "Dokumente" : string.Join('/', parts);
    }
}
