using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace QuicRemote.Integration.Tests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public class QuicDebugTest
{
    [Fact]
    public async Task CheckQuicSupport()
    {
        // Check if QUIC is supported
        var isSupported = QuicListener.IsSupported;
        Console.WriteLine($"QUIC Listener supported: {isSupported}");

        var isConnectionSupported = QuicConnection.IsSupported;
        Console.WriteLine($"QUIC Connection supported: {isConnectionSupported}");

        Assert.True(isSupported, "QUIC Listener should be supported");
        Assert.True(isConnectionSupported, "QUIC Connection should be supported");
    }

    [Fact]
    public async Task CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        Assert.NotNull(cert);
        Console.WriteLine($"Certificate created: {cert.Subject}");
    }

    [Fact(Timeout = 5000)]
    public async Task QuicListener_CreateWithTimeout()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 4850);

        var cert = GenerateCert();
        var options = new QuicListenerOptions
        {
            ListenEndPoint = endpoint,
            ApplicationProtocols = new List<SslApplicationProtocol>
            {
                new SslApplicationProtocol("test")
            },
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(
                new QuicServerConnectionOptions
                {
                    ServerAuthenticationOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificate = cert
                    }
                })
        };

        try
        {
            await using var listener = await QuicListener.ListenAsync(options);
            Console.WriteLine($"Listener created on {listener.LocalEndPoint}");
            Assert.NotNull(listener);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    [Fact(Timeout = 10000)]
    public async Task QuicListenerAndConnection_FullTest()
    {
        var port = 4851;
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);

        var cert = GenerateCert();
        var options = new QuicListenerOptions
        {
            ListenEndPoint = endpoint,
            ApplicationProtocols = new List<SslApplicationProtocol>
            {
                new SslApplicationProtocol("test")
            },
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(
                new QuicServerConnectionOptions
                {
                    ServerAuthenticationOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificate = cert,
                        ApplicationProtocols = new List<SslApplicationProtocol>
                        {
                            new SslApplicationProtocol("test")
                        }
                    },
                    DefaultStreamErrorCode = 1,
                    DefaultCloseErrorCode = 1
                })
        };

        await using var listener = await QuicListener.ListenAsync(options);
        Console.WriteLine($"Listener created on {listener.LocalEndPoint}");

        // Start accept task
        var acceptTask = listener.AcceptConnectionAsync();

        // Connect from client
        var clientOptions = new QuicClientConnectionOptions
        {
            RemoteEndPoint = endpoint,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
                ApplicationProtocols = new List<SslApplicationProtocol>
                {
                    new SslApplicationProtocol("test")
                }
            },
            DefaultStreamErrorCode = 1,  // Must be > 0
            DefaultCloseErrorCode = 1   // Must be > 0
        };

        var clientConnection = await System.Net.Quic.QuicConnection.ConnectAsync(clientOptions);
        Console.WriteLine("Client connected");

        var serverConnection = await acceptTask;
        Console.WriteLine("Server accepted connection");

        Assert.NotNull(serverConnection);
        Assert.NotNull(clientConnection);

        await serverConnection.CloseAsync(0);
        await serverConnection.DisposeAsync();
        await clientConnection.CloseAsync(0);
        await clientConnection.DisposeAsync();

        Console.WriteLine("Test completed successfully");
    }

    private static X509Certificate2 GenerateCert()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=QuicTest",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        return new X509Certificate2(
            cert.Export(X509ContentType.Pfx, "test"),
            "test");
    }
}
