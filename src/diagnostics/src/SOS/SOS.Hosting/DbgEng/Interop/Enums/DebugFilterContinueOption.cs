﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_FILTER_CONTINUE_OPTION : uint
    {
        GO_HANDLED = 0x00000000,
        GO_NOT_HANDLED = 0x00000001
    }
}