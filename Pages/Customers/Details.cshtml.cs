using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PreLedgerORC.Data;
using PreLedgerORC.Services;

namespace PreLedgerORC.Pages.Customers;

public class DetailsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly CustomerFilesService _files;

    public DetailsModel(AppDbContext db, CustomerFilesService files)
    {
        _db = db;
        _files = files;
    }

    public string CustomerName { get; private set; } = "";
    public int CustomerId { get; private set; }

    public List<CustomerNoteListItem> Notes { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null) return RedirectToPage("/Index");

        CustomerId = customer.Id;
        CustomerName = customer.Name;

        Notes = LoadNotesFromDisk(CustomerId);

        return Page();
    }

    private List<CustomerNoteListItem> LoadNotesFromDisk(int customerId)
    {
        try
        {
            var baseDir = _files.ResolveCustomerDirectoryById(customerId);

            var list = Directory.EnumerateFiles(baseDir, "*.*", SearchOption.AllDirectories)
                .Where(p =>
                    p.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .Select(p =>
                {
                    var rel = _files.GetRelPathFromAbsolute(customerId, p);
                    var name = Path.GetFileName(p);

                    DateTime updated;
                    try { updated = System.IO.File.GetLastWriteTimeUtc(p); }
                    catch { updated = DateTime.UtcNow; }

                    var display = name;

                    if (name.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase))
                        display = name.Substring(0, name.Length - ".note.json".Length);
                    else if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        display = name.Substring(0, name.Length - ".md".Length);

                    return new CustomerNoteListItem
                    {
                        Title = display,
                        RelPath = rel,
                        UpdatedUtc = updated
                    };
                })
                .OrderByDescending(x => x.UpdatedUtc)
                .ToList();

            return list;
        }
        catch
        {
            return new List<CustomerNoteListItem>();
        }
    }

    public sealed class CustomerNoteListItem
    {
        public string Title { get; set; } = "";
        public string RelPath { get; set; } = "";
        public DateTime UpdatedUtc { get; set; }
    }
}
