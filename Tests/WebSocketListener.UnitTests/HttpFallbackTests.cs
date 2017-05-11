﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Moq;
using vtortola.WebSockets;
using vtortola.WebSockets.Rfc6455;
using Xunit;

namespace WebSocketListener.UnitTests
{
    public class HttpFallbackTests
    {
        public HttpFallbackTests()
        {
            this.factories = new WebSocketFactoryCollection();
            this.factories.RegisterStandard(new WebSocketFactoryRfc6455());

            this.fallback = new Mock<IHttpFallback>();
            this.fallback.Setup(x => x.Post(It.IsAny<HttpRequest>(), It.IsAny<Stream>()))
                .Callback((HttpRequest r, Stream s) => this.postedConnections.Add(new Tuple<HttpRequest, Stream>(r, s)));
            this.postedConnections = new List<Tuple<HttpRequest, Stream>>();
        }
        private readonly Mock<IHttpFallback> fallback;
        private readonly List<Tuple<HttpRequest, Stream>> postedConnections;

        private readonly WebSocketFactoryCollection factories;

        [Fact]
        public void HttpFallback()
        {
            var options = new WebSocketListenerOptions();
            options.HttpFallback = this.fallback.Object;
            var handshaker = new WebSocketHandshaker(this.factories, options);

            using (var ms = new MemoryStream())
            {
                using (var sw = new StreamWriter(ms, Encoding.ASCII, 1024, true))
                {
                    sw.WriteLine(@"GET /chat HTTP/1.1");
                    sw.WriteLine(@"Host: server.example.com");
                    sw.WriteLine(@"Cookie: key=W9g/8FLW8RAFqSCWBvB9Ag==#5962c0ace89f4f780aa2a53febf2aae5;");
                    sw.WriteLine(@"Origin: http://example.com");
                }

                var position = ms.Position;
                ms.Seek(0, SeekOrigin.Begin);

                var result = handshaker.HandshakeAsync(ms).Result;
                Assert.NotNull(result);
                Assert.False(result.IsWebSocketRequest);
                Assert.False(result.IsValidWebSocketRequest);
                Assert.True(result.IsValidHttpRequest);
                Assert.False(result.IsVersionSupported);
                Assert.Equal(new Uri("http://example.com"), result.Request.Headers.Origin);
                Assert.Equal("server.example.com", result.Request.Headers[HttpRequestHeader.Host]);
                Assert.Equal(@"/chat", result.Request.RequestUri.ToString());
                Assert.Equal(1, result.Request.Cookies.Count);
                var cookie = result.Request.Cookies["key"];
                Assert.Equal("key", cookie.Name);
                Assert.Equal(@"W9g/8FLW8RAFqSCWBvB9Ag==#5962c0ace89f4f780aa2a53febf2aae5", cookie.Value);
                Assert.NotNull(result.Request.LocalEndpoint);
                Assert.NotNull(result.Request.RemoteEndpoint);
            }
        }

        [Fact]
        public void SimpleHandshakeIgnoringFallback()
        {
            var options = new WebSocketListenerOptions();
            options.HttpFallback = this.fallback.Object;
            var handshaker = new WebSocketHandshaker(this.factories, options);

            using (var ms = new MemoryStream())
            {
                using (var sw = new StreamWriter(ms, Encoding.ASCII, 1024, true))
                {
                    sw.WriteLine(@"GET /chat HTTP/1.1");
                    sw.WriteLine(@"Host: server.example.com");
                    sw.WriteLine(@"Upgrade: websocket");
                    sw.WriteLine(@"Connection: Upgrade");
                    sw.WriteLine(@"Cookie: key=W9g/8FLW8RAFqSCWBvB9Ag==#5962c0ace89f4f780aa2a53febf2aae5;");
                    sw.WriteLine(@"Sec-WebSocket-Key: x3JJHMbDL1EzLkh9GBhXDw==");
                    sw.WriteLine(@"Sec-WebSocket-Version: 13");
                    sw.WriteLine(@"Origin: http://example.com");
                }

                var position = ms.Position;
                ms.Seek(0, SeekOrigin.Begin);

                var result = handshaker.HandshakeAsync(ms).Result;
                Assert.NotNull(result);
                Assert.True(result.IsWebSocketRequest);
                Assert.True(result.IsVersionSupported);
                Assert.Equal(new Uri("http://example.com"), result.Request.Headers.Origin);
                Assert.Equal("server.example.com", result.Request.Headers[HttpRequestHeader.Host]);
                Assert.Equal(@"/chat", result.Request.RequestUri.ToString());
                Assert.Equal(1, result.Request.Cookies.Count);
                var cookie = result.Request.Cookies["key"];
                Assert.Equal("key", cookie.Name);
                Assert.Equal(@"W9g/8FLW8RAFqSCWBvB9Ag==#5962c0ace89f4f780aa2a53febf2aae5", cookie.Value);
                Assert.NotNull(result.Request.LocalEndpoint);
                Assert.NotNull(result.Request.RemoteEndpoint);

                ms.Seek(position, SeekOrigin.Begin);

                var sb = new StringBuilder();
                sb.AppendLine(@"HTTP/1.1 101 Switching Protocols");
                sb.AppendLine(@"Upgrade: websocket");
                sb.AppendLine(@"Connection: Upgrade");
                sb.AppendLine(@"Sec-WebSocket-Accept: HSmrc0sMlYUkAGmm5OPpG2HaGWk=");
                sb.AppendLine();

                using (var sr = new StreamReader(ms))
                {
                    var s = sr.ReadToEnd();
                    Assert.Equal(sb.ToString(), s);
                }
            }
        }
    }
}
