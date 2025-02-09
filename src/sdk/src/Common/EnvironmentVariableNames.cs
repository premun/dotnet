﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.DotNet.Cli
{
    static class EnvironmentVariableNames
    {
        public static readonly string ALLOW_TARGETING_PACK_CACHING = "DOTNETSDK_ALLOW_TARGETING_PACK_CACHING";
        public static readonly string WORKLOAD_PACK_ROOTS = "DOTNETSDK_WORKLOAD_PACK_ROOTS";
        public static readonly string WORKLOAD_MANIFEST_ROOTS = "DOTNETSDK_WORKLOAD_MANIFEST_ROOTS";
        public static readonly string WORKLOAD_UPDATE_NOTIFY_DISABLE = "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE";
        public static readonly string WORKLOAD_UPDATE_NOTIFY_INTERVAL_HOURS = "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_INTERVAL_HOURS";
        public static readonly string WORKLOAD_DISABLE_PACK_GROUPS = "DOTNET_CLI_WORKLOAD_DISABLE_PACK_GROUPS";
        public static readonly string TELEMETRY_OPTOUT = "DOTNET_CLI_TELEMETRY_OPTOUT";
        public static readonly string ENABLE_PUBLISH_RELEASE_FOR_SOLUTIONS = "DOTNET_CLI_ENABLE_PUBLISH_RELEASE_FOR_SOLUTIONS";
        public static readonly string ENABLE_PACK_RELEASE_FOR_SOLUTIONS = "DOTNET_CLI_ENABLE_PACK_RELEASE_FOR_SOLUTIONS";
    }
}
