using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PreLedgerORC.Data;
using PreLedgerORC.Services;

namespace PreLedgerORC.ViewComponents;

public class CustomersSidebarViewComponent : ViewComponent
{
    private readonly AppDbContext _db;
    private readonly CustomerFilesService _files;

    public CustomersSidebarViewComponent(AppDbContext db, CustomerFilesService files)
    {
        _db = db;
        _files = files;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var customers = await _db.Customers
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();

        var result = new List<CustomerTreeVm>();

        foreach (var c in customers)
        {
            var vm = new CustomerTreeVm
            {
                Id = c.Id,
                Name = c.Name,
                Nodes = new List<TreeNodeVm>()
            };

            try
            {
                var rootDir = _files.ResolveCustomerDirectoryById(c.Id);
                vm.Nodes = BuildTree(rootDir, "");
            }
            catch
            {
                // folder might not exist yet; keep empty
            }

            result.Add(vm);
        }

        return View(result);
    }

    private static List<TreeNodeVm> BuildTree(string absDir, string relBase)
    {
        var nodes = new List<TreeNodeVm>();

        // Folders
        foreach (var dir in Directory.EnumerateDirectories(absDir).OrderBy(Path.GetFileName))
        {
            var name = Path.GetFileName(dir);
            var rel = CombineRel(relBase, name);

            var folder = new TreeNodeVm
            {
                Name = name,
                RelPath = rel,
                IsFolder = true,
                Children = BuildTree(dir, rel)
            };

            nodes.Add(folder);
        }

        // Notes
        foreach (var file in Directory.EnumerateFiles(absDir).OrderBy(Path.GetFileName))
        {
            var fn = Path.GetFileName(file);

            if (!fn.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase) &&
                !fn.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                continue;

            var rel = CombineRel(relBase, fn);

            nodes.Add(new TreeNodeVm
            {
                Name = PrettyNoteName(fn),
                RelPath = rel,
                IsFolder = false,
                Children = new List<TreeNodeVm>()
            });
        }

        return nodes;
    }

    private static string CombineRel(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b.Replace('\\', '/');
        return (a.TrimEnd('/') + "/" + b).Replace('\\', '/');
    }

    private static string PrettyNoteName(string filename)
    {
        // remove extensions
        var name = filename;
        if (name.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase))
            name = name[..^(".note.json".Length)];
        else if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            name = name[..^(".md".Length)];

        // remove leading timestamp "yyyy-MM-dd_HH-mm-ss_"
        if (name.Length > 20 && name[4] == '-' && name[7] == '-' && name[10] == '_' && name[13] == '-' && name[16] == '-')
        {
            var idx = name.IndexOf('_', 19);
            if (idx >= 0 && idx + 1 < name.Length)
                name = name[(idx + 1)..];
        }

        return name.Length == 0 ? "notes" : name;
    }

    public class CustomerTreeVm
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public List<TreeNodeVm> Nodes { get; set; } = new();
    }

    public class TreeNodeVm
    {
        public string Name { get; set; } = "";
        public string RelPath { get; set; } = ""; // folder rel or file rel
        public bool IsFolder { get; set; }
        public List<TreeNodeVm> Children { get; set; } = new();
    }
}
