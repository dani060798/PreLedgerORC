using System.Collections.Generic;
using System.IO;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PreLedgerORC.Services;

namespace PreLedgerORC.Pages.Customers;

public class NoteModel : PageModel
{
    private readonly CustomerFilesService _files;

    public NoteModel(CustomerFilesService files)
    {
        _files = files;
    }

    public int CustomerId { get; set; }
    public string RelPath { get; set; } = "";
    public string NoteTitle { get; set; } = "";

    [BindProperty]
    public string DeltaJson { get; set; } = "";

    public IActionResult OnGet(int customerId, string relPath)
    {
        CustomerId = customerId;
        RelPath = (relPath ?? "").Replace("\\", "/");
        NoteTitle = Path.GetFileName(RelPath);

        try
        {
            if (RelPath.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase))
            {
                var raw = _files.ReadNoteDeltaJson(customerId, RelPath);
                DeltaJson = CleanDeltaJsonOrFallback(raw);
                return Page();
            }

            if (RelPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                var md = _files.ReadNoteMarkdown(customerId, RelPath);
                DeltaJson = CreatePlainTextDelta(md ?? "");
                return Page();
            }

            return Redirect($"/Customers/{customerId}");
        }
        catch
        {
            return Redirect($"/Customers/{customerId}");
        }
    }

    public IActionResult OnPost(int customerId, string relPath)
    {
        CustomerId = customerId;
        RelPath = (relPath ?? "").Replace("\\", "/");
        NoteTitle = Path.GetFileName(RelPath);

        try
        {
            // Persist Delta for .note.json
            if (RelPath.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase))
            {
                var cleaned = CleanDeltaJsonOrFallback(DeltaJson);
                _files.WriteNoteDeltaJson(customerId, RelPath, cleaned);
                return Redirect($"/Customers/Note?customerId={customerId}&relPath={Uri.EscapeDataString(RelPath)}");
            }

            // Legacy: write plaintext to .md
            if (RelPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                var text = ExtractPlainTextFromDelta(DeltaJson);
                _files.WriteNoteMarkdown(customerId, RelPath, text);
                return Redirect($"/Customers/Note?customerId={customerId}&relPath={Uri.EscapeDataString(RelPath)}");
            }

            return Redirect($"/Customers/{customerId}");
        }
        catch
        {
            return Redirect($"/Customers/{customerId}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static string CreatePlainTextDelta(string text)
    {
        if (!text.EndsWith('\n')) text += "\n";

        var obj = new DeltaRoot
        {
            Ops = new List<DeltaOp> { new DeltaOp { Insert = text } }
        };

        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    private static string ExtractPlainTextFromDelta(string? raw)
    {
        raw ??= "";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("ops", out var opsEl) || opsEl.ValueKind != JsonValueKind.Array)
                return "";

            var sb = new System.Text.StringBuilder();
            foreach (var opEl in opsEl.EnumerateArray())
            {
                if (opEl.ValueKind != JsonValueKind.Object) continue;
                if (!opEl.TryGetProperty("insert", out var insEl)) continue;
                if (insEl.ValueKind != JsonValueKind.String) continue;

                sb.Append(insEl.GetString());
            }
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    // Security hardening: remove embeds, allow only a small attribute set and safe link schemes
    private static string CleanDeltaJsonOrFallback(string? raw)
    {
        raw ??= "";
        raw = raw.Trim();
        if (raw.Length == 0)
            return """{"ops":[{"insert":"\n"}]}""";

        try
        {
            using var doc = JsonDocument.Parse(raw);

            if (!doc.RootElement.TryGetProperty("ops", out var opsEl) || opsEl.ValueKind != JsonValueKind.Array)
                return """{"ops":[{"insert":"\n"}]}""";

            var cleanedOps = new List<DeltaOp>();

            foreach (var opEl in opsEl.EnumerateArray())
            {
                if (opEl.ValueKind != JsonValueKind.Object)
                    continue;

                if (!opEl.TryGetProperty("insert", out var insEl))
                    continue;

                // allow only string insert (no embeds)
                if (insEl.ValueKind != JsonValueKind.String)
                    continue;

                var insertText = insEl.GetString() ?? "";
                if (insertText.Length == 0)
                    continue;

                var cleaned = new DeltaOp { Insert = insertText };

                if (opEl.TryGetProperty("attributes", out var attrEl) && attrEl.ValueKind == JsonValueKind.Object)
                {
                    var attrs = new Dictionary<string, object>();

                    CopyBool(attrEl, "bold", attrs);
                    CopyBool(attrEl, "italic", attrs);
                    CopyBool(attrEl, "underline", attrs);

                    if (attrEl.TryGetProperty("header", out var headerEl) && headerEl.ValueKind == JsonValueKind.Number)
                    {
                        var h = headerEl.GetInt32();
                        if (h is >= 1 and <= 3) attrs["header"] = h;
                    }

                    if (attrEl.TryGetProperty("list", out var listEl) && listEl.ValueKind == JsonValueKind.String)
                    {
                        var v = (listEl.GetString() ?? "").Trim().ToLowerInvariant();
                        if (v == "ordered" || v == "bullet") attrs["list"] = v;
                    }

                    if (attrEl.TryGetProperty("link", out var linkEl) && linkEl.ValueKind == JsonValueKind.String)
                    {
                        var link = (linkEl.GetString() ?? "").Trim();
                        if (IsSafeLink(link)) attrs["link"] = link;
                    }

                    if (attrs.Count > 0)
                        cleaned.Attributes = attrs;
                }

                cleanedOps.Add(cleaned);
            }

            if (cleanedOps.Count == 0)
                return """{"ops":[{"insert":"\n"}]}""";

            // Ensure ends with newline
            var last = cleanedOps[^1];
            if (last.Insert is not null && !last.Insert.EndsWith('\n'))
                cleanedOps.Add(new DeltaOp { Insert = "\n" });

            var root = new DeltaRoot { Ops = cleanedOps };
            return JsonSerializer.Serialize(root, JsonOptions);
        }
        catch
        {
            return """{"ops":[{"insert":"\n"}]}""";
        }
    }

    private static void CopyBool(JsonElement attrEl, string name, Dictionary<string, object> dest)
    {
        if (attrEl.TryGetProperty(name, out var b) && (b.ValueKind == JsonValueKind.True || b.ValueKind == JsonValueKind.False))
            dest[name] = b.GetBoolean();
    }

    private static bool IsSafeLink(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return false;

        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            return false;

        var scheme = uri.Scheme.ToLowerInvariant();
        return scheme == "http" || scheme == "https" || scheme == "mailto";
    }

    private sealed class DeltaRoot
    {
        public List<DeltaOp> Ops { get; set; } = new();
    }

    private sealed class DeltaOp
    {
        public string? Insert { get; set; }
        public Dictionary<string, object>? Attributes { get; set; }
    }
}
