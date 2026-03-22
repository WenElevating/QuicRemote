using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace QuicRemote.Network.Quic;

public sealed class QuicListener : IAsyncDisposable
{
    private readonly System.Net.Quic.QuicListener _listener;
    private bool _disposed;

    public IPEndPoint LocalEndPoint => _listener.LocalEndPoint;

    private QuicListener(System.Net.Quic.QuicListener listener)
    {
        _listener = listener;
    }

    public static async Task<QuicListener> CreateAsync(
        IPEndPoint listenEndpoint,
        X509Certificate2? certificate = null,
        CancellationToken cancellationToken = default)
    {
        certificate ??= GenerateSelfSignedCertificate();

        var serverAuthenticationOptions = new SslServerAuthenticationOptions
        {
            ApplicationProtocols = new List<SslApplicationProtocol>
            {
                new SslApplicationProtocol("quicremote")
            },
            ServerCertificate = certificate
        };

        var listenerOptions = new QuicListenerOptions
        {
            ListenEndPoint = listenEndpoint,
            ApplicationProtocols = new List<SslApplicationProtocol>
            {
                new SslApplicationProtocol("quicremote")
            },
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(
                new QuicServerConnectionOptions
                {
                    ServerAuthenticationOptions = serverAuthenticationOptions,
                    DefaultStreamErrorCode = 1,  // Must be > 0
                    DefaultCloseErrorCode = 1,   // Must be > 0
                    MaxInboundUnidirectionalStreams = 10,
                    MaxInboundBidirectionalStreams = 10,
                    IdleTimeout = TimeSpan.FromMinutes(5)
                })
        };

        var listener = await System.Net.Quic.QuicListener.ListenAsync(
            listenerOptions, cancellationToken);

        return new QuicListener(listener);
    }

    public async Task<QuicConnection> AcceptConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = await _listener.AcceptConnectionAsync(cancellationToken);
        return new QuicConnection(connection);
    }

    private static X509Certificate2 GenerateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=QuicRemote-Dev",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                false));

        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        return new X509Certificate2(
            certificate.Export(X509ContentType.Pfx, "quicremote"),
            "quicremote");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _listener.DisposeAsync();
    }
}
