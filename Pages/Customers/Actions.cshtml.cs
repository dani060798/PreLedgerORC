using System.Threading.Tasks;
using System.Threading;
using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PreLedgerORC.Data;
using PreLedgerORC.Models;
using PreLedgerORC.Services;
using System.IO;
using System.Linq;

namespace PreLedgerORC.Pages.Customers;

public class ActionsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly CustomerFilesService _files;
    private readonly DocumentStorageService _storage;

    public ActionsModel(AppDbContext db, CustomerFilesService files, DocumentStorageService storage)
    {
        _db = db;
        _files = files;
        _storage = storage;
    }

    public IActionResult OnGet() => RedirectToPage("/Index");

    // -------------------------
    // Customers
    // -------------------------
    public async Task<IActionResult> OnPostCreateAsync([FromForm] string Name)
    {
        Name = (Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(Name))
            return Redirect(Request.Headers.Referer.ToString());

        var customer = new Customer { Name = Name, CreatedUtc = DateTime.UtcNow };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        _files.GetCustomerDirectory(customer.Id, customer.Name);

        return Redirect($"/Customers/{customer.Id}");
    }

    public async Task<IActionResult> OnPostRenameAsync([FromForm] int CustomerId, [FromForm] string NewName)
    {
        NewName = (NewName ?? "").Trim();
        if (CustomerId <= 0 || string.IsNullOrWhiteSpace(NewName))
            return Redirect(Request.Headers.Referer.ToString());

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == CustomerId);
        if (customer is null)
            return Redirect(Request.Headers.Referer.ToString());

        customer.Name = NewName;
        await _db.SaveChangesAsync();

        _files.GetCustomerDirectory(customer.Id, customer.Name);

        return Redirect(Request.Headers.Referer.ToString());
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromForm] int CustomerId)
    {
        if (CustomerId <= 0)
            return Redirect(Request.Headers.Referer.ToString());

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == CustomerId);
        if (customer is null)
            return Redirect(Request.Headers.Referer.ToString());

        // FIX: only allow delete if no DB docs and no files in customer FS (folders are ok)
        var hasDocs = await _db.DocumentItems.AsNoTracking().AnyAsync(d => d.CustomerId == CustomerId);
        if (hasDocs)
            return Redirect(Request.Headers.Referer.ToString());

        try
        {
            var rootDir = _files.ResolveCustomerDirectoryById(CustomerId);
            if (Directory.Exists(rootDir))
            {
                var hasAnyFiles = Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories).Any();
                if (hasAnyFiles)
                    return Redirect(Request.Headers.Referer.ToString());
            }
        }
        catch
        {
            // if folder not found etc. -> treat as empty and allow delete
        }

        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync();

        return Redirect("/Index");
    }

    // -------------------------
    // Folders + Notes (FS)
    // -------------------------
    public async Task<IActionResult> OnPostCreateFolderAsync(
        [FromForm] int CustomerId,
        [FromForm] string FolderName,
        [FromForm] string? ParentRelPath)
    {
        FolderName = (FolderName ?? "").Trim();
        if (CustomerId <= 0 || string.IsNullOrWhiteSpace(FolderName))
            return Redirect(Request.Headers.Referer.ToString());

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == CustomerId);
        if (customer is null)
            return Redirect(Request.Headers.Referer.ToString());

        _files.CreateFolder(customer.Id, customer.Name, FolderName, ParentRelPath);
        return Redirect(Request.Headers.Referer.ToString());
    }

    public async Task<IActionResult> OnPostCreateNotesAsync(
        [FromForm] int CustomerId,
        [FromForm] string? Title,
        [FromForm] string? ParentRelPath)
    {
        if (CustomerId <= 0)
            return Redirect(Request.Headers.Referer.ToString());

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == CustomerId);
        if (customer is null)
            return Redirect(Request.Headers.Referer.ToString());

        var rel = _files.CreateNotes(customer.Id, customer.Name, Title, ParentRelPath);

        var encoded = Uri.EscapeDataString(rel);
        return Redirect($"/Customers/Note?customerId={customer.Id}&relPath={encoded}");
    }

    public async Task<IActionResult> OnPostDeleteNoteAsync([FromForm] int CustomerId, [FromForm] string RelPath)
    {
        if (CustomerId <= 0)
            return Redirect(Request.Headers.Referer.ToString());

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == CustomerId);
        if (customer is null)
            return Redirect(Request.Headers.Referer.ToString());

        try { _files.DeleteNote(CustomerId, RelPath); } catch { }
        return Redirect(Request.Headers.Referer.ToString());
    }

    public async Task<IActionResult> OnPostMoveNoteAsync(
        [FromForm] int CustomerId,
        [FromForm] string SourceRelPath,
        [FromForm] string TargetFolderRelPath)
    {
        if (CustomerId <= 0)
            return Redirect(Request.Headers.Referer.ToString());

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == CustomerId);

        if (customer is null)
            return Redirect(Request.Headers.Referer.ToString());

        try
        {
            _files.MoveNote(CustomerId, SourceRelPath, TargetFolderRelPath);
        }
        catch { }

        return Redirect(Request.Headers.Referer.ToString());
    }

    // NEW: Rename Note (FS)  — Parameternamen wie bei Delete/Move
    public async Task<IActionResult> OnPostRenameNoteAsync(
        [FromForm] int CustomerId,
        [FromForm] string RelPath,
        [FromForm] string NewName)
    {
        if (CustomerId <= 0)
            return Redirect(Request.Headers.Referer.ToString());

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == CustomerId);
        if (customer is null)
            return Redirect(Request.Headers.Referer.ToString());

        NewName = (NewName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(NewName))
            return Redirect(Request.Headers.Referer.ToString());

        try { _files.RenameNote(CustomerId, RelPath, NewName); } catch { }
        return Redirect(Request.Headers.Referer.ToString());
    }

    // NEW: Rename Folder (FS)
    public async Task<IActionResult> OnPostRenameFolderAsync(
        [FromForm] int CustomerId,
        [FromForm] string RelPath,
        [FromForm] string NewName)
    {
        if (CustomerId <= 0)
            return Redirect(Request.Headers.Referer.ToString());

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == CustomerId);
        if (customer is null)
            return Redirect(Request.Headers.Referer.ToString());

        NewName = (NewName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(NewName))
            return Redirect(Request.Headers.Referer.ToString());

        try { _files.RenameFolder(CustomerId, RelPath, NewName); } catch { }
        return Redirect(Request.Headers.Referer.ToString());
    }

    private string ValidateFolderId(int customerId, string folderId)
    {
        folderId = (folderId ?? "").Trim();
        if (folderId.Length == 0) return "root";
        if (folderId.Equals("root", StringComparison.OrdinalIgnoreCase)) return "root";

        try
        {
            var abs = _files.GetAbsolutePathForCustomer(customerId, folderId);
            if (Directory.Exists(abs))
                return folderId.Replace('\\', '/').Trim('/');
        }
        catch { }

        return "root";
    }

    // NEW: Delete Folder (FS + DB docs) rekursiv
    public async Task<IActionResult> OnPostDeleteFolderAsync(
        [FromForm] int CustomerId,
        [FromForm] string RelPath)
    {
        if (CustomerId <= 0)
            return Redirect(Request.Headers.Referer.ToString());

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == CustomerId);
        if (customer is null)
            return Redirect(Request.Headers.Referer.ToString());

        // --- FIX: Never delete root ("Dokumente") ---
        var relGuard = (RelPath ?? "").Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(relGuard) || relGuard.Equals("root", StringComparison.OrdinalIgnoreCase))
            return Redirect(Request.Headers.Referer.ToString());
        // -------------------------------------------

        // FS
        try { _files.DeleteFolderRecursive(CustomerId, RelPath); } catch { }

        // DB docs (FolderId ist RelPath)
        var rel = (RelPath ?? "").Replace('\\', '/').Trim('/');
        var normalized = rel.Length == 0 ? "" : (rel + "/");

        var docs = _db.DocumentItems.Where(d =>
            d.CustomerId == CustomerId &&
            (
                (d.FolderId ?? "root") == rel ||
                (!string.IsNullOrEmpty(normalized) && (d.FolderId ?? "").StartsWith(normalized))
            )
        );

        _db.DocumentItems.RemoveRange(docs);
        await _db.SaveChangesAsync();

        return Redirect(Request.Headers.Referer.ToString());
    }

    // -------------------------
    // Documents (DB + Storage)
    // -------------------------
    public async Task<IActionResult> OnPostRenameDocumentAsync(
        [FromForm] Guid DocumentId,
        [FromForm] string NewName,
        CancellationToken ct)
    {
        var item = await _db.DocumentItems.FirstOrDefaultAsync(x => x.Id == DocumentId, ct);
        if (item == null) return Redirect(Request.Headers.Referer.ToString());

        NewName = (NewName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(NewName))
            return Redirect(Request.Headers.Referer.ToString());

        var oldExt = Path.GetExtension(item.OriginalFileName);
        var newExt = Path.GetExtension(NewName);

        if (string.IsNullOrWhiteSpace(newExt) && !string.IsNullOrWhiteSpace(oldExt))
            NewName += oldExt;

        NewName = Path.GetFileName(NewName);

        item.OriginalFileName = NewName;
        await _db.SaveChangesAsync(ct);

        return Redirect(Request.Headers.Referer.ToString());
    }

    public async Task<IActionResult> OnPostMoveDocumentAsync(
    [FromForm] int CustomerId,
    [FromForm] Guid DocumentId,
    [FromForm] string TargetFolderId)
    {
        if (CustomerId <= 0)
            return Redirect(Request.Headers.Referer.ToString());

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == CustomerId);
        if (customer is null)
            return Redirect(Request.Headers.Referer.ToString());

        var doc = await _db.DocumentItems.FirstOrDefaultAsync(d => d.Id == DocumentId && d.CustomerId == CustomerId);
        if (doc == null) return Redirect(Request.Headers.Referer.ToString());

        TargetFolderId = ValidateFolderId(CustomerId, TargetFolderId);

        // Normalize: never use "root"
        if (string.IsNullOrWhiteSpace(TargetFolderId) || TargetFolderId.Equals("root", StringComparison.OrdinalIgnoreCase))
            TargetFolderId = "Dokumente";

        try
        {
            // current absolute file
            var oldAbs = _storage.GetAbsolutePathFromRelative(doc.StoredPath);

            // ensure target folder exists under customer FS
            try
            {
                var targetFolderAbs = _files.GetAbsolutePathForCustomer(CustomerId, TargetFolderId);
                Directory.CreateDirectory(targetFolderAbs);
            }
            catch
            {
                // if anything goes wrong, fall back to Dokumente
                TargetFolderId = "Dokumente";
                var targetFolderAbs = _files.GetAbsolutePathForCustomer(CustomerId, TargetFolderId);
                Directory.CreateDirectory(targetFolderAbs);
            }

            // compute new StoredPath (keep original filename)
            var originalName = Path.GetFileName(doc.OriginalFileName);
            if (string.IsNullOrWhiteSpace(originalName))
                originalName = Path.GetFileName(oldAbs);

            if (string.IsNullOrWhiteSpace(originalName))
                originalName = "upload.pdf";

            // collision handling: name.pdf -> name_2.pdf ...
            string candidate = originalName;
            string baseName = Path.GetFileNameWithoutExtension(originalName);
            string ext = Path.GetExtension(originalName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";

            string newStoredPath;
            string newAbs;

            int i = 2;
            while (true)
            {
                newStoredPath = _storage.BuildStoredRelativePathForFilename(CustomerId, TargetFolderId, candidate);
                newAbs = _storage.GetAbsolutePathFromRelative(newStoredPath);

                if (!System.IO.File.Exists(newAbs))
                    break;

                candidate = $"{baseName}_{i}{ext}";
                i++;
            }

            // move physical file (if it exists)
            if (System.IO.File.Exists(oldAbs))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newAbs)!);
                System.IO.File.Move(oldAbs, newAbs);
            }

            // update DB
            doc.FolderId = TargetFolderId;
            doc.StoredPath = newStoredPath;

            await _db.SaveChangesAsync();
        }
        catch
        {
            // keep best-effort behavior: if anything fails, don't break UX
            // (document will remain where it was)
        }

        return Redirect(Request.Headers.Referer.ToString());
    }

    // NEW: Delete Document (DB + Storage)
    public async Task<IActionResult> OnPostDeleteDocumentAsync(
        [FromForm] Guid DocumentId,
        CancellationToken ct)
    {
        var doc = await _db.DocumentItems.FirstOrDefaultAsync(d => d.Id == DocumentId, ct);
        if (doc == null) return Redirect(Request.Headers.Referer.ToString());

        // physical file best-effort
        try
        {
            if (!string.IsNullOrWhiteSpace(doc.StoredPath))
            {
                var abs = _storage.GetAbsolutePathFromRelative(doc.StoredPath);
                if (System.IO.File.Exists(abs))
                    System.IO.File.Delete(abs);

                var dir = Path.GetDirectoryName(abs);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir, recursive: false);
                }
            }
        }
        catch { }

        _db.DocumentItems.Remove(doc);
        await _db.SaveChangesAsync(ct);

        return Redirect(Request.Headers.Referer.ToString());
    }
}
