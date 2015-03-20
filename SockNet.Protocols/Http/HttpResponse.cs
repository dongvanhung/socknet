﻿using System;
using System.Collections.Generic;
using System.Text;

namespace ArenaNet.SockNet.Protocols.Http
{
    /// <summary>
    /// Represents an HTTP response.
    /// </summary>
    public class HttpResponse : HttpPayload
    {
        public override string CommandLine
        {
            get {
                return Version + " " + Code + " " + Reason;
            }
        }

        public string Version
        {
            private set;
            get;
        }

        public string Code
        {
            private set;
            get;
        }

        public string Reason
        {
            private set;
            get;
        }

        /// <summary>
        /// Handles a status line.
        /// </summary>
        /// <param name="commandLine"></param>
        /// <returns></returns>
        protected override bool HandleCommandLine(string commandLine)
        {
            string[] split = commandLine.Split(new string[] { " " }, 3, StringSplitOptions.None);

            if (split.Length > 1)
            {
                Version = split[0].Trim();
                Code = split[1].Trim();

                if (split.Length > 2)
                {
                    Reason = split[2].Trim();
                }

                return true;
            } 
            else
            {
                return false;
            }
        }
    }
}