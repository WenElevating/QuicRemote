using System.Buffers;
using System.IO.Pipelines;
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
public class QuicStreamDiagnostic
{
    [Fact(Timeout = 20000)]
    public async Task QuicStream_RawStreamWorks()
    {
        // Test raw QuicStream without PipeReader to isolate the issue
        var port = 4870;
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
                    DefaultCloseErrorCode = 1,
                    MaxInboundBidirectionalStreams = 10,
                    MaxInboundUnidirectionalStreams = 10
                })
        };

        await using var listener = await QuicListener.ListenAsync(options);
        Console.WriteLine($"Listener created on {listener.LocalEndPoint}");

        var acceptTask = listener.AcceptConnectionAsync();

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
            DefaultStreamErrorCode = 1,
            DefaultCloseErrorCode = 1,
            MaxInboundBidirectionalStreams = 10,
            MaxInboundUnidirectionalStreams = 10
        };

        var clientConnection = await System.Net.Quic.QuicConnection.ConnectAsync(clientOptions);
        var serverConnection = await acceptTask;

        Console.WriteLine("Connections established");

        // Run accept and open/write in parallel using Task.Run
        var acceptStreamTask = Task.Run(async () =>
        {
            Console.WriteLine("Server starting to accept stream...");
            var stream = await serverConnection.AcceptInboundStreamAsync();
            Console.WriteLine($"Server accepted stream: {stream.Id}");
            return stream;
        });

        // Run client stream operations in a separate task
        var clientTask = Task.Run(async () =>
        {
            await Task.Delay(200); // Wait for server to start accepting
            Console.WriteLine("Client opening stream...");
            var stream = await clientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
            Console.WriteLine($"Client stream opened: {stream.Id}");

            // Immediately write data to "activate" the stream
            var sendData = new byte[] { 1, 2, 3, 4, 5 };
            await stream.WriteAsync(sendData);
            Console.WriteLine($"Client sent {sendData.Length} bytes");

            return (stream, sendData);
        });

        // Wait for both
        var serverStream = await acceptStreamTask;
        var (clientStream, sendData) = await clientTask;

        Console.WriteLine($"Both streams ready: server={serverStream.Id}, client={clientStream.Id}");

        // Read using raw stream
        var buffer = new byte[100];
        var bytesRead = await serverStream.ReadAsync(buffer);
        Console.WriteLine($"Server received {bytesRead} bytes");

        Assert.Equal(sendData.Length, bytesRead);
        Assert.Equal(sendData, buffer.Take(bytesRead).ToArray());

        await serverStream.DisposeAsync();
        await clientStream.DisposeAsync();
        await serverConnection.CloseAsync(0);
        await clientConnection.CloseAsync(0);
        await serverConnection.DisposeAsync();
        await clientConnection.DisposeAsync();

        Console.WriteLine("Raw stream test passed!");
    }

    [Fact(Timeout = 15000)]
    public async Task QuicStream_PipeReaderWorks()
    {
        // Test PipeReader with QuicStream
        var port = 4871;
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
                    DefaultCloseErrorCode = 1,
                    MaxInboundBidirectionalStreams = 10,
                    MaxInboundUnidirectionalStreams = 10
                })
        };

        await using var listener = await QuicListener.ListenAsync(options);

        var acceptTask = listener.AcceptConnectionAsync();

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
            DefaultStreamErrorCode = 1,
            DefaultCloseErrorCode = 1,
            MaxInboundBidirectionalStreams = 10,
            MaxInboundUnidirectionalStreams = 10
        };

        var clientConnection = await System.Net.Quic.QuicConnection.ConnectAsync(clientOptions);
        var serverConnection = await acceptTask;

        Console.WriteLine("Connections established");

        // Run accept in parallel
        var acceptStreamTask = Task.Run(async () =>
        {
            Console.WriteLine("Server starting to accept stream...");
            var stream = await serverConnection.AcceptInboundStreamAsync();
            Console.WriteLine($"Server accepted stream: {stream.Id}");
            return stream;
        });

        // Run client operations in parallel - must write data before server can accept
        var clientTask = Task.Run(async () =>
        {
            await Task.Delay(200); // Wait for server to start accepting
            Console.WriteLine("Client opening stream...");
            var stream = await clientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
            Console.WriteLine($"Client stream opened: {stream.Id}");

            // Create PipeWriter
            var writer = PipeWriter.Create(stream);

            // Write data
            var sendData = new byte[] { 1, 2, 3, 4, 5 };
            var span = writer.GetSpan(sendData.Length);
            sendData.CopyTo(span);
            writer.Advance(sendData.Length);
            await writer.FlushAsync();
            Console.WriteLine($"Client wrote {sendData.Length} bytes via PipeWriter");

            // Complete writing
            await writer.CompleteAsync();
            Console.WriteLine("Client completed writing");

            return (stream, sendData);
        });

        // Wait for both
        var serverQuicStream = await acceptStreamTask;
        var (clientQuicStream, sendData) = await clientTask;

        // Create PipeReader for server
        var serverReader = PipeReader.Create(serverQuicStream);

        // Read using PipeReader
        Console.WriteLine("Server starting to read...");
        var result = await serverReader.ReadAsync();
        Console.WriteLine($"Server read result: IsCompleted={result.IsCompleted}, Buffer.Length={result.Buffer.Length}");

        var receivedData = result.Buffer.FirstSpan.ToArray();
        Console.WriteLine($"Server received {receivedData.Length} bytes");

        Assert.Equal(sendData, receivedData);

        // Mark as consumed
        serverReader.AdvanceTo(result.Buffer.End);

        await serverReader.CompleteAsync();
        await clientQuicStream.DisposeAsync();
        await serverQuicStream.DisposeAsync();

        await serverConnection.CloseAsync(0);
        await clientConnection.CloseAsync(0);
        await serverConnection.DisposeAsync();
        await clientConnection.DisposeAsync();

        Console.WriteLine("PipeReader test passed!");
    }

    [Fact(Timeout = 15000)]
    public async Task QuicStream_WriteMultipleTimes()
    {
        // Test multiple writes without complete
        var port = 4872;
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
                    DefaultCloseErrorCode = 1,
                    MaxInboundBidirectionalStreams = 10,
                    MaxInboundUnidirectionalStreams = 10
                })
        };

        await using var listener = await QuicListener.ListenAsync(options);

        var acceptTask = listener.AcceptConnectionAsync();

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
            DefaultStreamErrorCode = 1,
            DefaultCloseErrorCode = 1,
            MaxInboundBidirectionalStreams = 10,
            MaxInboundUnidirectionalStreams = 10
        };

        var clientConnection = await System.Net.Quic.QuicConnection.ConnectAsync(clientOptions);
        var serverConnection = await acceptTask;

        Console.WriteLine("Connections established");

        // Run accept in parallel
        var acceptStreamTask = Task.Run(async () =>
        {
            Console.WriteLine("Server starting to accept stream...");
            var stream = await serverConnection.AcceptInboundStreamAsync();
            Console.WriteLine($"Server accepted stream: {stream.Id}");
            return stream;
        });

        // Run client operations in parallel - must write data before server can accept
        var clientTask = Task.Run(async () =>
        {
            await Task.Delay(200); // Wait for server to start accepting
            Console.WriteLine("Client opening stream...");
            var stream = await clientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
            Console.WriteLine($"Client stream opened: {stream.Id}");

            var writer = PipeWriter.Create(stream);

            // Write multiple chunks
            var chunk1 = new byte[] { 1, 2, 3 };
            var chunk2 = new byte[] { 4, 5, 6 };
            var chunk3 = new byte[] { 7, 8, 9 };

            void WriteChunk(PipeWriter w, byte[] data)
            {
                var span = w.GetSpan(data.Length);
                data.CopyTo(span);
                w.Advance(data.Length);
            }

            WriteChunk(writer, chunk1);
            await writer.FlushAsync();

            WriteChunk(writer, chunk2);
            await writer.FlushAsync();

            WriteChunk(writer, chunk3);
            await writer.FlushAsync();

            // Complete writing
            await writer.CompleteAsync();
            Console.WriteLine("Client completed writing");

            return (stream, chunk1.Concat(chunk2).Concat(chunk3).ToArray());
        });

        // Wait for both
        var serverQuicStream = await acceptStreamTask;
        var (clientQuicStream, expectedData) = await clientTask;

        var serverReader = PipeReader.Create(serverQuicStream);

        // Read all data
        var result = await serverReader.ReadAsync();
        var receivedData = result.Buffer.FirstSpan.ToArray();
        serverReader.AdvanceTo(result.Buffer.End);

        Assert.Equal(expectedData, receivedData);

        await serverReader.CompleteAsync();
        await clientQuicStream.DisposeAsync();
        await serverQuicStream.DisposeAsync();

        await serverConnection.CloseAsync(0);
        await clientConnection.CloseAsync(0);
        await serverConnection.DisposeAsync();
        await clientConnection.DisposeAsync();

        Console.WriteLine("Multiple writes test passed!");
    }

    private static X509Certificate2 GenerateCert()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=QuicStreamTest",
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
