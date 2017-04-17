using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WebSocketManager.Common;
using WebSocketManager.Common.Serialization;
using WebSocketManager.Sockets;
using Xunit;

namespace WebSocketManager.Tests
{
    public class HubTests
    {
        [Fact]
        public async void CanCallSynchronousMethodOnHub()
        {
            var serviceProvider = CreateServiceProvider();
            var actualHub = serviceProvider.GetRequiredService<HubWebSocketHandler<TestHub>>();
            var webSocket = new Mock<WebSocket>();
            await actualHub.OnConnected(webSocket.Object, new DefaultHttpContext());
            await SendMessageToSocketAsync(actualHub, webSocket.Object, nameof(TestHub.SynchronousMethod), null);
        }

        private async Task SendMessageToSocketAsync(WebSocketHandler handler, WebSocket webSocket, string methodName, params object[] args)
        {
            var serializedMessage = Json.SerializeObject(new InvocationDescriptor()
            {
                MethodName = methodName,
                Arguments = args
            });
            await handler.ReceiveAsync(webSocket, null, serializedMessage);
        }

        private WebSocket CreateLoopbackWebSocket(WebSocketHandler handler)
        {
            var webSocket = new Mock<WebSocket>();
            webSocket.Setup(
                    socket =>
                        socket.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(),
                            It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback(
                    async (ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken cts) =>
                    {
                        using (var ms = new MemoryStream())
                        {
                            await ms.WriteAsync(buffer.ToArray(), 0, buffer.Count, cts);
                            ms.Seek(0, SeekOrigin.Begin);
                            using (var reader = new StreamReader(ms, Encoding.UTF8))
                            {
                                var serializedInvocationDescriptor = await reader.ReadToEndAsync().ConfigureAwait(false);
                                await handler.ReceiveAsync(webSocket.Object, null, serializedInvocationDescriptor);
                            }
                        }
                    });
            webSocket.SetupGet(socket => socket.State).Returns(WebSocketState.Open);
            return webSocket.Object;
        }

        private IServiceProvider CreateServiceProvider(Action<ServiceCollection> addServices = null)
        {
            var services = new ServiceCollection();
            services.AddOptions()
                .AddLogging()
                .AddWebSocketManager();

            addServices?.Invoke(services);

            return services.BuildServiceProvider();
        }

        public class TestHub : Hub
        {
            public int SynchronousMethod()
            {
                return 5;
            }
        }
    }
}
