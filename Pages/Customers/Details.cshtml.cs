using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PreLedgerORC.Data;
using PreLedgerORC.Models;
using PreLedgerORC.Services;
using System.IO;

namespace PreLedgerORC.Pages.Customers;

public class DetailsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly DocumentStorageService _storage;
    private readonly IDocumentPipelineQueue _queue;
    private readonly CustomerFilesService _files;

    public DetailsModel(
        AppDbContext db,
        DocumentStorageService storage,
        IDocumentPipelineQueue queue,
        CustomerFilesService files)
    {
        _db = db;
        _storage = storage;
        _queue = queue;
        _files = files;
    }

    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "";

    public sealed class NoteVm
    {
        public string Title { get; set; } = "";
        public string RelPath { get; set; } = "";
        public DateTime UpdatedUtc { get; set; }
    }

    public sealed class UploadTargetVm
    {
        public string FolderId { get; set; } = "Dokumente";   // default is "Dokumente"
        public string DisplayName { get; set; } = "Dokumente";
    }

    public List<NoteVm> Notes { get; set; } = new();
    public List<DocumentItem> Documents { get; set; } = new();

    public List<UploadTargetVm> UploadTargets { get; set; } = new();
    public string SelectedFolderId { get; set; } = "Dokumente";

    [TempData]
    public string? Flash { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        CustomerId = id;

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (customer == null)
            return NotFound();

        CustomerName = customer.Name;

        // Ensure default docs folder exists physically
        try
        {
            var rootDir = _files.ResolveCustomerDirectoryById(id);
            Directory.CreateDirectory(Path.Combine(rootDir, "Dokumente"));
        }
        catch { }

        Documents = await _db.DocumentItems.AsNoTracking()
            .Where(x => x.CustomerId == id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        Notes = LoadNotesFromFilesystem(id);

        UploadTargets = BuildUploadTargets(id);
        SelectedFolderId = "Dokumente";

        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(int id, List<IFormFile> files, [FromForm] string? FolderId, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (customer == null)
            return NotFound();

        if (files == null || files.Count == 0)
        {
            Flash = "Keine Datei ausgewählt.";
            return Redirect($"/Customers/{id}");
        }

        // Default is "Dokumente" (never "root")
        var folderId = string.IsNullOrWhiteSpace(FolderId) ? "Dokumente" : FolderId.Trim();

        // safety: only allow an existing folder under customer directory
        folderId = ValidateFolderIdForCustomer(id, folderId);

        // Ensure docs folder exists
        try
        {
            var rootDir = _files.ResolveCustomerDirectoryById(id);
            Directory.CreateDirectory(Path.Combine(rootDir, "Dokumente"));
        }
        catch { }

        foreach (var file in files)
        {
            try
            {
                _storage.ValidateUpload(file);

                var doc = new DocumentItem
                {
                    Id = Guid.NewGuid(),
                    CustomerId = id,
                    FolderId = folderId, // always a real folder, never "root"
                    OriginalFileName = Path.GetFileName(file.FileName ?? "upload"),
                    CreatedAtUtc = DateTime.UtcNow,
                    Status = DocumentStatus.Pending
                };

                var originalName = Path.GetFileName(doc.OriginalFileName);
                if (string.IsNullOrWhiteSpace(originalName))
                    originalName = "upload.pdf";

                // collision handling: Scan.pdf -> Scan_2.pdf ...
                string candidate = originalName;
                string baseName = Path.GetFileNameWithoutExtension(originalName);
                string ext = Path.GetExtension(originalName);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";

                int i = 2;
                while (true)
                {
                    var relCandidate = _storage.BuildStoredRelativePathForFilename(id, folderId, candidate);
                    var absCandidate = _storage.GetAbsolutePathFromRelative(relCandidate);

                    if (!System.IO.File.Exists(absCandidate))
                    {
                        doc.StoredPath = relCandidate;
                        break;
                    }

                    candidate = $"{baseName}_{i}{ext}";
                    i++;
                }

                _db.DocumentItems.Add(doc);
                await _db.SaveChangesAsync(ct);

                await _storage.SaveToStoredPathAsync(file, doc.StoredPath, ct);

                _queue.Enqueue(doc.Id);
            }
            catch (Exception ex)
            {
                Flash = $"Upload fehlgeschlagen: {ex.Message}";
            }
        }

        return Redirect($"/Customers/{id}");
    }

    public async Task<IActionResult> OnPostDeleteDocumentAsync(int id, Guid documentId, CancellationToken ct)
    {
        var item = await _db.DocumentItems.FirstOrDefaultAsync(x => x.Id == documentId && x.CustomerId == id, ct);
        if (item == null)
            return Redirect($"/Customers/{id}");

        try
        {
            // IMPORTANT: delete only the file (not directories like "Dokumente")
            var abs = _storage.GetAbsolutePathFromRelative(item.StoredPath);
            if (System.IO.File.Exists(abs))
                System.IO.File.Delete(abs);

            _db.DocumentItems.Remove(item);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            Flash = $"Delete fehlgeschlagen: {ex.Message}";
        }

        return Redirect($"/Customers/{id}");
    }

    public string StatusLabel(DocumentStatus s) => s switch
    {
        DocumentStatus.Pending => "Pending",
        DocumentStatus.Stored => "Stored",
        DocumentStatus.OcrQueued => "OCR queued",
        DocumentStatus.OcrDone => "OCR done",
        DocumentStatus.ExportReady => "Export ready",
        DocumentStatus.Failed => "Failed",
        _ => s.ToString()
    };

    public string StatusCss(DocumentStatus s) => s switch
    {
        DocumentStatus.Pending => "badge-soft badge-pending",
        DocumentStatus.Stored => "badge-soft badge-stored",
        DocumentStatus.OcrQueued => "badge-soft badge-queued",
        DocumentStatus.OcrDone => "badge-soft badge-done",
        DocumentStatus.ExportReady => "badge-soft badge-ready",
        DocumentStatus.Failed => "badge-soft badge-failed",
        _ => "badge-soft"
    };

    private string ValidateFolderIdForCustomer(int customerId, string folderId)
    {
        folderId = (folderId ?? "").Trim();
        if (folderId.Length == 0) return "Dokumente";
        if (folderId.Equals("root", StringComparison.OrdinalIgnoreCase)) return "Dokumente";

        // ensure folder exists under customer directory
        try
        {
            var abs = _files.GetAbsolutePathForCustomer(customerId, folderId);
            if (Directory.Exists(abs))
                return folderId.Replace('\\', '/').Trim('/');
        }
        catch
        {
            // ignore
        }

        return "Dokumente";
    }

    private List<UploadTargetVm> BuildUploadTargets(int customerId)
    {
        var list = new List<UploadTargetVm>
        {
            new UploadTargetVm { FolderId = "Dokumente", DisplayName = "Dokumente" }
        };

        string baseDir;
        try
        {
            baseDir = _files.ResolveCustomerDirectoryById(customerId);
        }
        catch
        {
            return list;
        }

        // all folders under customer dir (excluding hidden/system)
        foreach (var dir in Directory.EnumerateDirectories(baseDir, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith(".", StringComparison.OrdinalIgnoreCase)) continue;

            string rel;
            try
            {
                rel = _files.GetRelPathFromAbsolute(customerId, dir);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rel)) continue;

            if (rel.Equals("Dokumente", StringComparison.OrdinalIgnoreCase))
                continue;

            list.Add(new UploadTargetVm
            {
                FolderId = rel,
                DisplayName = rel
            });

        }

        return list
            .OrderBy(x => x.FolderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<NoteVm> LoadNotesFromFilesystem(int customerId)
    {
        var list = new List<NoteVm>();

        string baseDir;
        try
        {
            baseDir = _files.ResolveCustomerDirectoryById(customerId);
        }
        catch
        {
            return list;
        }

        var files = Directory.EnumerateFiles(baseDir, "*.*", SearchOption.AllDirectories)
            .Where(f =>
                f.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".md", StringComparison.OrdinalIgnoreCase));

        foreach (var abs in files)
        {
            DateTime updatedUtc;
            try { updatedUtc = System.IO.File.GetLastWriteTimeUtc(abs); }
            catch { updatedUtc = DateTime.UtcNow; }

            string rel;
            try { rel = _files.GetRelPathFromAbsolute(customerId, abs); }
            catch { continue; }

            var title = FriendlyTitleFromFileName(Path.GetFileName(abs));

            list.Add(new NoteVm
            {
                Title = title,
                RelPath = rel,
                UpdatedUtc = updatedUtc
            });
        }

        return list.OrderByDescending(x => x.UpdatedUtc).ToList();
    }

    private static string FriendlyTitleFromFileName(string fileName)
    {
        if (fileName.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase))
        {
            var stem = fileName[..^(".note.json".Length)];

            if (stem.Length > 20 && stem[4] == '-' && stem[7] == '-' && stem[10] == '_' && stem[13] == '-' && stem[16] == '-')
            {
                var idx = stem.IndexOf('_', 19);
                if (idx >= 0 && idx + 1 < stem.Length)
                    stem = stem[(idx + 1)..];
            }

            return string.IsNullOrWhiteSpace(stem) ? "notes" : stem.Replace('_', ' ');
        }

        if (fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            return string.IsNullOrWhiteSpace(stem) ? "note" : stem.Replace('_', ' ');
        }

        return fileName;
    }
}
