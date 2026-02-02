using System.Threading.Tasks;
using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PreLedgerORC.Data;
using PreLedgerORC.Models;
using PreLedgerORC.Services;

namespace PreLedgerORC.Pages.Customers;

public class ActionsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly CustomerFilesService _files;

    public ActionsModel(AppDbContext db, CustomerFilesService files)
    {
        _db = db;
        _files = files;
    }

    public IActionResult OnGet() => RedirectToPage("/Index");

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

        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync();

        return Redirect("/Index");
    }

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

        // Open the new note directly
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

        try
        {
            _files.DeleteNote(CustomerId, RelPath);
        }
        catch
        {
            // ignore
        }

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
        catch
        {
            // ignore
        }

        return Redirect(Request.Headers.Referer.ToString());
    }
}
