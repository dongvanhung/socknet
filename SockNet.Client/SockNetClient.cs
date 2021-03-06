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
using System.Net.Sockets;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.Medley.Pool;

namespace ArenaNet.SockNet.Client
{
    /// <summary>
    /// A client implementation that creates ClientSockNetChannels.
    /// </summary>
    public static class SockNetClient
    {
        public static ClientSockNetChannel Create(IPAddress address, int port, bool noDelay = ClientSockNetChannel.DefaultNoDelay, short ttl = ClientSockNetChannel.DefaultTtl, ObjectPool<byte[]> bufferPool = null)
        {
            return Create(new IPEndPoint(address, port), noDelay, ttl, bufferPool);
        }

        public static ClientSockNetChannel Create(IPEndPoint endpoint, bool noDelay = ClientSockNetChannel.DefaultNoDelay, short ttl = ClientSockNetChannel.DefaultTtl, ObjectPool<byte[]> bufferPool = null)
        {
            // TODO client track channels
            if (bufferPool == null)
            {
                bufferPool = SockNetChannelGlobals.GlobalBufferPool;
            }

            return new ClientSockNetChannel(endpoint, bufferPool).WithNoDelay(noDelay).WithTtl(ttl);
        }
    }
}
