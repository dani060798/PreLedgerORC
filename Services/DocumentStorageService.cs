using Microsoft.AspNetCore.StaticFiles;
using PreLedgerORC.Models;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

namespace PreLedgerORC.Services;

public class DocumentStorageService
{
    private readonly AppPaths _paths;
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

    public DocumentStorageService(AppPaths paths)
    {
        _paths = paths;
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
    /// </summary>
    public string BuildStoredRelativePath(int customerId, string folderIdOrRoot, DateTime createdAtUtc, Guid documentId, string originalExt)
    {
        var day = createdAtUtc.ToString("yyyy-MM-dd");
        folderIdOrRoot = NormalizeFolderId(folderIdOrRoot);

        var rel = Path.Combine("Data", customerId.ToString(), folderIdOrRoot, "Documents", day, documentId.ToString("D"), "original" + originalExt);
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
            return "root";

        // keep it simple for MVP: allow only safe path segments
        folderIdOrRoot = folderIdOrRoot.Replace('\\', '/').Trim('/');
        if (folderIdOrRoot.Length == 0) return "root";

        // collapse dangerous chars
        folderIdOrRoot = Regex.Replace(folderIdOrRoot, @"\.\.+", ".");
        folderIdOrRoot = Regex.Replace(folderIdOrRoot, @"[^a-zA-Z0-9_\-\/]", "_");

        // prevent traversal segments
        var parts = folderIdOrRoot.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != "." && p != "..")
            .ToArray();

        return parts.Length == 0 ? "root" : string.Join('/', parts);
    }
}