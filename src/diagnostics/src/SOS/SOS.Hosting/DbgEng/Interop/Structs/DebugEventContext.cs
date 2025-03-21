﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DEBUG_EVENT_CONTEXT
    {
        public uint Size;
        public uint ProcessEngineId;
        public uint ThreadEngineId;
        public uint FrameEngineId;
    }
}