using System.Net;
using System.Net.Quic;
using System.Net.Security;

namespace QuicRemote.Network.Quic;

public sealed class QuicConnection : IAsyncDisposable
{
    private readonly System.Net.Quic.QuicConnection _connection;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    private bool _connected = true;

    public EndPoint RemoteEndPoint => _connection.RemoteEndPoint;
    public bool IsConnected => _connected && !_disposed;
    public TimeSpan RoundTripTime => TimeSpan.Zero; // Not directly available in current API

    internal QuicConnection(System.Net.Quic.QuicConnection connection)
    {
        _connection = connection;
    }

    public static async Task<QuicConnection> ConnectAsync(
        IPEndPoint endpoint,
        string serverName,
        CancellationToken cancellationToken = default)
    {
        var connectionOptions = new QuicClientConnectionOptions
        {
            RemoteEndPoint = endpoint,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                TargetHost = serverName,
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
                ApplicationProtocols = new List<SslApplicationProtocol>
                {
                    new SslApplicationProtocol("quicremote")
                }
            },
            DefaultStreamErrorCode = 1,  // Must be > 0
            DefaultCloseErrorCode = 1,   // Must be > 0
            MaxInboundUnidirectionalStreams = 10,
            MaxInboundBidirectionalStreams = 10,
            IdleTimeout = TimeSpan.FromMinutes(5)
        };

        var connection = await System.Net.Quic.QuicConnection.ConnectAsync(
            connectionOptions, cancellationToken);

        return new QuicConnection(connection);
    }

    public async Task<QuicStream> OpenStreamAsync(CancellationToken cancellationToken = default)
    {
        var stream = await _connection.OpenOutboundStreamAsync(
            QuicStreamType.Bidirectional, cancellationToken);
        return new QuicStream(stream);
    }

    public async Task<QuicStream> AcceptStreamAsync(CancellationToken cancellationToken = default)
    {
        var stream = await _connection.AcceptInboundStreamAsync(cancellationToken);
        return new QuicStream(stream);
    }

    public async Task CloseAsync(long errorCode = 0, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        _connected = false;
        await _connection.CloseAsync(errorCode, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _connected = false;
        _cts.Cancel();
        await _connection.DisposeAsync();
        _cts.Dispose();
    }
}
