using System;
using System.ComponentModel.DataAnnotations;

namespace PreLedgerORC.Models;

public enum DocumentStatus
{
    Pending = 0,
    Stored = 1,
    OcrQueued = 2,
    OcrDone = 3,
    ExportReady = 4,
    Failed = 5
}

public class DocumentItem
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public int CustomerId { get; set; }

    /// <summary>
    /// Folder identifier (filesystem-based). For MVP we store either "root" or a relative folder path.
    /// Nullable when unknown.
    /// </summary>
    public string? FolderId { get; set; }

    [MaxLength(255)]
    public string OriginalFileName { get; set; } = "";

    /// <summary>
    /// Stored relative path (project-root relative, slash-separated).
    /// Example: Data/12/root/Documents/2026-02-02/{guid}/original.pdf
    /// </summary>
    [MaxLength(1024)]
    public string StoredPath { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;

    [MaxLength(1024)]
    public string? ErrorMessage { get; set; }
}
