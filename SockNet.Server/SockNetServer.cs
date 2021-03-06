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
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using ArenaNet.SockNet.Common;
using ArenaNet.Medley.Pool;

namespace ArenaNet.SockNet.Server
{
    /// <summary>
    /// A SockNet server.
    /// </summary>
    public static class SockNetServer
    {
        public static ServerSockNetChannel Create(IPAddress bindAddress, int bindPort, int backlog = ServerSockNetChannel.DefaultBacklog, ObjectPool<byte[]> bufferPool = null)
        {
            return Create(new IPEndPoint(bindAddress, bindPort), backlog, bufferPool);
        }

        public static ServerSockNetChannel Create(IPEndPoint bindEndpoint, int backlog = ServerSockNetChannel.DefaultBacklog, ObjectPool<byte[]> bufferPool = null)
        {
            // TODO possibly track?
            if (bufferPool == null)
            {
                bufferPool = SockNetChannelGlobals.GlobalBufferPool;
            }

            return new ServerSockNetChannel(bindEndpoint, SockNetChannelGlobals.GlobalBufferPool, backlog);
        }
    }
}
