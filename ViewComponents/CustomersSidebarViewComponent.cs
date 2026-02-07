using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PreLedgerORC.Data;
using PreLedgerORC.Services;
using PreLedgerORC.Models;
using System.Linq;

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

            // load filesystem tree (folders + notes)
            try
            {
                var rootDir = _files.ResolveCustomerDirectoryById(c.Id);

                // ensure default docs folder exists physically
                Directory.CreateDirectory(Path.Combine(rootDir, "Dokumente"));

                vm.Nodes = BuildFsTree(rootDir, "");
            }
            catch
            {
                // keep empty
            }

            // load documents from DB and inject into nodes
            var docs = await _db.DocumentItems
                 .AsNoTracking()
                 .Where(d => d.CustomerId == c.Id)
                 .OrderByDescending(d => d.CreatedAtUtc)
                 .Select(d => new DocRow
                 {
                     Id = d.Id,
                     OriginalFileName = d.OriginalFileName,
                     FolderId = d.FolderId,
                     CreatedAtUtc = d.CreatedAtUtc
                 })
                 .ToListAsync();

            InjectDocuments(vm.Nodes, docs);

            result.Add(vm);
        }

        return View(result);
    }

    private static List<TreeNodeVm> BuildFsTree(string absDir, string relBase)
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
                NodeType = TreeNodeType.Folder,
                Children = BuildFsTree(dir, rel)
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
                NodeType = TreeNodeType.Note,
                Children = new List<TreeNodeVm>()
            });
        }

        return nodes;
    }

    private static void InjectDocuments(List<TreeNodeVm> rootNodes, List<DocRow> docs)
    {
        // ✅ FIX: no virtual docs root. Only use the physical "Dokumente" folder.
        var docsRoot = FindFolderByRelPath(rootNodes, "Dokumente");
        if (docsRoot == null)
            return;

        foreach (var d in docs)
        {
            var folderId = d.FolderId ?? "Dokumente";
            folderId = string.IsNullOrWhiteSpace(folderId) ? "Dokumente" : folderId;

            if (folderId.Equals("root", StringComparison.OrdinalIgnoreCase))
                folderId = "Dokumente";

            var targetFolderNode = FindFolderByRelPath(rootNodes, folderId) ?? docsRoot;

            targetFolderNode.Children.Add(new TreeNodeVm
            {
                Name = d.OriginalFileName,
                RelPath = "",
                NodeType = TreeNodeType.Document,
                DocumentId = d.Id,
                DocumentFolderId = folderId,
                Children = new List<TreeNodeVm>()
            });
        }

        SortTree(rootNodes);
    }

    private static void SortTree(List<TreeNodeVm> nodes)
    {
        foreach (var n in nodes)
            SortTree(n.Children);

        nodes.Sort((a, b) =>
        {
            int prio(TreeNodeVm x) => x.NodeType switch
            {
                TreeNodeType.Folder => 0,
                TreeNodeType.Note => 1,
                TreeNodeType.Document => 2,
                _ => 9
            };
            var pa = prio(a);
            var pb = prio(b);
            if (pa != pb) return pa.CompareTo(pb);

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
    }

    // Kept for compatibility (unused for docs)
    private static TreeNodeVm EnsureVirtualFolder(List<TreeNodeVm> nodes, string name, string relPath)
    {
        var existing = nodes.FirstOrDefault(n => n.NodeType == TreeNodeType.Folder && n.RelPath == relPath);
        if (existing != null) return existing;

        var vf = new TreeNodeVm
        {
            Name = name,
            RelPath = relPath,
            NodeType = TreeNodeType.Folder,
            IsVirtual = true,
            Children = new List<TreeNodeVm>()
        };

        nodes.Insert(0, vf);
        return vf;
    }

    private static TreeNodeVm? FindFolderByRelPath(List<TreeNodeVm> nodes, string folderRelPath)
    {
        folderRelPath = folderRelPath.Replace('\\', '/').Trim('/');

        foreach (var n in nodes)
        {
            if (n.NodeType == TreeNodeType.Folder)
            {
                if (!string.IsNullOrWhiteSpace(n.RelPath) &&
                    n.RelPath.Replace('\\', '/').Trim('/').Equals(folderRelPath, StringComparison.OrdinalIgnoreCase))
                    return n;

                var found = FindFolderByRelPath(n.Children, folderRelPath);
                if (found != null) return found;
            }
        }

        return null;
    }

    private static string CombineRel(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b.Replace('\\', '/');
        return (a.TrimEnd('/') + "/" + b).Replace('\\', '/');
    }

    private static string PrettyNoteName(string filename)
    {
        var name = filename;
        if (name.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase))
            name = name[..^(".note.json".Length)];
        else if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            name = name[..^(".md".Length)];

        if (name.Length > 20 && name[4] == '-' && name[7] == '-' && name[10] == '_' && name[13] == '-' && name[16] == '-')
        {
            var idx = name.IndexOf('_', 19);
            if (idx >= 0 && idx + 1 < name.Length)
                name = name[(idx + 1)..];
        }

        return name.Length == 0 ? "notes" : name;
    }

    public enum TreeNodeType
    {
        Folder = 0,
        Note = 1,
        Document = 2
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
        public string RelPath { get; set; } = "";
        public TreeNodeType NodeType { get; set; }
        public List<TreeNodeVm> Children { get; set; } = new();

        public Guid? DocumentId { get; set; }
        public string? DocumentFolderId { get; set; }

        public bool IsVirtual { get; set; }
    }

    private sealed class DocRow
    {
        public Guid Id { get; init; }
        public string OriginalFileName { get; init; } = "";
        public string? FolderId { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }
}
