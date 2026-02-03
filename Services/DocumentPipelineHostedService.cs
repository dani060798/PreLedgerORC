using System.Threading.Tasks;
using System.Threading;
using System;
using Microsoft.EntityFrameworkCore;
using PreLedgerORC.Data;
using PreLedgerORC.Models;

namespace PreLedgerORC.Services;

public class DocumentPipelineHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IDocumentPipelineQueue _queue;
    private readonly ILogger<DocumentPipelineHostedService> _logger;

    public DocumentPipelineHostedService(IServiceProvider sp, IDocumentPipelineQueue queue, ILogger<DocumentPipelineHostedService> logger)
    {
        _sp = sp;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DocumentPipelineHostedService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid docId;

            try
            {
                docId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var item = await db.DocumentItems.FirstOrDefaultAsync(x => x.Id == docId, stoppingToken);
                if (item == null) continue;

                if (item.Status == DocumentStatus.Pending)
                {
                    item.Status = DocumentStatus.Stored;
                    item.ErrorMessage = null;
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipeline error for document {DocumentId}", docId);
            }
        }

        _logger.LogInformation("DocumentPipelineHostedService stopped.");
    }
}