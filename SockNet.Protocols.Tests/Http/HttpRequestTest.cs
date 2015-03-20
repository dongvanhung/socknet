﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Protocols.Http
{
    [TestClass]
    public class HttpRequestTest
    {
        [TestMethod]
        public void TestSimpleClosed()
        {
            string sampleRequest = "GET / HTTP/1.0\r\nHost: localhost\r\n\r\n";

            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[500]; });
            ChunkedBuffer buffer = new ChunkedBuffer(pool);
            buffer.Write(Encoding.ASCII.GetBytes(sampleRequest), 0, Encoding.ASCII.GetByteCount(sampleRequest));

            HttpRequest request = new HttpRequest();
            Assert.IsTrue(request.Parse(buffer.Stream, true));

            Assert.AreEqual("GET", request.Action);
            Assert.AreEqual("/", request.Path);
            Assert.AreEqual("HTTP/1.0", request.Version);
            Assert.AreEqual("GET / HTTP/1.0", request.CommandLine);
            Assert.AreEqual("localhost", request.Header["Host"]);
            Assert.AreEqual(0, request.BodySize);
            Assert.AreEqual(0, request.Body.Position);

            MemoryStream stream = new MemoryStream();
            request.Write(stream, false);
            stream.Position = 0;
            
            using (StreamReader reader = new StreamReader(stream))
            {
                Assert.AreEqual(sampleRequest, reader.ReadToEnd());
            }
        }

        [TestMethod]
        public void TestContentLengthNotClosed()
        {
            string sampleContent = "<test><val>hello</val></test>";
            int sampleContentLength = Encoding.UTF8.GetByteCount(sampleContent);
            string sampleRequest = "POST / HTTP/1.0\r\nHost: localhost\r\nContent-Length: " + sampleContentLength + "\r\n\r\n" + sampleContent;

            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[500]; });
            ChunkedBuffer buffer = new ChunkedBuffer(pool);
            buffer.Write(Encoding.ASCII.GetBytes(sampleRequest), 0, Encoding.ASCII.GetByteCount(sampleRequest));

            HttpRequest request = new HttpRequest();
            Assert.IsTrue(request.Parse(buffer.Stream, false));

            Assert.AreEqual("POST", request.Action);
            Assert.AreEqual("/", request.Path);
            Assert.AreEqual("HTTP/1.0", request.Version);
            Assert.AreEqual("POST / HTTP/1.0", request.CommandLine);
            Assert.AreEqual("localhost", request.Header["Host"]);
            Assert.AreEqual(sampleContentLength, request.BodySize);
            Assert.AreEqual(sampleContentLength, request.Body.Position);

            MemoryStream stream = new MemoryStream();
            request.Write(stream, false);
            stream.Position = 0;

            using (StreamReader reader = new StreamReader(stream))
            {
                Assert.AreEqual(sampleRequest, reader.ReadToEnd());
            }
        }

        [TestMethod]
        public void TestContentLengthPartial()
        {
            string sampleContent = "<test><val>hello</val></test>";
            int sampleContentLength = Encoding.UTF8.GetByteCount(sampleContent);
            string sampleRequest = "POST / HTTP/1.0\r\nHost: localhost\r\nContent-Length: " + sampleContentLength + "\r\n\r\n" + sampleContent;

            int partialSize = sampleRequest.Length / 3;
            string sampleRequest1 = sampleRequest.Substring(0, partialSize);
            string sampleRequest2 = sampleRequest.Substring(partialSize, partialSize);
            string sampleRequest3 = sampleRequest.Substring(partialSize * 2, sampleRequest.Length - (partialSize * 2));

            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[500]; });
            ChunkedBuffer buffer = new ChunkedBuffer(pool);
            buffer.Write(Encoding.ASCII.GetBytes(sampleRequest1), 0, Encoding.ASCII.GetByteCount(sampleRequest1));

            HttpRequest request = new HttpRequest();
            Assert.IsFalse(request.Parse(buffer.Stream, false));

            buffer.Write(Encoding.ASCII.GetBytes(sampleRequest2), 0, Encoding.ASCII.GetByteCount(sampleRequest2));
            Assert.IsFalse(request.Parse(buffer.Stream, false));

            buffer.Write(Encoding.ASCII.GetBytes(sampleRequest3), 0, Encoding.ASCII.GetByteCount(sampleRequest3));
            Assert.IsTrue(request.Parse(buffer.Stream, false));

            Assert.AreEqual("POST", request.Action);
            Assert.AreEqual("/", request.Path);
            Assert.AreEqual("HTTP/1.0", request.Version);
            Assert.AreEqual("POST / HTTP/1.0", request.CommandLine);
            Assert.AreEqual("localhost", request.Header["Host"]);
            Assert.AreEqual(sampleContentLength, request.BodySize);
            Assert.AreEqual(sampleContentLength, request.Body.Position);

            MemoryStream stream = new MemoryStream();
            request.Write(stream, false);
            stream.Position = 0;

            using (StreamReader reader = new StreamReader(stream))
            {
                Assert.AreEqual(sampleRequest, reader.ReadToEnd());
            }
        }

        [TestMethod]
        public void TestContentLengthClosed()
        {
            string sampleContent = "<test><val>hello</val></test>";
            int sampleContentLength = Encoding.UTF8.GetByteCount(sampleContent);
            string sampleRequest = "POST / HTTP/1.0\r\nHost: localhost\r\nContent-Length: " + sampleContentLength + "\r\n\r\n" + sampleContent;

            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[500]; });
            ChunkedBuffer buffer = new ChunkedBuffer(pool);
            buffer.Write(Encoding.ASCII.GetBytes(sampleRequest), 0, Encoding.ASCII.GetByteCount(sampleRequest));

            HttpRequest request = new HttpRequest();
            Assert.IsTrue(request.Parse(buffer.Stream, true));

            Assert.AreEqual("POST", request.Action);
            Assert.AreEqual("/", request.Path);
            Assert.AreEqual("HTTP/1.0", request.Version);
            Assert.AreEqual("POST / HTTP/1.0", request.CommandLine);
            Assert.AreEqual("localhost", request.Header["Host"]);
            Assert.AreEqual(sampleContentLength, request.BodySize);
            Assert.AreEqual(sampleContentLength, request.Body.Position);

            MemoryStream stream = new MemoryStream();
            request.Write(stream, false);
            stream.Position = 0;

            using (StreamReader reader = new StreamReader(stream))
            {
                Assert.AreEqual(sampleRequest, reader.ReadToEnd());
            }
        }

        [TestMethod]
        public void TestChunked()
        {
            string sampleContent = "<test><val>hello</val></test>";

            int sampleContentLength = Encoding.UTF8.GetByteCount(sampleContent);

            string chunk1Content = "<test><val>";
            string chunk2Content = "hello</val>";
            string chunk3Content = "</test>";

            int chunk1ContentLength = Encoding.UTF8.GetByteCount(chunk1Content);
            int chunk2ContentLength = Encoding.UTF8.GetByteCount(chunk2Content);
            int chunk3ContentLength = Encoding.UTF8.GetByteCount(chunk3Content);

            string chunk1Request = "POST / HTTP/1.0\r\nHost: localhost\r\nTransfer-Encoding: chunked\r\n\r\n" + string.Format("{0:X}", chunk1ContentLength) + "\r\n" + chunk1Content + "\r\n";
            string chunk2Request = string.Format("{0:X}", chunk2ContentLength) + "\r\n" + chunk2Content + "\r\n";
            string chunk3Request = string.Format("{0:X}", chunk3ContentLength) + "\r\n" + chunk3Content + "\r\n";
            string chunk4Request = "0\r\n\r\n";

            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[500]; });

            ChunkedBuffer buffer1 = new ChunkedBuffer(pool);
            buffer1.Write(Encoding.ASCII.GetBytes(chunk1Request), 0, Encoding.ASCII.GetByteCount(chunk1Request));

            ChunkedBuffer buffer2 = new ChunkedBuffer(pool);
            buffer2.Write(Encoding.ASCII.GetBytes(chunk2Request), 0, Encoding.ASCII.GetByteCount(chunk2Request));
            
            ChunkedBuffer buffer3 = new ChunkedBuffer(pool);
            buffer3.Write(Encoding.ASCII.GetBytes(chunk3Request), 0, Encoding.ASCII.GetByteCount(chunk3Request));
            
            ChunkedBuffer buffer4 = new ChunkedBuffer(pool);
            buffer4.Write(Encoding.ASCII.GetBytes(chunk4Request), 0, Encoding.ASCII.GetByteCount(chunk4Request));

            HttpRequest request = new HttpRequest();
            Assert.IsFalse(request.IsChunked);
            Assert.IsFalse(request.Parse(buffer1.Stream, false));
            Assert.IsTrue(request.IsChunked);
            Assert.IsFalse(request.Parse(buffer2.Stream, false));
            Assert.IsTrue(request.IsChunked);
            Assert.IsFalse(request.Parse(buffer3.Stream, false));
            Assert.IsTrue(request.IsChunked);
            Assert.IsTrue(request.Parse(buffer4.Stream, false));
            Assert.IsTrue(request.IsChunked);

            Assert.AreEqual("POST", request.Action);
            Assert.AreEqual("/", request.Path);
            Assert.AreEqual("HTTP/1.0", request.Version);
            Assert.AreEqual("POST / HTTP/1.0", request.CommandLine);
            Assert.AreEqual("localhost", request.Header["Host"]);
            Assert.AreEqual(sampleContentLength, request.BodySize);
            Assert.AreEqual(sampleContentLength, request.Body.Position);

            MemoryStream stream = new MemoryStream();
            request.Write(stream, false);
            stream.Position = 0;

            using (StreamReader reader = new StreamReader(stream))
            {
                Assert.AreEqual("POST / HTTP/1.0\r\nHost: localhost\r\nTransfer-Encoding: chunked\r\n\r\n" + sampleContent, reader.ReadToEnd());
            }
        }
    }
}
