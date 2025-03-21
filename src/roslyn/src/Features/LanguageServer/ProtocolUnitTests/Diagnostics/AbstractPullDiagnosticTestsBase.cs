﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.RemoveUnnecessaryImports;
using Microsoft.CodeAnalysis.CSharp.RemoveUnnecessaryParentheses;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.LanguageServer.Handler;
using Microsoft.CodeAnalysis.LanguageServer.Handler.Diagnostics.Experimental;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.SolutionCrawler;
using Microsoft.CodeAnalysis.TaskList;
using Microsoft.CodeAnalysis.Test.Utilities;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Roslyn.Test.Utilities;
using Roslyn.Utilities;
using Xunit;
using Xunit.Abstractions;
using LSP = Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Microsoft.CodeAnalysis.LanguageServer.UnitTests.Diagnostics
{
    using DocumentDiagnosticPartialReport = SumType<FullDocumentDiagnosticReport, UnchangedDocumentDiagnosticReport, DocumentDiagnosticPartialResult>;

    public abstract class AbstractPullDiagnosticTestsBase : AbstractLanguageServerProtocolTests
    {
        protected AbstractPullDiagnosticTestsBase(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
        {
        }

        private protected override TestAnalyzerReferenceByLanguage CreateTestAnalyzersReference()
        {
            var builder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<DiagnosticAnalyzer>>();
            builder.Add(LanguageNames.CSharp, ImmutableArray.Create(
                DiagnosticExtensions.GetCompilerDiagnosticAnalyzer(LanguageNames.CSharp),
                new CSharpRemoveUnnecessaryImportsDiagnosticAnalyzer(),
                new CSharpRemoveUnnecessaryExpressionParenthesesDiagnosticAnalyzer()));
            builder.Add(LanguageNames.VisualBasic, ImmutableArray.Create(DiagnosticExtensions.GetCompilerDiagnosticAnalyzer(LanguageNames.VisualBasic)));
            builder.Add(InternalLanguageNames.TypeScript, ImmutableArray.Create<DiagnosticAnalyzer>(new MockTypescriptDiagnosticAnalyzer()));
            return new(builder.ToImmutableDictionary());
        }

        protected override TestComposition Composition => base.Composition.AddParts(typeof(MockTypescriptDiagnosticAnalyzer));

        private protected static async Task<ImmutableArray<TestDiagnosticResult>> RunGetWorkspacePullDiagnosticsAsync(
            TestLspServer testLspServer,
            bool useVSDiagnostics,
            ImmutableArray<(string resultId, TextDocumentIdentifier identifier)>? previousResults = null,
            bool useProgress = false,
            bool includeTaskListItems = false)
        {
            var optionService = testLspServer.TestWorkspace.GetService<IGlobalOptionService>();
            optionService.SetGlobalOption(new OptionKey(TaskListOptionsStorage.ComputeTaskListItemsForClosedFiles), includeTaskListItems);
            await testLspServer.WaitForDiagnosticsAsync();

            if (useVSDiagnostics)
            {
                BufferedProgress<VSInternalWorkspaceDiagnosticReport>? progress = useProgress ? BufferedProgress.Create<VSInternalWorkspaceDiagnosticReport>(null) : null;
                var diagnostics = await testLspServer.ExecuteRequestAsync<VSInternalWorkspaceDiagnosticsParams, VSInternalWorkspaceDiagnosticReport[]>(
                    VSInternalMethods.WorkspacePullDiagnosticName,
                    CreateWorkspaceDiagnosticParams(previousResults, progress),
                    CancellationToken.None).ConfigureAwait(false);

                if (useProgress)
                {
                    Assert.Null(diagnostics);
                    diagnostics = progress!.Value.GetValues();
                }

                AssertEx.NotNull(diagnostics);
                return diagnostics.Select(d => new TestDiagnosticResult(d.TextDocument!, d.ResultId!, d.Diagnostics)).ToImmutableArray();
            }
            else
            {
                BufferedProgress<WorkspaceDiagnosticReport>? progress = useProgress ? BufferedProgress.Create<WorkspaceDiagnosticReport>(null) : null;
                var returnedResult = await testLspServer.ExecuteRequestAsync<WorkspaceDiagnosticParams, WorkspaceDiagnosticReport?>(
                    ExperimentalMethods.WorkspaceDiagnostic,
                    CreateProposedWorkspaceDiagnosticParams(previousResults, progress),
                    CancellationToken.None).ConfigureAwait(false);

                if (useProgress)
                {
                    Assert.Null(returnedResult);
                    var progressValues = progress!.Value.GetValues();
                    Assert.NotNull(progressValues);
                    return progressValues.SelectMany(value => value.Items).Select(diagnostics => ConvertWorkspaceDiagnosticResult(diagnostics)).ToImmutableArray();

                }

                AssertEx.NotNull(returnedResult);
                return returnedResult.Items.Select(diagnostics => ConvertWorkspaceDiagnosticResult(diagnostics)).ToImmutableArray();
            }

            static WorkspaceDiagnosticParams CreateProposedWorkspaceDiagnosticParams(
                ImmutableArray<(string resultId, TextDocumentIdentifier identifier)>? previousResults = null,
                IProgress<WorkspaceDiagnosticReport[]>? progress = null)
            {
                var previousResultsLsp = previousResults?.Select(r => new PreviousResultId(r.identifier.Uri, r.resultId)).ToArray() ?? Array.Empty<PreviousResultId>();
                return new WorkspaceDiagnosticParams(identifier: null, previousResultsLsp, workDoneToken: null, partialResultToken: progress);
            }

            static TestDiagnosticResult ConvertWorkspaceDiagnosticResult(SumType<WorkspaceFullDocumentDiagnosticReport, WorkspaceUnchangedDocumentDiagnosticReport> workspaceReport)
            {
                if (workspaceReport.Value is WorkspaceFullDocumentDiagnosticReport fullReport)
                {
                    return new TestDiagnosticResult(new TextDocumentIdentifier { Uri = fullReport.Uri }, fullReport.ResultId!, fullReport.Items);
                }
                else
                {
                    var unchangedReport = (WorkspaceUnchangedDocumentDiagnosticReport)workspaceReport.Value!;
                    return new TestDiagnosticResult(new TextDocumentIdentifier { Uri = unchangedReport.Uri }, unchangedReport.ResultId!, null);
                }
            }
        }

        private protected static Task CloseDocumentAsync(TestLspServer testLspServer, Document document) => testLspServer.CloseDocumentAsync(document.GetURI());

        private protected static ImmutableArray<(string resultId, TextDocumentIdentifier identifier)> CreateDiagnosticParamsFromPreviousReports(ImmutableArray<TestDiagnosticResult> results)
        {

            return results.Select(r => (r.ResultId, r.TextDocument)).ToImmutableArray();
        }

        private protected static VSInternalDocumentDiagnosticsParams CreateDocumentDiagnosticParams(
            VSTextDocumentIdentifier vsTextDocumentIdentifier,
            string? previousResultId = null,
            IProgress<VSInternalDiagnosticReport[]>? progress = null)
        {
            return new VSInternalDocumentDiagnosticsParams
            {
                TextDocument = vsTextDocumentIdentifier,
                PreviousResultId = previousResultId,
                PartialResultToken = progress,
            };
        }

        private protected static VSInternalWorkspaceDiagnosticsParams CreateWorkspaceDiagnosticParams(
            ImmutableArray<(string resultId, TextDocumentIdentifier identifier)>? previousResults = null,
            IProgress<VSInternalWorkspaceDiagnosticReport[]>? progress = null)
        {
            return new VSInternalWorkspaceDiagnosticsParams
            {
                PreviousResults = previousResults?.Select(r => new VSInternalDiagnosticParams { PreviousResultId = r.resultId, TextDocument = r.identifier }).ToArray(),
                PartialResultToken = progress,
            };
        }

        private protected static async Task InsertTextAsync(
            TestLspServer testLspServer,
            Document document,
            int position,
            string text)
        {
            var sourceText = await document.GetTextAsync();
            var lineInfo = sourceText.Lines.GetLinePositionSpan(new TextSpan(position, 0));

            await testLspServer.InsertTextAsync(document.GetURI(), (lineInfo.Start.Line, lineInfo.Start.Character, text));
        }

        private protected static Task OpenDocumentAsync(TestLspServer testLspServer, Document document) => testLspServer.OpenDocumentAsync(document.GetURI());

        private protected static Task<ImmutableArray<TestDiagnosticResult>> RunGetDocumentPullDiagnosticsAsync(
            TestLspServer testLspServer,
            Uri uri,
            bool useVSDiagnostics,
            string? previousResultId = null,
            bool useProgress = false)
        {
            return RunGetDocumentPullDiagnosticsAsync(testLspServer, new VSTextDocumentIdentifier { Uri = uri }, useVSDiagnostics, previousResultId, useProgress);
        }

        private protected static async Task<ImmutableArray<TestDiagnosticResult>> RunGetDocumentPullDiagnosticsAsync(
            TestLspServer testLspServer,
            VSTextDocumentIdentifier vsTextDocumentIdentifier,
            bool useVSDiagnostics,
            string? previousResultId = null,
            bool useProgress = false)
        {
            await testLspServer.WaitForDiagnosticsAsync();

            if (useVSDiagnostics)
            {
                BufferedProgress<VSInternalDiagnosticReport>? progress = useProgress ? BufferedProgress.Create<VSInternalDiagnosticReport>(null) : null;
                var diagnostics = await testLspServer.ExecuteRequestAsync<VSInternalDocumentDiagnosticsParams, VSInternalDiagnosticReport[]>(
                    VSInternalMethods.DocumentPullDiagnosticName,
                    CreateDocumentDiagnosticParams(vsTextDocumentIdentifier, previousResultId, progress),
                    CancellationToken.None).ConfigureAwait(false);

                if (useProgress)
                {
                    Assert.Null(diagnostics);
                    diagnostics = progress!.Value.GetValues();
                }

                AssertEx.NotNull(diagnostics);
                return diagnostics.Select(d => new TestDiagnosticResult(vsTextDocumentIdentifier, d.ResultId!, d.Diagnostics)).ToImmutableArray();
            }
            else
            {
                BufferedProgress<DocumentDiagnosticPartialReport>? progress = useProgress ? BufferedProgress.Create<DocumentDiagnosticPartialReport>(null) : null;
                var diagnostics = await testLspServer.ExecuteRequestAsync<DocumentDiagnosticParams, SumType<FullDocumentDiagnosticReport, UnchangedDocumentDiagnosticReport>?>(
                    ExperimentalMethods.TextDocumentDiagnostic,
                    CreateProposedDocumentDiagnosticParams(vsTextDocumentIdentifier, previousResultId, progress),
                    CancellationToken.None).ConfigureAwait(false);
                if (useProgress)
                {
                    Assert.Null(diagnostics);
                    diagnostics = progress!.Value.GetValues().Single().First;
                }

                if (diagnostics == null)
                {
                    // The public LSP spec returns null when no diagnostics are available for a document wheres VS returns an empty array.
                    return ImmutableArray<TestDiagnosticResult>.Empty;
                }
                else if (diagnostics.Value.Value is UnchangedDocumentDiagnosticReport)
                {
                    // The public LSP spec returns different types when unchanged in contrast to VS which just returns null diagnostic array.
                    return ImmutableArray.Create(new TestDiagnosticResult(vsTextDocumentIdentifier, diagnostics.Value.Second.ResultId!, null));
                }
                else
                {
                    return ImmutableArray.Create(new TestDiagnosticResult(vsTextDocumentIdentifier, diagnostics.Value.First.ResultId!, diagnostics.Value.First.Items));
                }
            }

            static DocumentDiagnosticParams CreateProposedDocumentDiagnosticParams(
                VSTextDocumentIdentifier vsTextDocumentIdentifier,
                string? previousResultId = null,
                IProgress<DocumentDiagnosticPartialReport[]>? progress = null)
            {
                return new DocumentDiagnosticParams(vsTextDocumentIdentifier, null, previousResultId, progress, null);
            }
        }

        private protected Task<TestLspServer> CreateTestWorkspaceWithDiagnosticsAsync(string markup, BackgroundAnalysisScope scope, bool useVSDiagnostics, bool pullDiagnostics = true)
            => CreateTestLspServerAsync(markup, GetInitializationOptions(scope, useVSDiagnostics, pullDiagnostics ? DiagnosticMode.Pull : DiagnosticMode.Push));

        private protected Task<TestLspServer> CreateTestWorkspaceWithDiagnosticsAsync(string[] markups, BackgroundAnalysisScope scope, bool useVSDiagnostics, bool pullDiagnostics = true)
            => CreateTestLspServerAsync(markups, GetInitializationOptions(scope, useVSDiagnostics, pullDiagnostics ? DiagnosticMode.Pull : DiagnosticMode.Push));

        private protected Task<TestLspServer> CreateTestWorkspaceFromXmlAsync(string xmlMarkup, BackgroundAnalysisScope scope, bool useVSDiagnostics, bool pullDiagnostics = true)
            => CreateXmlTestLspServerAsync(xmlMarkup, initializationOptions: GetInitializationOptions(scope, useVSDiagnostics, pullDiagnostics ? DiagnosticMode.Pull : DiagnosticMode.Push));

        private protected static InitializationOptions GetInitializationOptions(
            BackgroundAnalysisScope scope,
            bool useVSDiagnostics,
            DiagnosticMode mode,
            WellKnownLspServerKinds serverKind = WellKnownLspServerKinds.AlwaysActiveVSLspServer,
            string[]? sourceGeneratedMarkups = null)
        {
            return new InitializationOptions
            {
                ClientCapabilities = useVSDiagnostics ? CapabilitiesWithVSExtensions : new LSP.ClientCapabilities(),
                OptionUpdater = (globalOptions) =>
                {
                    globalOptions.SetGlobalOption(new OptionKey(SolutionCrawlerOptionsStorage.BackgroundAnalysisScopeOption, LanguageNames.CSharp), scope);
                    globalOptions.SetGlobalOption(new OptionKey(SolutionCrawlerOptionsStorage.BackgroundAnalysisScopeOption, LanguageNames.VisualBasic), scope);
                    globalOptions.SetGlobalOption(new OptionKey(SolutionCrawlerOptionsStorage.BackgroundAnalysisScopeOption, InternalLanguageNames.TypeScript), scope);
                    globalOptions.SetGlobalOption(new OptionKey(InternalDiagnosticsOptions.NormalDiagnosticMode), mode);
                },
                ServerKind = serverKind,
                SourceGeneratedMarkups = sourceGeneratedMarkups ?? Array.Empty<string>()
            };
        }

        /// <summary>
        /// Helper type to store unified LSP diagnostic results.
        /// Diagnostics are null when unchanged.
        /// </summary>
        private protected record TestDiagnosticResult(TextDocumentIdentifier TextDocument, string ResultId, LSP.Diagnostic[]? Diagnostics)
        {
            public Uri Uri { get; } = TextDocument.Uri;
        }

        [DiagnosticAnalyzer(InternalLanguageNames.TypeScript), PartNotDiscoverable]
        private class MockTypescriptDiagnosticAnalyzer : DocumentDiagnosticAnalyzer
        {
            public static readonly DiagnosticDescriptor Descriptor = new DiagnosticDescriptor(
            "TS01", "TS error", "TS error", "Error", DiagnosticSeverity.Error, isEnabledByDefault: true);

            public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Descriptor);

            public override Task<ImmutableArray<Diagnostic>> AnalyzeSemanticsAsync(Document document, CancellationToken cancellationToken)
                => SpecializedTasks.EmptyImmutableArray<Diagnostic>();

            public override Task<ImmutableArray<Diagnostic>> AnalyzeSyntaxAsync(Document document, CancellationToken cancellationToken)
            {
                return Task.FromResult(ImmutableArray.Create(
                    Diagnostic.Create(Descriptor, Location.Create(document.FilePath!, default, default))));
            }
        }
    }
}
