using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PreLedgerORC.Data;
using PreLedgerORC.Models;
using PreLedgerORC.Services;

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

    public List<NoteVm> Notes { get; set; } = new();

    public List<DocumentItem> Documents { get; set; } = new();

    [TempData]
    public string? Flash { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        CustomerId = id;

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (customer == null)
            return NotFound();

        CustomerName = customer.Name;

        Documents = await _db.DocumentItems.AsNoTracking()
            .Where(x => x.CustomerId == id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        Notes = LoadNotesFromFilesystem(id);

        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(int id, List<IFormFile> files, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (customer == null)
            return NotFound();

        if (files == null || files.Count == 0)
        {
            Flash = "Keine Datei ausgewählt.";
            return Redirect($"/Customers/{id}");
        }

        // MVP: Upload in root. Später: selected folder from UI/Sidebar.
        var folderId = "root";

        foreach (var file in files)
        {
            try
            {
                _storage.ValidateUpload(file);

                var doc = new DocumentItem
                {
                    Id = Guid.NewGuid(),
                    CustomerId = id,
                    FolderId = folderId,
                    OriginalFileName = Path.GetFileName(file.FileName ?? "upload"),
                    CreatedAtUtc = DateTime.UtcNow,
                    Status = DocumentStatus.Pending
                };

                var ext = Path.GetExtension(doc.OriginalFileName);
                if (string.IsNullOrWhiteSpace(ext))
                    ext = ".bin";

                doc.StoredPath = _storage.BuildStoredRelativePath(id, folderId, doc.CreatedAtUtc, doc.Id, ext);

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
            var dir = _storage.GetDocumentDirectoryFromStoredPath(item.StoredPath);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);

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

        // scan for both delta notes and markdown notes
        var files = Directory.EnumerateFiles(baseDir, "*.*", SearchOption.AllDirectories)
            .Where(f =>
                f.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".md", StringComparison.OrdinalIgnoreCase));

        foreach (var abs in files)
        {
            DateTime updatedUtc;
            try
            {
                updatedUtc = System.IO.File.GetLastWriteTimeUtc(abs);
            }
            catch
            {
                updatedUtc = DateTime.UtcNow;
            }

            string rel;
            try
            {
                rel = _files.GetRelPathFromAbsolute(customerId, abs);
            }
            catch
            {
                continue;
            }

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
        // For "yyyy-MM-dd_HH-mm-ss_title.note.json" → show "title"
        // For ".md" → filename without extension
        if (fileName.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase))
        {
            var stem = fileName[..^(".note.json".Length)];

            // remove leading timestamp if present
            // pattern: 2026-02-03_12-30-10_
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
