using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PreLedgerORC.Data;
using PreLedgerORC.Services;

namespace PreLedgerORC.Pages.Documents;

public class OpenModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly DocumentStorageService _storage;

    public OpenModel(AppDbContext db, DocumentStorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var item = await _db.DocumentItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item == null) return NotFound();

        var abs = _storage.GetAbsolutePathFromRelative(item.StoredPath);

        if (!System.IO.File.Exists(abs))
            return NotFound();

        _storage.TryGetContentType(item.OriginalFileName, out var contentType);

        Response.Headers["X-Content-Type-Options"] = "nosniff";

        // LÖSUNG: Wir erstellen das Result-Objekt direkt. 
        // So können wir FileDownloadName UND EnableRangeProcessing gleichzeitig setzen.
        return new PhysicalFileResult(abs, contentType)
        {
            FileDownloadName = item.OriginalFileName,
            EnableRangeProcessing = true
        };
    }
}