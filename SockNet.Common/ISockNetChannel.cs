﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Common
{
    /// <summary>
    /// Globals for SockNet.
    /// </summary>
    public static class SockNetChannelGlobals
    {
        public const int DefaultBufferSize = 1024;

        public static readonly ObjectPool<byte[]> GlobalBufferPool = new ObjectPool<byte[]>(() => { return new byte[DefaultBufferSize]; });
    }

    /// <summary>
    /// A SockNet channel.
    /// </summary>
    public interface ISockNetChannel
    {
        /// <summary>
        /// The SockNetPipe.
        /// </summary>
        SockNetChannelPipe Pipe { get; }

        /// <summary>
        /// The buffer pool
        /// </summary>
        ObjectPool<byte[]> BufferPool { get; }

        /// <summary>
        /// The remote endpoint
        /// </summary>
        IPEndPoint RemoteEndpoint { get; }

        /// <summary>
        /// The local endpoint
        /// </summary>
        IPEndPoint LocalEndpoint { get; }

        /// <summary>
        /// Adds a module into this channel.
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        ISockNetChannel AddModule(ISockNetChannelModule module);

        /// <summary>
        /// Removes a module from this channel.
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        ISockNetChannel RemoveModule(ISockNetChannelModule module);

        /// <summary>
        /// Returns true if the connection  is encrypted.
        /// </summary>
        bool IsConnectionEncrypted { get; }

        /// <summary>
        /// Returns true if this channel is active.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Gets the current state
        /// </summary>
        Enum State { get; }

        /// <summary>
        /// Sends the given message to this channel
        /// </summary>
        /// <param name="data"></param>
        void Send(object data);

        /// <summary>
        /// Closes this channel.
        /// </summary>
        /// <returns></returns>
        Promise<ISockNetChannel> Close();
    }
}
