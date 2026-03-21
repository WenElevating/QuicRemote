using System.Buffers;
using System.IO.Pipelines;
using System.Net.Quic;

namespace QuicRemote.Network.Quic;

public sealed class QuicStream : IAsyncDisposable
{
    private readonly System.Net.Quic.QuicStream _stream;
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;
    private bool _disposed;

    public long StreamId => _stream.Id;
    public QuicStreamType StreamType => _stream.Type;
    public bool CanRead => _stream.CanRead;
    public bool CanWrite => _stream.CanWrite;

    internal QuicStream(System.Net.Quic.QuicStream stream)
    {
        _stream = stream;
        _reader = PipeReader.Create(stream);
        _writer = PipeWriter.Create(stream);
    }

    public async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        return await _reader.ReadAsync(cancellationToken);
    }

    public void AdvanceTo(SequencePosition consumed)
    {
        _reader.AdvanceTo(consumed);
    }

    public void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        _reader.AdvanceTo(consumed, examined);
    }

    public async ValueTask<FlushResult> WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        _writer.Write(buffer.Span);
        return await _writer.FlushAsync(cancellationToken);
    }

    public async ValueTask CompleteWriteAsync(CancellationToken cancellationToken = default)
    {
        await _writer.CompleteAsync();
    }

    public async ValueTask CompleteAsync(long errorCode = 0)
    {
        await _reader.CompleteAsync();
        await _writer.CompleteAsync();
        _stream.Abort(QuicAbortDirection.Both, errorCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _reader.CompleteAsync();
        await _writer.CompleteAsync();
        await _stream.DisposeAsync();
    }
}
