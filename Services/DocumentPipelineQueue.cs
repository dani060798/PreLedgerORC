using System.Threading;
using System;
using System.Threading.Channels;

namespace PreLedgerORC.Services;

public interface IDocumentPipelineQueue
{
    void Enqueue(Guid documentId);
    ValueTask<Guid> DequeueAsync(CancellationToken ct);
}

public class DocumentPipelineQueue : IDocumentPipelineQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public void Enqueue(Guid documentId)
    {
        _channel.Writer.TryWrite(documentId);
    }

    public async ValueTask<Guid> DequeueAsync(CancellationToken ct)
    {
        return await _channel.Reader.ReadAsync(ct);
    }
}