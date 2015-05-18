﻿/*
 * Copyright 2015 ArenaNet, LLC.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this 
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * 	 http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under 
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF 
 * ANY KIND, either express or implied. See the License for the specific language governing 
 * permissions and limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Net.Security;
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Net.Sockets;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.Medley.Pool;
using ArenaNet.Medley.Concurrent;
using ArenaNet.Medley.Collections.Concurrent;

namespace ArenaNet.SockNet.Common
{
    /// <summary>
    /// A simplification layer on top of System.Net.Sockets.
    /// </summary>
    /// <typeparam name="S">The type representing the state.</typeparam>
    public abstract class BaseSockNetChannel<S> : ISockNetChannel, IDisposable where S : struct, IComparable, IFormattable, IConvertible
    {
        // the pool for byte chunks
        private readonly ObjectPool<byte[]> bufferPool;
        public ObjectPool<byte[]> BufferPool { get { return bufferPool; } }

        // async read/write delegates
        private delegate int ReadDelegate(byte[] buffer, int offset, int count);
        private delegate void WriteDelegate(byte[] buffer, int offset, int count);

        // the receive stream that will be sent to incomingHandlers to handle data
        private readonly ChunkedBuffer chunkedBuffer;

        // the raw socket
        protected Socket Socket { get; private set; }

        // the installed modules
        protected ConcurrentHashSet<ISockNetChannelModule> modules = new ConcurrentHashSet<ISockNetChannelModule>(EqualityComparer<ISockNetChannelModule>.Default);

        // the current stream
        protected Stream stream;

        // channel attributes
        private ConcurrentHashMap<string, object> attributes = new ConcurrentHashMap<string, object>(StringComparer.OrdinalIgnoreCase, 1024, 256);

        /// <summary>
        /// Promise fulfiller for attachment.
        /// </summary>
        private PromiseFulfiller<ISockNetChannel> attachPromiseFulfiller = null;

        // data incomingHandlers for incomming and outgoing messages
        public SockNetChannelPipe Pipe { get; protected set; }

        /// <summary>
        /// The state that the send queue deals with.
        /// </summary>
        private class SendQueueState
        {
            public ChunkedBuffer buffer;
            public Promise<ISockNetChannel> promise;
        }

        /// <summary>
        /// The buffers that are queued for sends on this channel.
        /// </summary>
        private Queue<SendQueueState> sendQueue = new Queue<SendQueueState>();

        /// <summary>
        /// Whether the send queue is currently being processed.
        /// </summary>
        private bool isProcessingSendQueue = false;

        /// <summary>
        /// Whether this object is disposed;
        /// </summary>
        private bool disposed = false;

        /// <summary>
        /// Returns true if this client is connected and the connection is encrypted.
        /// </summary>
        public bool IsConnectionEncrypted
        {
            get
            {
                return this.stream != null && this.stream is SslStream && ((SslStream)this.stream).IsAuthenticated && ((SslStream)this.stream).IsEncrypted;
            }
        }

        /// <summary>
        /// Gets the remote IPEndPoint of this client.
        /// </summary>
        public IPEndPoint RemoteEndpoint
        {
            get
            {
                return IsActive ? (IPEndPoint)Socket.RemoteEndPoint : null;
            }
        }

        /// <summary>
        /// Gets the local IPEndPoint of this client.
        /// </summary>
        public IPEndPoint LocalEndpoint
        {
            get
            {
                return IsActive ? (IPEndPoint)Socket.LocalEndPoint : null;
            }
        }

        private string _id = Guid.NewGuid().ToString();

        /// <summary>
        /// Gener
        /// </summary>
        public string Id
        {
            get { return _id; }
        }

        public SockNetChannelProtocol Protocol
        {
            get;
            private set;
        }
        
        /// <summary>
        /// Gets the current state of the client.
        /// </summary>
        private readonly Array stateValues;
        public int state;
        public Enum State { get { return (Enum)stateValues.GetValue(state); } protected set { state = ((IConvertible)value).ToInt32(null); } }

        protected Semaphore streamWriteSemaphore = new Semaphore(1, 1);

        /// <summary>
        /// Creates a base socknet channel given a socket and a buffer pool.
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="bufferPool"></param>
        protected BaseSockNetChannel(Socket socket, ObjectPool<byte[]> bufferPool)
        {
            this.Socket = socket;

            if (socket.ProtocolType == ProtocolType.Tcp)
            {
                Protocol = SockNetChannelProtocol.Tcp;
            }
            else if (socket.ProtocolType == ProtocolType.Udp)
            {
                Protocol = SockNetChannelProtocol.Udp;
            }

            this.Pipe = new SockNetChannelPipe(this);
            
            this.bufferPool = bufferPool;
            this.chunkedBuffer = new ChunkedBuffer(bufferPool);

            this.stateValues = Enum.GetValues(typeof(S));
        }

        /// <summary>
        /// Disposes this channel.
        /// </summary>
        ~BaseSockNetChannel()
        {
            Dispose(false);
        }

        /// <summary>
        /// Adds a module into this channel.
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        public ISockNetChannel AddModule(ISockNetChannelModule module)
        {
            if (modules.TryAdd(module))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "Adding module: [{0}]", module);

                if (ShouldInstallModule(module))
                {
                    SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "Installing module: [{0}]", module);

                    module.Install(this);
                }
            }
            else
            {
                throw new Exception("Module [" + module + "] already installed.");
            }

            return this;
        }

        /// <summary>
        /// Removes a module from this channel.
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        public ISockNetChannel RemoveModule(ISockNetChannelModule module)
        {
            if (modules.Remove(module))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "Uninstalling module: [{0}]", module);

                module.Uninstall(this);
            }
            else
            {
                throw new Exception("Module [" + module + "] not installed.");
            }

            return this;
        }

        /// <summary>
        /// Returns true if the module is active in this channel;
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        public bool HasModule(ISockNetChannelModule module)
        {
            return modules.Contains(module);
        }

        /// <summary>
        /// Flag this channel as connecting.
        /// </summary>
        /// <returns></returns>
        protected bool TryFlaggingAs(S state, S previous)
        {
            int actualState = state.ToInt32(null);
            int actualPrevious = previous.ToInt32(null);

            return Interlocked.CompareExchange(ref this.state, actualState, actualPrevious) == actualPrevious;
        }

        /// <summary>
        /// Attaches as a non-ssl channel.
        /// </summary>
        protected Promise<ISockNetChannel> Attach()
        {
            stream = new NetworkStream(Socket, true);

            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "Reading data from [{0}]...", RemoteEndpoint);

            PooledObject<byte[]> buffer = bufferPool.Borrow();
            buffer.RefCount.Increment();

            stream.BeginRead(buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), new ReceiveState() { buffer = buffer, offset = 0, length = buffer.Value.Length });

            return (attachPromiseFulfiller = new Promise<ISockNetChannel>(this).CreateFulfiller()).Promise;
        }

        /// <summary>
        /// Attaches as a SSL channel with client auth.
        /// </summary>
        /// <param name="certificateValidationCallback"></param>
        protected Promise<ISockNetChannel> AttachAsSslClient(RemoteCertificateValidationCallback certificateValidationCallback)
        {
            stream = new NetworkStream(Socket, true);

            EnableClientSsl(certificateValidationCallback);

            return (attachPromiseFulfiller = new Promise<ISockNetChannel>().CreateFulfiller()).Promise;
        }

        /// <summary>
        /// Attaches as a SSL channel with client auth.
        /// </summary>
        /// <param name="certificateValidationCallback"></param>
        protected Promise<ISockNetChannel> AttachAsSslServer(RemoteCertificateValidationCallback certificateValidationCallback, X509Certificate serverCert)
        {
            stream = new NetworkStream(Socket, true);

            EnableServerSsl(certificateValidationCallback, serverCert);

            return (attachPromiseFulfiller = new Promise<ISockNetChannel>().CreateFulfiller()).Promise;
        }

        /// <summary>
        /// Enables SSL on this connection.
        /// </summary>
        /// <param name="certificateValidationCallback"></param>
        private void EnableClientSsl(RemoteCertificateValidationCallback certificateValidationCallback)
        {
            SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Authenticating SSL with [{0}]...", RemoteEndpoint);

            SslStream sslStream = new SslStream(stream, false, certificateValidationCallback);
            sslStream.BeginAuthenticateAsClient(RemoteEndpoint.Address.ToString(), new AsyncCallback(EnableClientSslCallback), sslStream);
        }

        /// <summary>
        /// A callback the gets invoked after SSL auth completes.
        /// </summary>
        /// <param name="result"></param>
        private void EnableClientSslCallback(IAsyncResult result)
        {
            SslStream sslStream = (SslStream)result.AsyncState;

            sslStream.EndAuthenticateAsClient(result);

            SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Authenticated SSL with [{0}].", RemoteEndpoint);
            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "[SSL] Reading data from [{0}]...", RemoteEndpoint);

            this.stream = sslStream;

            PooledObject<byte[]> buffer = bufferPool.Borrow();
            buffer.RefCount.Increment();

            stream.BeginRead(buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), new ReceiveState() { buffer = buffer, offset = 0, length = buffer.Value.Length });

            attachPromiseFulfiller.Fulfill(this);
        }

        /// <summary>
        /// Enables SSL on this connection.
        /// </summary>
        /// <param name="certificateValidationCallback"></param>
        private void EnableServerSsl(RemoteCertificateValidationCallback certificateValidationCallback, X509Certificate serverCert)
        {
            SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Authenticating SSL with [{0}]...", RemoteEndpoint);

            SslStream sslStream = new SslStream(stream, false, certificateValidationCallback);
            sslStream.BeginAuthenticateAsServer(serverCert, new AsyncCallback(EnableServerSslCallback), sslStream);
        }

        /// <summary>
        /// A callback the gets invoked after SSL auth completes.
        /// </summary>
        /// <param name="result"></param>
        private void EnableServerSslCallback(IAsyncResult result)
        {
            SslStream sslStream = (SslStream)result.AsyncState;

            sslStream.EndAuthenticateAsServer(result);

            SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Authenticated SSL with [{0}].", RemoteEndpoint);
            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "[SSL] Reading data from [{0}]...", RemoteEndpoint);

            this.stream = sslStream;

            PooledObject<byte[]> buffer = bufferPool.Borrow();
            buffer.RefCount.Increment();

            stream.BeginRead(buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), new ReceiveState() { buffer = buffer, offset = 0, length = buffer.Value.Length });

            attachPromiseFulfiller.Fulfill(this);
        }

        /// <summary>
        /// A state for async receive calls.
        /// </summary>
        public class ReceiveState
        {
            public PooledObject<byte[]> buffer;
            public int offset;
            public int length;
        }

        /// <summary>
        /// A callback that gets invoked when we have incoming data in the pipe.
        /// </summary>
        /// <param name="result"></param>
        private void ReceiveCallback(IAsyncResult result)
        {
            int count = 0;

            try
            {
                count = stream.EndRead(result);
            }
            catch (Exception)
            {
                // this means that the connection closed...
                Close();
                return;
            }

            ReceiveState state = (ReceiveState)result.AsyncState;

            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, (this.stream is SslStream ? "[SSL] " : "") + "Received [{0}] bytes from [{1}].", count, RemoteEndpoint);

            if (count > 0)
            {
                try
                {
                    chunkedBuffer.OfferChunk(state.buffer, state.offset, count);

                    while (true)
                    {
                        long startingPosition = chunkedBuffer.ReadPosition;

                        object obj = chunkedBuffer;

                        Pipe.HandleIncomingData(ref obj);

                        if (startingPosition == chunkedBuffer.ReadPosition)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, ex.Message, ex);
                }
                finally
                {
                    try
                    {
                        chunkedBuffer.Flush();

                        if (state.offset + count >= state.length)
                        {
                            if (state.buffer.RefCount.Decrement() < 1)
                            {
                                state.buffer.Return();
                            }

                            PooledObject<byte[]> buffer = bufferPool.Borrow();
                            buffer.RefCount.Increment();

                            if (IsActive)
                            {
                                SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, (this.stream is SslStream ? "[SSL] " : "") + "Reading data from [{0}]...", RemoteEndpoint);

                                stream.BeginRead(buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), new ReceiveState() { buffer = buffer, offset = 0, length = buffer.Value.Length });
                            }
                            else
                            {
                                Close();
                            }
                        }
                        else
                        {
                            if (IsActive)
                            {
                                SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, (this.stream is SslStream ? "[SSL] " : "") + "Reading data from [{0}]...", RemoteEndpoint);

                                stream.BeginRead(state.buffer.Value, state.offset + count, state.length - (state.offset + count), new AsyncCallback(ReceiveCallback), new ReceiveState() { buffer = state.buffer, offset = state.offset + count, length = state.length });
                            }
                            else
                            {
                                Close();
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, "Unable to begin reading.", e);

                        Close();
                    }
                }
            }
            else
            {
                Close();
            }
        }

        /// <summary>
        /// Sets the given message to the IPEndpoint.
        /// </summary>
        /// <param name="data"></param>
        public Promise<ISockNetChannel> Send(object data)
        {
            Promise<ISockNetChannel> promise = new Promise<ISockNetChannel>();

            object obj = data;

            try
            {
                Pipe.HandleOutgoingData(ref obj);
            }
            catch (Exception ex)
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, ex.Message);
            }
            finally
            {
                if (obj is byte[])
                {
                    byte[] rawSendableData = (byte[])obj;
                    
                    SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, (IsConnectionEncrypted ? "[SSL] " : "") + "Sending [{0}] bytes to [{1}]...", rawSendableData.Length, RemoteEndpoint);

                    EnqueueToSendQueueAndNotify(new ChunkedBuffer(bufferPool).OfferRaw(rawSendableData, 0, rawSendableData.Length), promise);
                }
                else if (obj is ChunkedBuffer)
                {
                    ChunkedBuffer sendableStream = ((ChunkedBuffer)obj);

                    SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, (IsConnectionEncrypted ? "[SSL] " : "") + "Sending [{0}] bytes to [{1}]...", sendableStream.AvailableBytesToRead, RemoteEndpoint);
                    
                    EnqueueToSendQueueAndNotify(sendableStream, promise);
                }
                else
                {
                    SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, "Unable to send object: {0}", obj);
                }
            }
            return promise;
        }

        /// <summary>
        /// Queues the given buffer to the send queue and notifies it.
        /// </summary>
        /// <param name="buffer"></param>
        private void EnqueueToSendQueueAndNotify(ChunkedBuffer buffer, Promise<ISockNetChannel> promise)
        {
            lock (sendQueue)
            {
                sendQueue.Enqueue(new SendQueueState()
                {
                    buffer = buffer,
                    promise = promise
                });
            }

            NotifySendQueue();
        }

        /// <summary>
        /// Notifies the send queue to process more data.
        /// </summary>
        private void NotifySendQueue()
        {
            lock (sendQueue)
            {
                if (isProcessingSendQueue)
                {
                    return;
                }

                if (sendQueue.Count > 0)
                {
                    try
                    {
                        SendQueueState nextState = sendQueue.Dequeue();

                        isProcessingSendQueue = true;

                        Promise<ChunkedBuffer> promise = nextState.buffer.DrainToStream(stream);
                        promise.State = nextState;
                        promise.OnFulfilled = QueueWriteCallback;
                    }
                    catch (Exception e)
                    {
                        SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, "Send data to the socket stream", e);

                        isProcessingSendQueue = false;
                    }
                }
                else
                {
                    isProcessingSendQueue = false;
                }
            }
        }

        /// <summary>
        /// Callback for the queued write.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="error"></param>
        /// <param name="promise"></param>
        private void QueueWriteCallback(ChunkedBuffer buffer, Exception error, Promise<ChunkedBuffer> promise)
        {
            SendQueueState state = (SendQueueState)promise.State;

            try
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, (stream is SslStream ? "[SSL] " : "") + "Sent data to [{0}].", RemoteEndpoint);

                isProcessingSendQueue = false;

                buffer.Close();
            }
            catch (Exception e)
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, "Unable to close the given buffer", e);
            }
            finally
            {
                try
                {
                    state.promise.CreateFulfiller().Fulfill(this);
                }
                finally
                {
                    NotifySendQueue();
                }
            }
        }

        /// <summary>
        /// Sets an attribute.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <param name="upsert"></param>
        /// <returns></returns>
        public bool SetAttribute<T>(string name, T value, bool upsert = true)
        {
            return attributes.Put(name, (object)value, upsert);
        }

        /// <summary>
        /// Removes an attribute.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <param name="upsert"></param>
        /// <returns></returns>
        public bool RemoveAttribute(string name)
        {
            return attributes.Remove(name);
        }

        /// <summary>
        /// Gets an attribute.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetAttribute<T>(string name, out T value)
        {
            object response;
            bool success = attributes.TryGetValue(name, out response);
            value = (T)response;

            return success;
        }

        /// <summary>
        /// Disposes this object.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected implementation of Dispose pattern.
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                stream.Dispose();
                chunkedBuffer.Dispose();
            }

            disposed = true;
        }

        /// <summary>
        /// Closes this channel.
        /// </summary>
        /// <returns></returns>
        public abstract Promise<ISockNetChannel> Close();

        /// <summary>
        /// Check to see if we should install this module when it gets added.
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        public abstract bool ShouldInstallModule(ISockNetChannelModule module);

        /// <summary>
        /// Returns true if this channel is active.
        /// </summary>
        public abstract bool IsActive { get; }
    }
}
