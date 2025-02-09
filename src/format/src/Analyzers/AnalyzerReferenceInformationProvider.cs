﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the MIT license.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.CodeAnalysis.Tools.Analyzers
{
    internal class AnalyzerReferenceInformationProvider : IAnalyzerInformationProvider
    {
        private static readonly Dictionary<string, Assembly> s_pathsToAssemblies = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Assembly> s_namesToAssemblies = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object s_guard = new();

        public ImmutableDictionary<ProjectId, AnalyzersAndFixers> GetAnalyzersAndFixers(
            Solution solution,
            FormatOptions formatOptions,
            ILogger logger)
        {
            return solution.Projects
                .ToImmutableDictionary(project => project.Id, GetAnalyzersAndFixers);
        }

        private AnalyzersAndFixers GetAnalyzersAndFixers(Project project)
        {
            var analyzerAssemblies = project.AnalyzerReferences
                .Select(reference => TryLoadAssemblyFrom(reference.FullPath, reference))
                .OfType<Assembly>()
                .ToImmutableArray();

            return AnalyzerFinderHelpers.LoadAnalyzersAndFixers(analyzerAssemblies);
        }

        private static Assembly? TryLoadAssemblyFrom(string? path, AnalyzerReference analyzerReference)
        {
            // Since we are not deploying these assemblies we need to ensure the files exist.
            if (path is null || !File.Exists(path))
            {
                return null;
            }

            lock (s_guard)
            {
                if (s_pathsToAssemblies.TryGetValue(path, out var cachedAssembly))
                {
                    return cachedAssembly;
                }

                try
                {
                    Assembly analyzerAssembly;
                    if (analyzerReference is AnalyzerFileReference analyzerFileReference)
                    {
                        // If we have access to the analyzer file reference, we can update our
                        // cache and return the assembly.
                        analyzerAssembly = analyzerFileReference.GetAssembly();
                        s_namesToAssemblies.TryAdd(analyzerAssembly.GetName().FullName, analyzerAssembly);
                    }
                    else
                    {
                        var loader = new DefaultAnalyzerAssemblyLoader();
                        analyzerAssembly = loader.LoadFromPath(path);
                    }

                    s_pathsToAssemblies.Add(path, analyzerAssembly);

                    return analyzerAssembly;
                }
                catch { }
            }

            return null;
        }

        public DiagnosticSeverity GetSeverity(FormatOptions formatOptions) => formatOptions.AnalyzerSeverity;
    }
}
