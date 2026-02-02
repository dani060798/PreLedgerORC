using System.IO;
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace PreLedgerORC.Services;

public class CustomerFilesService
{
    private readonly AppPaths _paths;

    public CustomerFilesService(AppPaths paths)
    {
        _paths = paths;
    }

    public string GetCustomerDirectory(int customerId, string customerName)
    {
        Directory.CreateDirectory(_paths.ClientsRootDirectory);

        var safeName = ToSafePathSegment(customerName);
        var dir = Path.Combine(_paths.ClientsRootDirectory, $"{customerId}_{safeName}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public string ResolveCustomerDirectoryById(int customerId)
    {
        Directory.CreateDirectory(_paths.ClientsRootDirectory);

        var match = Directory.EnumerateDirectories(_paths.ClientsRootDirectory)
            .FirstOrDefault(d => Path.GetFileName(d).StartsWith($"{customerId}_", StringComparison.OrdinalIgnoreCase));

        if (match is null)
            throw new DirectoryNotFoundException($"Customer directory not found for id {customerId}");

        return match;
    }

    public string GetAbsolutePathForCustomer(int customerId, string relPath)
    {
        var baseDir = ResolveCustomerDirectoryById(customerId);

        relPath = NormalizeRelPath(relPath);
        var combined = Path.Combine(baseDir, relPath.Replace('/', Path.DirectorySeparatorChar));
        var full = Path.GetFullPath(combined);

        var fullBase = Path.GetFullPath(baseDir) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid path (outside customer directory).");

        return full;
    }

    public string GetRelPathFromAbsolute(int customerId, string absolutePath)
    {
        var baseDir = ResolveCustomerDirectoryById(customerId);
        var fullBase = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(absolutePath);

        if (!full.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid path (outside customer directory).");

        var rel = full.Substring(fullBase.Length).Replace('\\', '/');
        return NormalizeRelPath(rel);
    }

    // -----------------------------
    // Notes (Delta JSON) storage
    // -----------------------------
    public string ReadNoteDeltaJson(int customerId, string relPath)
    {
        relPath = NormalizeRelPath(relPath);
        if (!relPath.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .note.json notes are supported for delta read.");

        var abs = GetAbsolutePathForCustomer(customerId, relPath);
        if (!File.Exists(abs))
            throw new FileNotFoundException("Note not found", abs);

        return File.ReadAllText(abs, Encoding.UTF8);
    }

    public void WriteNoteDeltaJson(int customerId, string relPath, string deltaJson)
    {
        relPath = NormalizeRelPath(relPath);
        if (!relPath.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .note.json notes are supported for delta write.");

        var abs = GetAbsolutePathForCustomer(customerId, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, deltaJson ?? "{}", Encoding.UTF8);
    }

    public void DeleteNote(int customerId, string relPath)
    {
        relPath = NormalizeRelPath(relPath);

        if (!IsNoteFile(relPath))
            throw new InvalidOperationException("Only notes can be deleted.");

        var abs = GetAbsolutePathForCustomer(customerId, relPath);
        if (File.Exists(abs))
            File.Delete(abs);
    }

    public string MoveNote(int customerId, string sourceRelPath, string targetFolderRelPath)
    {
        sourceRelPath = NormalizeRelPath(sourceRelPath);
        targetFolderRelPath = NormalizeRelPath(targetFolderRelPath);

        if (!IsNoteFile(sourceRelPath))
            throw new InvalidOperationException("Only notes can be moved.");

        // Source absolute
        var sourceAbs = GetAbsolutePathForCustomer(customerId, sourceRelPath);
        if (!File.Exists(sourceAbs))
            throw new FileNotFoundException("Source note not found", sourceAbs);

        // Target folder absolute
        var baseDir = ResolveCustomerDirectoryById(customerId);

        string targetFolderAbs;
        if (string.IsNullOrWhiteSpace(targetFolderRelPath))
        {
            targetFolderAbs = baseDir;
        }
        else
        {
            targetFolderAbs = GetAbsolutePathForCustomer(customerId, targetFolderRelPath);
            if (!Directory.Exists(targetFolderAbs))
                Directory.CreateDirectory(targetFolderAbs);
        }

        var fileName = Path.GetFileName(sourceAbs);
        var destAbs = Path.Combine(targetFolderAbs, fileName);

        // Collision handling: add suffix
        if (File.Exists(destAbs))
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);

            // for ".note.json" we need to keep ".note.json" as full extension
            if (fileName.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase))
            {
                baseName = fileName.Substring(0, fileName.Length - ".note.json".Length);
                ext = ".note.json";
            }

            var i = 2;
            while (true)
            {
                var candidate = $"{baseName}_{i}{ext}";
                var candidateAbs = Path.Combine(targetFolderAbs, candidate);
                if (!File.Exists(candidateAbs))
                {
                    destAbs = candidateAbs;
                    break;
                }
                i++;
            }
        }

        File.Move(sourceAbs, destAbs);

        return GetRelPathFromAbsolute(customerId, destAbs);
    }

    // -----------------------------
    // Legacy Markdown support (optional)
    // -----------------------------
    public string ReadNoteMarkdown(int customerId, string relPath)
    {
        relPath = NormalizeRelPath(relPath);

        if (!relPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .md notes are supported.");

        var abs = GetAbsolutePathForCustomer(customerId, relPath);
        if (!File.Exists(abs))
            throw new FileNotFoundException("Note not found", abs);

        return File.ReadAllText(abs, Encoding.UTF8);
    }

    public void WriteNoteMarkdown(int customerId, string relPath, string markdown)
    {
        relPath = NormalizeRelPath(relPath);

        if (!relPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .md notes are supported.");

        var abs = GetAbsolutePathForCustomer(customerId, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, markdown ?? "", Encoding.UTF8);
    }

    // -----------------------------
    // Folder / Create helpers
    // -----------------------------
    public void CreateFolder(int customerId, string customerName, string folderName, string? parentRelPath = null)
    {
        var baseDir = GetCustomerDirectory(customerId, customerName);
        var targetParent = GetSafeTargetDirectory(baseDir, parentRelPath);

        var safeFolder = ToSafePathSegment(folderName);
        Directory.CreateDirectory(Path.Combine(targetParent, safeFolder));
    }

    /// <summary>
    /// Creates a new delta-based note file (.note.json). Returns the relative path.
    /// </summary>
    public string CreateNotes(int customerId, string customerName, string? title = null, string? parentRelPath = null)
    {
        var baseDir = GetCustomerDirectory(customerId, customerName);
        var targetParent = GetSafeTargetDirectory(baseDir, parentRelPath);

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var fileTitle = string.IsNullOrWhiteSpace(title) ? "notes" : ToSafePathSegment(title);
        var filename = $"{stamp}_{fileTitle}.note.json";
        var absPath = Path.Combine(targetParent, filename);

        if (!File.Exists(absPath))
        {
            var initial =
                """
                {"ops":[{"insert":"Notes\n"},{"insert":"\n"}]}
                """;
            File.WriteAllText(absPath, initial, Encoding.UTF8);
        }

        // Convert absolute -> rel path
        var rel = absPath.Substring(baseDir.TrimEnd(Path.DirectorySeparatorChar).Length)
            .TrimStart(Path.DirectorySeparatorChar)
            .Replace('\\', '/');

        return NormalizeRelPath(rel);
    }

    private static string GetSafeTargetDirectory(string baseDir, string? parentRelPath)
    {
        if (string.IsNullOrWhiteSpace(parentRelPath))
            return baseDir;

        parentRelPath = NormalizeRelPath(parentRelPath);

        var fullBase = Path.GetFullPath(baseDir) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(baseDir, parentRelPath.Replace('/', Path.DirectorySeparatorChar)));

        if (!candidate.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
            return baseDir;

        Directory.CreateDirectory(candidate);
        return candidate;
    }

    private static bool IsNoteFile(string relPath)
    {
        relPath = NormalizeRelPath(relPath);
        return relPath.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase)
               || relPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelPath(string? relPath)
    {
        relPath ??= "";
        relPath = relPath.Replace('\\', '/').Trim();
        while (relPath.StartsWith("/")) relPath = relPath[1..];
        relPath = relPath.Replace("//", "/");
        return relPath;
    }

    private static string ToSafePathSegment(string input)
    {
        input = input.Trim();
        if (string.IsNullOrWhiteSpace(input)) return "untitled";

        var invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
        var pattern = $"[{Regex.Escape(invalid)}]";
        var safe = Regex.Replace(input, pattern, "_");
        safe = Regex.Replace(safe, @"\s+", " ").Trim();

        if (safe.Length > 80) safe = safe[..80];
        return safe;
    }
}
