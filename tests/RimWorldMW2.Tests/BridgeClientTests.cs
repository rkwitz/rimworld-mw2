using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RimWorldMW2;
using Xunit;

namespace RimWorldMW2.Tests
{
    /// <summary>BridgeClient against a real loopback socket standing in for IW4L.</summary>
    public class BridgeClientTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [Fact]
        public void SendsAndReceivesLines()
        {
            using (var listener = Listen(out int port))
            using (var client = new BridgeClient("127.0.0.1", port))
            using (TcpClient server = Accept(listener))
            {
                WaitFor(() => client.Connected, "client to connect");
                var stream = server.GetStream();
                var reader = new StreamReader(stream, new UTF8Encoding(false));
                var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };

                client.Send("{\"t\":\"tick\",\"tick\":1}");
                client.Send("{\"t\":\"tick\",\"tick\":2}");
                Assert.Equal("{\"t\":\"tick\",\"tick\":1}", reader.ReadLine());
                Assert.Equal("{\"t\":\"tick\",\"tick\":2}", reader.ReadLine());

                writer.Write("{\"t\":\"use\",\"pawn\":\"Human12\"}\n\n{\"t\":\"menu\",\"open\":true}\n");
                WaitFor(() => client.Inbound.Count >= 2, "two inbound lines");
                Assert.True(client.Inbound.TryDequeue(out string first));
                Assert.True(client.Inbound.TryDequeue(out string second));
                Assert.Equal("{\"t\":\"use\",\"pawn\":\"Human12\"}", first);
                Assert.Equal("{\"t\":\"menu\",\"open\":true}", second);
                Assert.True(client.Inbound.IsEmpty, "blank lines are skipped");

                Assert.True(client.StatusMessages.TryDequeue(out string status));
                Assert.Equal($"connected to 127.0.0.1:{port}", status);
            }
        }

        [Fact]
        public void DropsSendsWhileDisconnected()
        {
            using (var listener = Listen(out int port))
            {
                listener.Stop();
                using (var client = new BridgeClient("127.0.0.1", port))
                {
                    client.Send("{\"t\":\"tick\",\"tick\":1}");
                    Assert.False(client.Connected);
                }
            }
        }

        [Fact]
        public void ReconnectsAfterTheEngineGoesAway()
        {
            using (var listener = Listen(out int port))
            using (var client = new BridgeClient("127.0.0.1", port))
            {
                using (Accept(listener))
                    WaitFor(() => client.Connected, "first connection");
                WaitFor(() => !client.Connected, "disconnect to be noticed");

                using (TcpClient again = Accept(listener))
                {
                    WaitFor(() => client.Connected, "reconnection");
                    client.Send("{\"t\":\"tick\",\"tick\":3}");
                    var reader = new StreamReader(again.GetStream(), new UTF8Encoding(false));
                    Assert.Equal("{\"t\":\"tick\",\"tick\":3}", reader.ReadLine());
                }
                Assert.Contains("disconnected", client.StatusMessages);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DisposeReturnsPromptly(bool engineRunning)
        {
            // M19: RimWorld crashed on quit while this thread still waited on a socket.
            using (var listener = Listen(out int port))
            {
                if (!engineRunning) listener.Stop();
                var client = new BridgeClient("127.0.0.1", port);
                TcpClient server = engineRunning ? Accept(listener) : null;
                if (engineRunning) WaitFor(() => client.Connected, "client to connect");

                var watch = Stopwatch.StartNew();
                client.Dispose();
                watch.Stop();
                server?.Dispose();
                Assert.True(watch.ElapsedMilliseconds < 1500, $"Dispose took {watch.ElapsedMilliseconds} ms");
                Assert.False(client.Connected);
            }
        }

        private static TcpListener Listen(out int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return listener;
        }

        private static TcpClient Accept(TcpListener listener)
        {
            var accept = listener.AcceptTcpClientAsync();
            Assert.True(accept.Wait(Timeout), "the client never connected");
            TcpClient server = accept.Result;
            server.ReceiveTimeout = (int)Timeout.TotalMilliseconds;
            return server;
        }

        private static void WaitFor(Func<bool> condition, string what)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.Elapsed > Timeout) throw new TimeoutException("timed out waiting for " + what);
                System.Threading.Thread.Sleep(10);
            }
        }
    }
}
