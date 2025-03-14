﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.ServiceModel.Syndication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.EditAndContinue;
using Microsoft.CodeAnalysis.Editor.Test;
using Microsoft.CodeAnalysis.LanguageServer.Handler;
using Microsoft.CodeAnalysis.LanguageServer.Handler.Diagnostics.Experimental;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Microsoft.CodeAnalysis.SolutionCrawler;
using Microsoft.CodeAnalysis.Test.Utilities;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Roslyn.Test.Utilities;
using Roslyn.Test.Utilities.TestGenerators;
using Roslyn.Utilities;
using Xunit;
using Xunit.Abstractions;
using LSP = Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Microsoft.CodeAnalysis.LanguageServer.UnitTests.Diagnostics
{
    public class PullDiagnosticTests : AbstractPullDiagnosticTestsBase
    {
        public PullDiagnosticTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
        {
        }
        #region Document Diagnostics

        [Theory, CombinatorialData]
        public async Task TestNoDocumentDiagnosticsForClosedFilesWithFSAOff(bool useVSDiagnostics)
        {
            var markup =
@"class A {";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            var results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, document.GetURI(), useVSDiagnostics);

            Assert.Empty(results);
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsForOpenFilesWithFSAOff(bool useVSDiagnostics)
        {
            var markup =
@"class A {";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);

            // Calling GetTextBuffer will effectively open the file.
            testLspServer.TestWorkspace.Documents.Single().GetTextBuffer();

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            await OpenDocumentAsync(testLspServer, document);

            var results = await RunGetDocumentPullDiagnosticsAsync(
                testLspServer, document.GetURI(), useVSDiagnostics);

            Assert.Equal("CS1513", results.Single().Diagnostics.Single().Code);
            Assert.NotNull(results.Single().Diagnostics.Single().CodeDescription!.Href);
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentTodoCommentsDiagnosticsForOpenFile(bool useVSDiagnostics)
        {
            var markup =
@"
// todo: goo
class A {
}";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);

            // Calling GetTextBuffer will effectively open the file.
            testLspServer.TestWorkspace.Documents.Single().GetTextBuffer();

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            await OpenDocumentAsync(testLspServer, document);

            var results = await RunGetDocumentPullDiagnosticsAsync(
                testLspServer, document.GetURI(), useVSDiagnostics);

            Assert.Equal("TODO", results.Single().Diagnostics.Single().Code);
            Assert.Equal("todo: goo", results.Single().Diagnostics.Single().Message);
        }

        [Theory, CombinatorialData]
        public async Task TestNoDocumentDiagnosticsForOpenFilesWithFSAOffIfInPushMode(bool useVSDiagnostics)
        {
            var markup =
@"class A {";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics, pullDiagnostics: false);

            // Calling GetTextBuffer will effectively open the file.
            testLspServer.TestWorkspace.Documents.Single().GetTextBuffer();

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            await OpenDocumentAsync(testLspServer, document);

            await Assert.ThrowsAsync<StreamJsonRpc.RemoteInvocationException>(async () => await RunGetDocumentPullDiagnosticsAsync(testLspServer, document.GetURI(), useVSDiagnostics));
        }

        [Theory, CombinatorialData]
        public async Task TestNoDocumentDiagnosticsForOpenFilesIfDefaultAndFeatureFlagOff(bool useVSDiagnostics)
        {
            var markup =
@"class A {";
            await using var testLspServer = await CreateTestLspServerAsync(markup,
                GetInitializationOptions(BackgroundAnalysisScope.OpenFiles, useVSDiagnostics, DiagnosticMode.Default));

            // Calling GetTextBuffer will effectively open the file.
            testLspServer.TestWorkspace.Documents.Single().GetTextBuffer();
            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();
            await OpenDocumentAsync(testLspServer, document);

            // Ensure we get no diagnostics when feature flag is off.
            testLspServer.TestWorkspace.GlobalOptions.SetGlobalOption(new OptionKey(DiagnosticOptionsStorage.LspPullDiagnosticsFeatureFlag), false);

            await Assert.ThrowsAsync<StreamJsonRpc.RemoteInvocationException>(async () => await RunGetDocumentPullDiagnosticsAsync(testLspServer, document.GetURI(), useVSDiagnostics));
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsForOpenFilesIfDefaultAndFeatureFlagOn(bool useVSDiagnostics)
        {
            var markup =
@"class A {";
            await using var testLspServer = await CreateTestLspServerAsync(markup,
                GetInitializationOptions(BackgroundAnalysisScope.OpenFiles, useVSDiagnostics, DiagnosticMode.Default));

            // Calling GetTextBuffer will effectively open the file.
            testLspServer.TestWorkspace.Documents.Single().GetTextBuffer();
            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();
            await OpenDocumentAsync(testLspServer, document);

            testLspServer.TestWorkspace.GlobalOptions.SetGlobalOption(new OptionKey(DiagnosticOptionsStorage.LspPullDiagnosticsFeatureFlag), true);

            var results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, document.GetURI(), useVSDiagnostics);
            Assert.Equal("CS1513", results.Single().Diagnostics.Single().Code);
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsForRemovedDocument(bool useVSDiagnostics)
        {
            var markup =
@"class A {";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);
            var workspace = testLspServer.TestWorkspace;

            // Calling GetTextBuffer will effectively open the file.
            workspace.Documents.Single().GetTextBuffer();

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            // Get the diagnostics for the solution containing the doc.
            var solution = document.Project.Solution;

            await OpenDocumentAsync(testLspServer, document);

            var results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, document.GetURI(), useVSDiagnostics).ConfigureAwait(false);

            Assert.Equal("CS1513", results.Single().Diagnostics.Single().Code);

            // Now remove the doc.
            workspace.OnDocumentRemoved(workspace.Documents.Single().Id);
            await CloseDocumentAsync(testLspServer, document);

            results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, document.GetURI(), useVSDiagnostics, results.Single().ResultId).ConfigureAwait(false);

            Assert.Equal(useVSDiagnostics ? null : Array.Empty<LSP.Diagnostic>(), results.Single().Diagnostics);
            Assert.Null(results.Single().ResultId);
        }

        [Theory, CombinatorialData]
        public async Task TestNoChangeIfDocumentDiagnosticsCalledTwice(bool useVSDiagnostics)
        {
            var markup =
@"class A {";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);

            // Calling GetTextBuffer will effectively open the file.
            testLspServer.TestWorkspace.Documents.Single().GetTextBuffer();

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            await OpenDocumentAsync(testLspServer, document);

            var results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, document.GetURI(), useVSDiagnostics);

            Assert.Equal("CS1513", results.Single().Diagnostics.Single().Code);

            var resultId = results.Single().ResultId;
            results = await RunGetDocumentPullDiagnosticsAsync(
                testLspServer, document.GetURI(), useVSDiagnostics, previousResultId: resultId);

            Assert.Null(results.Single().Diagnostics);
            Assert.Equal(resultId, results.Single().ResultId);
        }

        [Theory, CombinatorialData]
        [WorkItem(1481208, "https://devdiv.visualstudio.com/DevDiv/_workitems/edit/1481208")]
        public async Task TestDocumentDiagnosticsWhenEnCVersionChanges(bool useVSDiagnostics)
        {
            var markup =
@"class A {";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);

            // Calling GetTextBuffer will effectively open the file.
            testLspServer.TestWorkspace.Documents.Single().GetTextBuffer();

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            await OpenDocumentAsync(testLspServer, document);

            var results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, document.GetURI(), useVSDiagnostics);

            Assert.Equal("CS1513", results.Single().Diagnostics.Single().Code);

            var resultId = results.Single().ResultId;

            // Create a fake diagnostic to trigger a change in edit and continue, without a document change
            var encDiagnosticsSource = testLspServer.TestWorkspace.ExportProvider.GetExportedValue<EditAndContinueDiagnosticUpdateSource>();
            var rudeEdits = ImmutableArray.Create((document.Id, ImmutableArray.Create(new RudeEditDiagnostic(RudeEditKind.Update, default))));
            encDiagnosticsSource.ReportDiagnostics(testLspServer.TestWorkspace, testLspServer.TestWorkspace.CurrentSolution, ImmutableArray<DiagnosticData>.Empty, rudeEdits);

            results = await RunGetDocumentPullDiagnosticsAsync(
                testLspServer, document.GetURI(), useVSDiagnostics, previousResultId: resultId);

            // Result should be different, but diagnostics should be the same
            Assert.NotEqual(resultId, results.Single().ResultId);
            Assert.Equal("CS1513", results.Single().Diagnostics.Single().Code);
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsRemovedAfterErrorIsFixed(bool useVSDiagnostics)
        {
            var markup =
@"class A {";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);

            // Calling GetTextBuffer will effectively open the file.
            var buffer = testLspServer.TestWorkspace.Documents.Single().GetTextBuffer();

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            await OpenDocumentAsync(testLspServer, document);

            var results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, document.GetURI(), useVSDiagnostics);
            Assert.Equal("CS1513", results[0].Diagnostics.Single().Code);

            await InsertTextAsync(testLspServer, document, buffer.CurrentSnapshot.Length, "}");

            results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, document.GetURI(), useVSDiagnostics, results.Single().ResultId);
            Assert.Empty(results[0].Diagnostics);
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsRemainAfterErrorIsNotFixed(bool useVSDiagnostics)
        {
            var markup =
@"class A {";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);

            // Calling GetTextBuffer will effectively open the file.
            var buffer = testLspServer.TestWorkspace.Documents.Single().GetTextBuffer();

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            await OpenDocumentAsync(testLspServer, document);
            var results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, document.GetURI(), useVSDiagnostics);
            Assert.Equal("CS1513", results[0].Diagnostics.Single().Code);
            Assert.Equal(new Position { Line = 0, Character = 9 }, results[0].Diagnostics.Single().Range.Start);

            buffer.Insert(0, " ");
            await InsertTextAsync(testLspServer, document, position: 0, text: " ");

            results = await RunGetDocumentPullDiagnosticsAsync(
                testLspServer, document.GetURI(),
                useVSDiagnostics,
                previousResultId: results[0].ResultId);
            Assert.Equal("CS1513", results[0].Diagnostics.Single().Code);
            Assert.Equal(new Position { Line = 0, Character = 10 }, results[0].Diagnostics.Single().Range.Start);
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsAreNotMapped(bool useVSDiagnostics)
        {
            var markup =
@"#line 1 ""test.txt""
class A {";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);

            // Calling GetTextBuffer will effectively open the file.
            testLspServer.TestWorkspace.Documents.Single().GetTextBuffer();

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            await OpenDocumentAsync(testLspServer, document);

            var results = await RunGetDocumentPullDiagnosticsAsync(
                testLspServer, document.GetURI(), useVSDiagnostics);

            Assert.Equal("CS1513", results.Single().Diagnostics.Single().Code);
            Assert.Equal(1, results.Single().Diagnostics.Single().Range.Start.Line);
        }

        [Theory, CombinatorialData]
        public async Task TestStreamingDocumentDiagnostics(bool useVSDiagnostics)
        {
            var markup =
@"class A {";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);

            // Calling GetTextBuffer will effectively open the file.
            testLspServer.TestWorkspace.Documents.Single().GetTextBuffer();

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            await OpenDocumentAsync(testLspServer, document);

            var results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, document.GetURI(), useVSDiagnostics, useProgress: true);

            Assert.Equal("CS1513", results!.Single().Diagnostics.Single().Code);
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsForOpenFilesUsesActiveContext(bool useVSDiagnostics)
        {
            var documentText =
@"#if ONE
class A {
#endif
class B {";
            var workspaceXml =
@$"<Workspace>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj1"" PreprocessorSymbols=""ONE"">
        <Document FilePath=""C:\C.cs"">{documentText}</Document>
    </Project>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj2"">
        <Document IsLinkFile=""true"" LinkFilePath=""C:\C.cs"" LinkAssemblyName=""CSProj1"">{documentText}</Document>
    </Project>
</Workspace>";

            await using var testLspServer = await CreateTestWorkspaceFromXmlAsync(workspaceXml, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);

            var csproj1Document = testLspServer.GetCurrentSolution().Projects.Where(p => p.Name == "CSProj1").Single().Documents.First();
            var csproj2Document = testLspServer.GetCurrentSolution().Projects.Where(p => p.Name == "CSProj2").Single().Documents.First();

            // Open either of the documents via LSP, we're tracking the URI and text.
            await OpenDocumentAsync(testLspServer, csproj1Document);

            // This opens all documents in the workspace and ensures buffers are created.
            testLspServer.TestWorkspace.GetTestDocument(csproj1Document.Id).GetTextBuffer();

            // Set CSProj2 as the active context and get diagnostics.
            testLspServer.TestWorkspace.SetDocumentContext(csproj2Document.Id);
            var results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, csproj2Document.GetURI(), useVSDiagnostics);
            Assert.Equal("CS1513", results.Single().Diagnostics.Single().Code);
            if (useVSDiagnostics)
            {
                // Only VSDiagnostics will have the project.
                var vsDiagnostic = (LSP.VSDiagnostic)results.Single().Diagnostics.Single();
                Assert.Equal("CSProj2", vsDiagnostic.Projects.Single().ProjectName);
            }

            // Set CSProj1 as the active context and get diagnostics.
            testLspServer.TestWorkspace.SetDocumentContext(csproj1Document.Id);
            results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, csproj1Document.GetURI(), useVSDiagnostics);
            Assert.Equal(2, results.Single().Diagnostics!.Length);
            Assert.All(results.Single().Diagnostics, d => Assert.Equal("CS1513", d.Code));

            if (useVSDiagnostics)
            {
                Assert.All(results.Single().Diagnostics, d => Assert.Equal("CSProj1", ((VSDiagnostic)d).Projects.Single().ProjectName));
            }
        }

        [Fact]
        public async Task TestDocumentDiagnosticsHasSameIdentifierForLinkedFile()
        {
            var documentText =
@"class A { err }";
            var workspaceXml =
@$"<Workspace>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj1"" PreprocessorSymbols=""ONE"">
        <Document FilePath=""C:\C.cs"">{documentText}</Document>
    </Project>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj2"">
        <Document IsLinkFile=""true"" LinkFilePath=""C:\C.cs"" LinkAssemblyName=""CSProj1"">{documentText}</Document>
    </Project>
</Workspace>";

            await using var testLspServer = await CreateTestWorkspaceFromXmlAsync(workspaceXml, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics: false);

            var csproj1Document = testLspServer.GetCurrentSolution().Projects.Where(p => p.Name == "CSProj1").Single().Documents.First();
            var csproj2Document = testLspServer.GetCurrentSolution().Projects.Where(p => p.Name == "CSProj2").Single().Documents.First();

            // Open either of the documents via LSP, we're tracking the URI and text.
            await OpenDocumentAsync(testLspServer, csproj1Document);

            var csproj1Results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, GetVsTextDocumentIdentifier(csproj1Document), useVSDiagnostics: true);
            var csproj1Diagnostic = (VSDiagnostic)csproj1Results.Single().Diagnostics.Single();
            var csproj2Results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, GetVsTextDocumentIdentifier(csproj2Document), useVSDiagnostics: true);
            var csproj2Diagnostic = (VSDiagnostic)csproj2Results.Single().Diagnostics.Single();
            Assert.Equal(csproj1Diagnostic.Identifier, csproj2Diagnostic.Identifier);

            static VSTextDocumentIdentifier GetVsTextDocumentIdentifier(Document document)
            {
                var projectContext = new VSProjectContext
                {
                    Id = ProtocolConversions.ProjectIdToProjectContextId(document.Project.Id),
                    Label = document.Project.Name
                };
                return new VSTextDocumentIdentifier
                {
                    ProjectContext = projectContext,
                    Uri = document.GetURI(),
                };
            }
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsWithChangeInReferencedProject(bool useVSDiagnostics)
        {
            var markup1 =
@"namespace M
{
    class A : B { }
}";
            var markup2 =
@"namespace M
{
    public class {|caret:|} { }
}";

            var workspaceXml =
@$"<Workspace>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj1"">
        <Document FilePath=""C:\A.cs"">{markup1}</Document>
        <ProjectReference>CSProj2</ProjectReference>
    </Project>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj2"">
        <Document FilePath=""C:\B.cs"">{markup2}</Document>
    </Project>
</Workspace>";

            await using var testLspServer = await CreateTestWorkspaceFromXmlAsync(workspaceXml, BackgroundAnalysisScope.FullSolution, useVSDiagnostics).ConfigureAwait(false);
            var csproj1Document = testLspServer.GetCurrentSolution().Projects.Where(p => p.Name == "CSProj1").Single().Documents.First();
            var csproj2Document = testLspServer.GetCurrentSolution().Projects.Where(p => p.Name == "CSProj2").Single().Documents.First();

            await testLspServer.OpenDocumentAsync(csproj1Document.GetURI());
            await testLspServer.OpenDocumentAsync(csproj2Document.GetURI());

            // Verify we a diagnostic in A.cs since B does not exist.
            var results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, csproj1Document.GetURI(), useVSDiagnostics);
            Assert.Single(results);
            Assert.Equal("CS0246", results.Single().Diagnostics.Single().Code);

            // Insert B into B.cs and verify that the error in A.cs is now gone.
            var locationToReplace = testLspServer.GetLocations("caret").Single().Range;
            await testLspServer.ReplaceTextAsync(csproj2Document.GetURI(), (locationToReplace, "B"));
            var originalResultId = results.Single().ResultId;
            results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, csproj1Document.GetURI(), useVSDiagnostics, originalResultId);
            Assert.Single(results);
            Assert.Empty(results.Single().Diagnostics);
            Assert.NotEqual(originalResultId, results.Single().ResultId);
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsWithChangeInNotReferencedProject(bool useVSDiagnostics)
        {
            var markup1 =
@"namespace M
{
    class A : B { }
}";
            var markup2 =
@"namespace M
{
    public class {|caret:|} { }
}";

            var workspaceXml =
@$"<Workspace>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj1"">
        <Document FilePath=""C:\A.cs"">{markup1}</Document>
    </Project>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj2"">
        <Document FilePath=""C:\B.cs"">{markup2}</Document>
    </Project>
</Workspace>";

            await using var testLspServer = await CreateTestWorkspaceFromXmlAsync(workspaceXml, BackgroundAnalysisScope.FullSolution, useVSDiagnostics).ConfigureAwait(false);
            var csproj1Document = testLspServer.GetCurrentSolution().Projects.Where(p => p.Name == "CSProj1").Single().Documents.First();
            var csproj2Document = testLspServer.GetCurrentSolution().Projects.Where(p => p.Name == "CSProj2").Single().Documents.First();

            await testLspServer.OpenDocumentAsync(csproj1Document.GetURI());
            await testLspServer.OpenDocumentAsync(csproj2Document.GetURI());

            // Verify we get a diagnostic in A since the class B does not exist.
            var results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, csproj1Document.GetURI(), useVSDiagnostics);
            Assert.Single(results);
            Assert.Equal("CS0246", results.Single().Diagnostics.Single().Code);

            // Add B to CSProj2 and verify that we get an unchanged result (still has diagnostic) for A.cs
            // since CSProj1 does not reference CSProj2
            var locationToReplace = testLspServer.GetLocations("caret").Single().Range;
            await testLspServer.ReplaceTextAsync(csproj2Document.GetURI(), (locationToReplace, "B"));
            var originalResultId = results.Single().ResultId;
            results = await RunGetDocumentPullDiagnosticsAsync(testLspServer, csproj1Document.GetURI(), useVSDiagnostics, originalResultId);
            Assert.Single(results);
            Assert.Null(results.Single().Diagnostics);
            Assert.Equal(originalResultId, results.Single().ResultId);
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsFromRazorServer(bool useVSDiagnostics)
        {
            var markup =
@"class A {";

            // Turn off pull diagnostics by default, but send a request to the razor LSP server which is always pull.
            await using var testLspServer = await CreateTestLspServerAsync(markup,
                GetInitializationOptions(BackgroundAnalysisScope.OpenFiles, useVSDiagnostics, DiagnosticMode.Push, WellKnownLspServerKinds.RazorLspServer));

            // Calling GetTextBuffer will effectively open the file.
            testLspServer.TestWorkspace.Documents.Single().GetTextBuffer();

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            await OpenDocumentAsync(testLspServer, document);

            var results = await RunGetDocumentPullDiagnosticsAsync(
                testLspServer, document.GetURI(), useVSDiagnostics);

            // Assert that we have diagnostics even though the option is set to push.
            Assert.Equal("CS1513", results.Single().Diagnostics.Single().Code);
            Assert.NotNull(results.Single().Diagnostics.Single().CodeDescription!.Href);
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsFromLiveShareServer(bool useVSDiagnostics)
        {
            var markup =
@"class A {";

            // Turn off pull diagnostics by default, but send a request to the razor LSP server which is always pull.
            await using var testLspServer = await CreateTestLspServerAsync(markup,
                GetInitializationOptions(BackgroundAnalysisScope.OpenFiles, useVSDiagnostics, DiagnosticMode.Push, WellKnownLspServerKinds.LiveShareLspServer));

            // Calling GetTextBuffer will effectively open the file.
            testLspServer.TestWorkspace.Documents.Single().GetTextBuffer();

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            await OpenDocumentAsync(testLspServer, document);

            var results = await RunGetDocumentPullDiagnosticsAsync(
                testLspServer, document.GetURI(), useVSDiagnostics);

            // Assert that we have diagnostics even though the option is set to push.
            Assert.Equal("CS1513", results.Single().Diagnostics.Single().Code);
            Assert.NotNull(results.Single().Diagnostics.Single().CodeDescription!.Href);
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsIncludesSourceGeneratorDiagnostics(bool useVSDiagnostics)
        {
            var markup = "// Hello, World";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics, pullDiagnostics: true);

            // Calling GetTextBuffer will effectively open the file.
            testLspServer.TestWorkspace.Documents.Single().GetTextBuffer();

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            var generator = new DiagnosticProducingGenerator(context => Location.Create(context.Compilation.SyntaxTrees.Single(), new TextSpan(0, 10)));

            testLspServer.TestWorkspace.OnAnalyzerReferenceAdded(
                document.Project.Id,
                new TestGeneratorReference(generator));

            await OpenDocumentAsync(testLspServer, document);

            var results = await RunGetDocumentPullDiagnosticsAsync(
                testLspServer, document.GetURI(), useVSDiagnostics);

            var diagnostic = Assert.Single(results.Single().Diagnostics);
            Assert.Equal(DiagnosticProducingGenerator.Descriptor.Id, diagnostic.Code);
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsWithFadingOptionOn(bool useVSDiagnostics)
        {
            var markup =
@"
{|first:using System.Linq;
using System.Threading;|}
class A
{
}";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);
            var firstLocation = testLspServer.GetLocations("first").Single().Range;
            testLspServer.TestWorkspace.GlobalOptions.SetGlobalOption(new OptionKey(FadingOptions.FadeOutUnusedImports, LanguageNames.CSharp), true);

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            await OpenDocumentAsync(testLspServer, document);

            var results = await RunGetDocumentPullDiagnosticsAsync(
                testLspServer, document.GetURI(), useVSDiagnostics);

            // We should have an unnecessary diagnostic marking all the usings.
            Assert.True(results.Single().Diagnostics![0].Tags!.Contains(DiagnosticTag.Unnecessary));
            Assert.Equal(firstLocation, results.Single().Diagnostics![1].Range);

            // We should have a regular diagnostic marking all the usings that doesn't fade.
            Assert.False(results.Single().Diagnostics![1].Tags!.Contains(DiagnosticTag.Unnecessary));
            Assert.Equal(firstLocation, results.Single().Diagnostics![1].Range);
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsWithFadingOptionOff(bool useVSDiagnostics)
        {
            var markup =
@"
{|first:using System.Linq;
using System.Threading;|}
class A
{
}";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);
            var firstLocation = testLspServer.GetLocations("first").Single().Range;
            testLspServer.TestWorkspace.GlobalOptions.SetGlobalOption(new OptionKey(FadingOptions.FadeOutUnusedImports, LanguageNames.CSharp), false);

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            await OpenDocumentAsync(testLspServer, document);

            var results = await RunGetDocumentPullDiagnosticsAsync(
                testLspServer, document.GetURI(), useVSDiagnostics);

            Assert.All(results.Single().Diagnostics, d => Assert.False(d.Tags!.Contains(DiagnosticTag.Unnecessary)));
        }

        [Theory, CombinatorialData]
        public async Task TestDocumentDiagnosticsWithNotConfigurableFading(bool useVSDiagnostics)
        {
            var markup =
@"class A
{
    void M()
    {
        _ = {|line:{|open:(|}1 + 2 +|}
            3 + 4{|close:)|};
    }
}";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);
            var openLocation = testLspServer.GetLocations("open").Single().Range;
            var closeLocation = testLspServer.GetLocations("close").Single().Range;
            var lineLocation = testLspServer.GetLocations("line").Single().Range;

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            await OpenDocumentAsync(testLspServer, document);

            var results = await RunGetDocumentPullDiagnosticsAsync(
                testLspServer, document.GetURI(), useVSDiagnostics);

            // The first line should have a diagnostic on it that is not marked as unnecessary.
            Assert.False(results.Single().Diagnostics![0].Tags!.Contains(DiagnosticTag.Unnecessary));
            Assert.Equal(lineLocation, results.Single().Diagnostics![0].Range);

            // The open paren should have an unnecessary diagnostic.
            Assert.True(results.Single().Diagnostics![1].Tags!.Contains(DiagnosticTag.Unnecessary));
            Assert.Equal(openLocation, results.Single().Diagnostics![1].Range);

            // The close paren should have an unnecessary diagnostic.
            Assert.True(results.Single().Diagnostics![2].Tags!.Contains(DiagnosticTag.Unnecessary));
            Assert.Equal(closeLocation, results.Single().Diagnostics![2].Range);
        }

        #endregion

        #region Workspace Diagnostics

        [Theory, CombinatorialData]
        public async Task TestNoWorkspaceDiagnosticsForClosedFilesWithFSAOff(bool useVSDiagnostics)
        {
            var markup1 =
@"class A {";
            var markup2 = "";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                new[] { markup1, markup2 }, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);

            Assert.Empty(results);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceDiagnosticsForClosedFilesWithFSAOn(bool useVSDiagnostics)
        {
            var markup1 =
@"class A {";
            var markup2 = "";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                new[] { markup1, markup2 }, BackgroundAnalysisScope.FullSolution, useVSDiagnostics);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);

            Assert.Equal(3, results.Length);
            Assert.Equal("CS1513", results[0].Diagnostics.Single().Code);
            Assert.Empty(results[1].Diagnostics);
            Assert.Empty(results[2].Diagnostics);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceTodoForClosedFilesWithFSAOffAndTodoOff(bool useVSDiagnostics)
        {
            var markup1 =
@"
// todo: goo
class A {
}";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                new[] { markup1 }, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, includeTaskListItems: false);

            Assert.Equal(0, results.Length);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceTodoForClosedFilesWithFSAOffAndTodoOn(bool useVSDiagnostics)
        {
            var markup1 =
@"
// todo: goo
class A {
}";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                new[] { markup1 }, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, includeTaskListItems: true);

            Assert.Equal(1, results.Length);
            Assert.Equal("TODO", results[0].Diagnostics.Single().Code);
            Assert.Equal("todo: goo", results[0].Diagnostics.Single().Message);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceTodoForClosedFilesWithFSAOnAndTodoOff(bool useVSDiagnostics)
        {
            var markup1 =
@"
// todo: goo
class A {
}";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                new[] { markup1 }, BackgroundAnalysisScope.FullSolution, useVSDiagnostics);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, includeTaskListItems: false);

            Assert.Equal(2, results.Length);

            Assert.Equal("TODO", results[0].Diagnostics.Single().Code);
            Assert.Equal("todo: goo", results[0].Diagnostics.Single().Message);
            Assert.Empty(results[1].Diagnostics);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceTodoForClosedFilesWithFSAOnAndTodoOn(bool useVSDiagnostics)
        {
            var markup1 =
@"
// todo: goo
class A {
}";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                new[] { markup1 }, BackgroundAnalysisScope.FullSolution, useVSDiagnostics);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, includeTaskListItems: true);

            Assert.Equal(2, results.Length);

            Assert.Equal("TODO", results[0].Diagnostics.Single().Code);
            Assert.Equal("todo: goo", results[0].Diagnostics.Single().Message);
            Assert.Empty(results[1].Diagnostics);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceTodoAndDiagnosticForClosedFilesWithFSAOnAndTodoOn(bool useVSDiagnostics)
        {
            var markup1 =
@"
// todo: goo
class A {
";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                new[] { markup1 }, BackgroundAnalysisScope.FullSolution, useVSDiagnostics);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, includeTaskListItems: true);

            Assert.Equal(2, results.Length);

            Assert.Equal("TODO", results[0].Diagnostics![0].Code);
            Assert.Equal("todo: goo", results[0].Diagnostics![0].Message);
            Assert.Equal("CS1513", results[0].Diagnostics![1].Code);

            Assert.Empty(results[1].Diagnostics);
        }

        [Theory, CombinatorialData]
        public async Task TestNoWorkspaceDiagnosticsForClosedFilesWithFSAOffWithFileInProjectOpen(bool useVSDiagnostics)
        {
            var markup1 =
@"class A {";
            var markup2 = "";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                new[] { markup1, markup2 }, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics, pullDiagnostics: true);

            var firstDocument = testLspServer.GetCurrentSolution().Projects.Single().Documents.First();
            await OpenDocumentAsync(testLspServer, firstDocument);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);

            Assert.Empty(results);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceDiagnosticsIncludesSourceGeneratorDiagnosticsClosedFSAOn(bool useVSDiagnostics)
        {
            var markup = "// Hello, World";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.FullSolution, useVSDiagnostics, pullDiagnostics: true);

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.Single();

            var generator = new DiagnosticProducingGenerator(context => Location.Create(context.Compilation.SyntaxTrees.Single(), new TextSpan(0, 10)));

            testLspServer.TestWorkspace.OnAnalyzerReferenceAdded(
                document.Project.Id,
                new TestGeneratorReference(generator));

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);

            Assert.Equal(DiagnosticProducingGenerator.Descriptor.Id, results[0].Diagnostics.Single().Code);
            Assert.Empty(results[1].Diagnostics);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceDiagnosticsDoesNotIncludeSourceGeneratorDiagnosticsClosedFSAOffAndNoFilesOpen(bool useVSDiagnostics)
        {
            var markup = "// Hello, World";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(markup, BackgroundAnalysisScope.OpenFiles, useVSDiagnostics, pullDiagnostics: true);

            var generator = new DiagnosticProducingGenerator(
                context => Location.Create(
                    context.Compilation.SyntaxTrees.Single(),
                    new TextSpan(0, 10)));

            testLspServer.TestWorkspace.OnAnalyzerReferenceAdded(
                testLspServer.GetCurrentSolution().Projects.Single().Id,
                new TestGeneratorReference(generator));

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);
            Assert.Empty(results);
        }

        [Theory, CombinatorialData]
        public async Task TestNoWorkspaceDiagnosticsForClosedFilesWithFSAOnAndInPushMode(bool useVSDiagnostics)
        {
            var markup1 =
@"class A {";
            var markup2 = "";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                new[] { markup1, markup2 }, BackgroundAnalysisScope.FullSolution, useVSDiagnostics, pullDiagnostics: false);

            await Assert.ThrowsAsync<StreamJsonRpc.RemoteInvocationException>(async () => await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics));
        }

        [Theory, CombinatorialData]
        public async Task TestNoWorkspaceDiagnosticsForClosedFilesInProjectsWithIncorrectLanguage(bool useVSDiagnostics)
        {
            var csharpMarkup =
@"class A {";
            var typeScriptMarkup = "???";

            var workspaceXml =
@$"<Workspace>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj1"" FilePath=""C:\CSProj1.csproj"">
        <Document FilePath=""C:\C.cs"">{csharpMarkup}</Document>
    </Project>
    <Project Language=""TypeScript"" CommonReferences=""true"" AssemblyName=""TypeScriptProj"" FilePath=""C:\TypeScriptProj.csproj"">
        <Document FilePath=""C:\T.ts"">{typeScriptMarkup}</Document>
    </Project>
</Workspace>";

            await using var testLspServer = await CreateTestWorkspaceFromXmlAsync(workspaceXml, BackgroundAnalysisScope.FullSolution, useVSDiagnostics).ConfigureAwait(false);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);

            Assert.False(results.Any(r => r.TextDocument!.Uri.LocalPath.Contains(".ts")));
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceDiagnosticsForSourceGeneratedFiles(bool useVSDiagnostics)
        {
            var markup1 =
@"class A {";
            var markup2 = "";
            await using var testLspServer = await CreateTestLspServerAsync(
                markups: Array.Empty<string>(),
                GetInitializationOptions(BackgroundAnalysisScope.FullSolution, useVSDiagnostics, DiagnosticMode.Pull, sourceGeneratedMarkups: new[] { markup1, markup2 }));

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);

            // Project.GetSourceGeneratedDocumentsAsync may not return documents in a deterministic order, so we sort
            // the results here to ensure subsequent assertions are not dependent on the order of items provided by the
            // project.
            results = results.Sort((x, y) => x.Uri.ToString().CompareTo(y.Uri.ToString()));

            Assert.Equal(3, results.Length);
            // Since we sorted above by URI the first result is the project.
            Assert.Empty(results[0].Diagnostics);
            Assert.Equal("CS1513", results[1].Diagnostics.Single().Code);
            Assert.Empty(results[2].Diagnostics);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceDiagnosticsForRemovedDocument(bool useVSDiagnostics)
        {
            var markup1 =
@"class A {";
            var markup2 = "";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                new[] { markup1, markup2 }, BackgroundAnalysisScope.FullSolution, useVSDiagnostics);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);

            Assert.Equal(3, results.Length);
            Assert.Equal("CS1513", results[0].Diagnostics.Single().Code);
            Assert.Empty(results[1].Diagnostics);
            Assert.Empty(results[2].Diagnostics);

            testLspServer.TestWorkspace.OnDocumentRemoved(testLspServer.TestWorkspace.Documents.First().Id);

            var results2 = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, previousResults: CreateDiagnosticParamsFromPreviousReports(results));

            // First doc should show up as removed.
            Assert.Equal(3, results2.Length);
            Assert.Equal(useVSDiagnostics ? null : Array.Empty<LSP.Diagnostic>(), results2[0].Diagnostics);
            Assert.Null(results2[0].ResultId);

            // Second and third doc should be changed as the project has changed.
            Assert.Empty(results2[1].Diagnostics);
            Assert.NotEqual(results[1].ResultId, results2[1].ResultId);
            Assert.Empty(results2[2].Diagnostics);
            Assert.NotEqual(results[2].ResultId, results2[2].ResultId);
        }

        [Theory, CombinatorialData]
        public async Task TestNoChangeIfWorkspaceDiagnosticsCalledTwice(bool useVSDiagnostics)
        {
            var markup1 =
@"class A {";
            var markup2 = "";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                 new[] { markup1, markup2 }, BackgroundAnalysisScope.FullSolution, useVSDiagnostics);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);

            Assert.Equal(3, results.Length);
            Assert.Equal("CS1513", results[0].Diagnostics.Single().Code);
            Assert.Empty(results[1].Diagnostics);
            Assert.Empty(results[2].Diagnostics);

            var results2 = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, previousResults: CreateDiagnosticParamsFromPreviousReports(results));

            Assert.Equal(3, results2.Length);
            Assert.Null(results2[0].Diagnostics);
            Assert.Null(results2[1].Diagnostics);
            Assert.Null(results2[2].Diagnostics);

            Assert.Equal(results[0].ResultId, results2[0].ResultId);
            Assert.Equal(results[1].ResultId, results2[1].ResultId);
            Assert.Equal(results[2].ResultId, results2[2].ResultId);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceDiagnosticsRemovedAfterErrorIsFixed(bool useVSDiagnostics)
        {
            var markup1 =
@"class A {";
            var markup2 = "";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                 new[] { markup1, markup2 }, BackgroundAnalysisScope.FullSolution, useVSDiagnostics);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);

            Assert.Equal(3, results.Length);
            Assert.Equal("CS1513", results[0].Diagnostics.Single().Code);
            Assert.Empty(results[1].Diagnostics);
            Assert.Empty(results[2].Diagnostics);

            var buffer = testLspServer.TestWorkspace.Documents.First().GetTextBuffer();
            buffer.Insert(buffer.CurrentSnapshot.Length, "}");

            var results2 = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, previousResults: CreateDiagnosticParamsFromPreviousReports(results));

            Assert.Equal(3, results2.Length);
            Assert.Empty(results2[0].Diagnostics);
            // Project has changed, so we re-computed diagnostics as changes in the first file
            // may have changed results in the second.
            Assert.Empty(results2[1].Diagnostics);
            Assert.Empty(results2[2].Diagnostics);

            Assert.NotEqual(results[0].ResultId, results2[0].ResultId);
            Assert.NotEqual(results[1].ResultId, results2[1].ResultId);
            Assert.NotEqual(results[2].ResultId, results2[2].ResultId);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceDiagnosticsRemainAfterErrorIsNotFixed(bool useVSDiagnostics)
        {
            var markup1 =
@"class A {";
            var markup2 = "";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                 new[] { markup1, markup2 }, BackgroundAnalysisScope.FullSolution, useVSDiagnostics);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);

            Assert.Equal(3, results.Length);
            Assert.Equal("CS1513", results[0].Diagnostics.Single().Code);
            Assert.Equal(new Position { Line = 0, Character = 9 }, results[0].Diagnostics.Single().Range.Start);

            Assert.Empty(results[1].Diagnostics);
            Assert.Empty(results[2].Diagnostics);

            var buffer = testLspServer.TestWorkspace.Documents.First().GetTextBuffer();
            buffer.Insert(0, " ");

            var document = testLspServer.GetCurrentSolution().Projects.Single().Documents.First();
            var text = await document.GetTextAsync();

            // Hacky, but we need to close the document manually since editing the text-buffer will open it in the
            // test-workspace.
            testLspServer.TestWorkspace.OnDocumentClosed(
                document.Id, TextLoader.From(TextAndVersion.Create(text, VersionStamp.Create())));

            var results2 = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);

            Assert.Equal("CS1513", results2[0].Diagnostics.Single().Code);
            Assert.Equal(new Position { Line = 0, Character = 10 }, results2[0].Diagnostics.Single().Range.Start);

            Assert.Empty(results2[1].Diagnostics);
            Assert.NotEqual(results[1].ResultId, results2[1].ResultId);
            Assert.Empty(results2[2].Diagnostics);
            Assert.NotEqual(results[2].ResultId, results2[2].ResultId);
        }

        [Theory, CombinatorialData]
        public async Task TestStreamingWorkspaceDiagnostics(bool useVSDiagnostics)
        {
            var markup1 =
@"class A {";
            var markup2 = "";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                 new[] { markup1, markup2 }, BackgroundAnalysisScope.FullSolution, useVSDiagnostics);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);

            Assert.Equal(3, results.Length);
            Assert.Equal("CS1513", results[0].Diagnostics.Single().Code);
            Assert.Equal(new Position { Line = 0, Character = 9 }, results[0].Diagnostics.Single().Range.Start);

            results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, useProgress: true);

            Assert.Equal("CS1513", results[0].Diagnostics![0].Code);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceDiagnosticsAreNotMapped(bool useVSDiagnostics)
        {
            var markup1 =
@"#line 1 ""test.txt""
class A {";
            var markup2 = "";
            await using var testLspServer = await CreateTestWorkspaceWithDiagnosticsAsync(
                new[] { markup1, markup2 }, BackgroundAnalysisScope.FullSolution, useVSDiagnostics);

            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);
            Assert.Equal(3, results.Length);
            Assert.Equal(new Uri("C:/test1.cs"), results[0].TextDocument!.Uri);
            Assert.Equal("CS1513", results[0].Diagnostics.Single().Code);
            Assert.Equal(1, results[0].Diagnostics.Single().Range.Start.Line);
            Assert.Empty(results[1].Diagnostics);
            Assert.Empty(results[2].Diagnostics);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceDiagnosticsWithChangeInReferencedProject(bool useVSDiagnostics)
        {
            var markup1 =
@"namespace M
{
    class A : B { }
}";
            var markup2 =
@"namespace M
{
    public class {|caret:|} { }
}";

            var workspaceXml =
@$"<Workspace>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj1"" FilePath=""C:\CSProj1.csproj"">
        <Document FilePath=""C:\A.cs"">{markup1}</Document>
        <ProjectReference>CSProj2</ProjectReference>
    </Project>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj2"" FilePath=""C:\CSProj2.csproj"">
        <Document FilePath=""C:\B.cs"">{markup2}</Document>
    </Project>
</Workspace>";

            await using var testLspServer = await CreateTestWorkspaceFromXmlAsync(workspaceXml, BackgroundAnalysisScope.FullSolution, useVSDiagnostics).ConfigureAwait(false);
            var csproj2Document = testLspServer.GetCurrentSolution().Projects.Where(p => p.Name == "CSProj2").Single().Documents.First();

            // Verify we a diagnostic in A.cs since B does not exist
            // and a diagnostic in B.cs since it is missing the class name.
            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);
            AssertEx.NotNull(results);
            Assert.Equal(4, results.Length);
            Assert.Equal("CS0246", results[0].Diagnostics.Single().Code);
            Assert.Equal("CS1001", results[2].Diagnostics.Single().Code);

            // Insert B into B.cs via the workspace.
            var caretLocation = testLspServer.GetLocations("caret").First().Range;
            var csproj2DocumentText = await csproj2Document.GetTextAsync();
            var newCsProj2Document = csproj2Document.WithText(csproj2DocumentText.WithChanges(new TextChange(ProtocolConversions.RangeToTextSpan(caretLocation, csproj2DocumentText), "B")));
            await testLspServer.TestWorkspace.ChangeDocumentAsync(csproj2Document.Id, newCsProj2Document.Project.Solution);

            // Get updated workspace diagnostics for the change.
            var previousResultIds = CreateDiagnosticParamsFromPreviousReports(results);
            results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, previousResults: previousResultIds);
            AssertEx.NotNull(results);
            Assert.Equal(4, results.Length);

            // Verify diagnostics for A.cs are updated as the type B now exists.
            Assert.Empty(results[0].Diagnostics);
            Assert.NotEqual(previousResultIds[0].resultId, results[0].ResultId);

            // Verify diagnostics for B.cs are updated as the class definition is now correct.
            Assert.Empty(results[2].Diagnostics);
            Assert.NotEqual(previousResultIds[2].resultId, results[2].ResultId);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceDiagnosticsWithChangeInRecursiveReferencedProject(bool useVSDiagnostics)
        {
            var markup1 =
@"namespace M
{
    public class A
    {
    }
}";
            var markup2 =
@"namespace M
{
    public class B
    {
    }
}";
            var markup3 =
@"namespace M
{
    public class {|caret:|}
    {
    }
}";

            var workspaceXml =
@$"<Workspace>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj1"" FilePath=""C:\CSProj1.csproj"">
        <ProjectReference>CSProj2</ProjectReference>
        <Document FilePath=""C:\A.cs"">{markup1}</Document>
    </Project>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj2"" FilePath=""C:\CSProj2.csproj"">
        <ProjectReference>CSProj3</ProjectReference>
        <Document FilePath=""C:\B.cs"">{markup2}</Document>
    </Project>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj3"" FilePath=""C:\CSProj3.csproj"">
        <Document FilePath=""C:\C.cs"">{markup3}</Document>
    </Project>
</Workspace>";

            await using var testLspServer = await CreateTestWorkspaceFromXmlAsync(workspaceXml, BackgroundAnalysisScope.FullSolution, useVSDiagnostics).ConfigureAwait(false);
            var csproj3Document = testLspServer.GetCurrentSolution().Projects.Where(p => p.Name == "CSProj3").Single().Documents.First();

            // Verify we have a diagnostic in C.cs initially.
            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);
            AssertEx.NotNull(results);
            Assert.Equal(6, results.Length);
            Assert.Empty(results[0].Diagnostics);
            Assert.Empty(results[1].Diagnostics);
            Assert.Empty(results[2].Diagnostics);
            Assert.Empty(results[3].Diagnostics);
            Assert.Equal("CS1001", results[4].Diagnostics.Single().Code);
            Assert.Empty(results[5].Diagnostics);

            // Insert C into C.cs via the workspace.
            var caretLocation = testLspServer.GetLocations("caret").First().Range;
            var csproj3DocumentText = await csproj3Document.GetTextAsync().ConfigureAwait(false);
            var newCsProj3Document = csproj3Document.WithText(csproj3DocumentText.WithChanges(new TextChange(ProtocolConversions.RangeToTextSpan(caretLocation, csproj3DocumentText), "C")));
            await testLspServer.TestWorkspace.ChangeDocumentAsync(csproj3Document.Id, newCsProj3Document.Project.Solution).ConfigureAwait(false);

            // Get updated workspace diagnostics for the change.
            var previousResultIds = CreateDiagnosticParamsFromPreviousReports(results);
            results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, previousResults: previousResultIds).ConfigureAwait(false);
            AssertEx.NotNull(results);
            Assert.Equal(6, results.Length);

            // Verify that new diagnostics are returned for all files (even though the diagnostics for the first two files are the same)
            // since we re-calculate when transitive project dependencies change.
            Assert.Empty(results[0].Diagnostics);
            Assert.NotEqual(previousResultIds[0].resultId, results[0].ResultId);
            Assert.Empty(results[1].Diagnostics);
            Assert.NotEqual(previousResultIds[1].resultId, results[1].ResultId);

            Assert.Empty(results[2].Diagnostics);
            Assert.NotEqual(previousResultIds[2].resultId, results[2].ResultId);
            Assert.Empty(results[3].Diagnostics);
            Assert.NotEqual(previousResultIds[3].resultId, results[3].ResultId);

            Assert.Empty(results[4].Diagnostics);
            Assert.NotEqual(previousResultIds[4].resultId, results[4].ResultId);
            Assert.Empty(results[5].Diagnostics);
            Assert.NotEqual(previousResultIds[5].resultId, results[5].ResultId);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceDiagnosticsWithChangeInNotReferencedProject(bool useVSDiagnostics)
        {
            var markup1 =
@"namespace M
{
    class A : B { }
}";
            var markup2 =
@"namespace M
{
    public class {|caret:|} { }
}";

            var workspaceXml =
@$"<Workspace>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj1"" FilePath=""C:\CSProj1.csproj"">
        <Document FilePath=""C:\A.cs"">{markup1}</Document>
    </Project>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj2"" FilePath=""C:\CSProj2.csproj"">
        <Document FilePath=""C:\B.cs"">{markup2}</Document>
    </Project>
</Workspace>";

            await using var testLspServer = await CreateTestWorkspaceFromXmlAsync(workspaceXml, BackgroundAnalysisScope.FullSolution, useVSDiagnostics).ConfigureAwait(false);
            var csproj2Document = testLspServer.GetCurrentSolution().Projects.Where(p => p.Name == "CSProj2").Single().Documents.First();

            // Verify we a diagnostic in A.cs since B does not exist
            // and a diagnostic in B.cs since it is missing the class name.
            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);
            AssertEx.NotNull(results);
            Assert.Equal(4, results.Length);
            Assert.Equal("CS0246", results[0].Diagnostics.Single().Code);
            Assert.Empty(results[1].Diagnostics);
            Assert.Equal("CS1001", results[2].Diagnostics.Single().Code);
            Assert.Empty(results[3].Diagnostics);

            // Insert B into B.cs via the workspace.
            var caretLocation = testLspServer.GetLocations("caret").First().Range;
            var csproj2DocumentText = await csproj2Document.GetTextAsync();
            var newCsProj2Document = csproj2Document.WithText(csproj2DocumentText.WithChanges(new TextChange(ProtocolConversions.RangeToTextSpan(caretLocation, csproj2DocumentText), "B")));
            await testLspServer.TestWorkspace.ChangeDocumentAsync(csproj2Document.Id, newCsProj2Document.Project.Solution);

            // Get updated workspace diagnostics for the change.
            var previousResultIds = CreateDiagnosticParamsFromPreviousReports(results);
            results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, previousResultIds);
            AssertEx.NotNull(results);
            Assert.Equal(4, results.Length);

            // Verify the diagnostic result for A.cs is unchanged as A.cs does not reference CSProj2.
            Assert.Null(results[0].Diagnostics);
            Assert.Equal(previousResultIds[0].resultId, results[0].ResultId);
            Assert.Null(results[1].Diagnostics);
            Assert.Equal(previousResultIds[1].resultId, results[1].ResultId);

            // Verify that the diagnostics result for B.cs reflects the change we made to it.
            Assert.Empty(results[2].Diagnostics);
            Assert.NotEqual(previousResultIds[2].resultId, results[2].ResultId);
            Assert.Empty(results[3].Diagnostics);
            Assert.NotEqual(previousResultIds[3].resultId, results[3].ResultId);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceDiagnosticsWithDependentProjectReloadedAndChanged(bool useVSDiagnostics)
        {
            var markup1 =
@"namespace M
{
    class A : B { }
}";
            var markup2 =
@"namespace M
{
    public class {|caret:|} { }
}";

            var workspaceXml =
@$"<Workspace>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj1"" FilePath=""C:\CSProj1.csproj"">
        <Document FilePath=""C:\A.cs"">{markup1}</Document>
        <ProjectReference>CSProj2</ProjectReference>
    </Project>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj2"" FilePath=""C:\CSProj2.csproj"">
        <Document FilePath=""C:\B.cs"">{markup2}</Document>
    </Project>
</Workspace>";

            await using var testLspServer = await CreateTestWorkspaceFromXmlAsync(workspaceXml, BackgroundAnalysisScope.FullSolution, useVSDiagnostics).ConfigureAwait(false);
            var csproj2Document = testLspServer.GetCurrentSolution().Projects.Where(p => p.Name == "CSProj2").Single().Documents.First();

            // Verify we a diagnostic in A.cs since B does not exist
            // and a diagnostic in B.cs since it is missing the class name.
            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);
            AssertEx.NotNull(results);
            Assert.Equal(4, results.Length);
            Assert.Equal("CS0246", results[0].Diagnostics.Single().Code);
            Assert.Equal("CS1001", results[2].Diagnostics.Single().Code);

            // Change and reload the project via the workspace.
            var projectInfo = testLspServer.TestWorkspace.Projects.Where(p => p.AssemblyName == "CSProj2").Single().ToProjectInfo();
            projectInfo = projectInfo.WithCompilationOptions(projectInfo.CompilationOptions!.WithPlatform(Platform.X64));
            testLspServer.TestWorkspace.OnProjectReloaded(projectInfo);
            var operations = testLspServer.TestWorkspace.ExportProvider.GetExportedValue<AsynchronousOperationListenerProvider>();
            await operations.GetWaiter(FeatureAttribute.Workspace).ExpeditedWaitAsync();

            // Get updated workspace diagnostics for the change.
            var previousResultIds = CreateDiagnosticParamsFromPreviousReports(results);
            results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, previousResults: previousResultIds);

            AssertEx.NotNull(results);
            Assert.Equal(4, results.Length);

            // The diagnostics should have been recalculated for both projects as a referenced project changed.
            Assert.Equal("CS0246", results[0].Diagnostics.Single().Code);
            Assert.Equal("CS1001", results[2].Diagnostics.Single().Code);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceDiagnosticsWithDependentProjectReloadedUnChanged(bool useVSDiagnostics)
        {
            var markup1 =
@"namespace M
{
    class A : B { }
}";
            var markup2 =
@"namespace M
{
    public class {|caret:|} { }
}";

            var workspaceXml =
@$"<Workspace>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj1"" FilePath=""C:\CSProj1.csproj"">
        <Document FilePath=""C:\A.cs"">{markup1}</Document>
        <ProjectReference>CSProj2</ProjectReference>
    </Project>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj2"" FilePath=""C:\CSProj2.csproj"">
        <Document FilePath=""C:\B.cs"">{markup2}</Document>
    </Project>
</Workspace>";

            await using var testLspServer = await CreateTestWorkspaceFromXmlAsync(workspaceXml, BackgroundAnalysisScope.FullSolution, useVSDiagnostics).ConfigureAwait(false);
            var csproj2Document = testLspServer.GetCurrentSolution().Projects.Where(p => p.Name == "CSProj2").Single().Documents.First();

            // Verify we a diagnostic in A.cs since B does not exist
            // and a diagnostic in B.cs since it is missing the class name.
            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);
            AssertEx.NotNull(results);
            Assert.Equal(4, results.Length);
            Assert.Equal("CS0246", results[0].Diagnostics.Single().Code);
            Assert.Equal("CS1001", results[2].Diagnostics.Single().Code);

            // Reload the project via the workspace.
            var projectInfo = testLspServer.TestWorkspace.Projects.Where(p => p.AssemblyName == "CSProj2").Single().ToProjectInfo();
            testLspServer.TestWorkspace.OnProjectReloaded(projectInfo);
            var operations = testLspServer.TestWorkspace.ExportProvider.GetExportedValue<AsynchronousOperationListenerProvider>();
            await operations.GetWaiter(FeatureAttribute.Workspace).ExpeditedWaitAsync();

            // Get updated workspace diagnostics for the change.
            var previousResultIds = CreateDiagnosticParamsFromPreviousReports(results);
            results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, previousResults: previousResultIds);

            // Verify that since no actual changes have been made we report unchanged diagnostics.
            AssertEx.NotNull(results);
            Assert.Equal(4, results.Length);

            // Diagnostics should be unchanged as the referenced project was only unloaded / reloaded, but did not actually change.
            Assert.Null(results[0].Diagnostics);
            Assert.Equal(previousResultIds[0].resultId, results[0].ResultId);
            Assert.Null(results[2].Diagnostics);
            Assert.Equal(previousResultIds[2].resultId, results[2].ResultId);
        }

        [Theory, CombinatorialData]
        public async Task TestWorkspaceDiagnosticsOrderOfReferencedProjectsReloadedDoesNotMatter(bool useVSDiagnostics)
        {
            var markup1 =
@"namespace M
{
    class A : B { }
}";

            var workspaceXml =
@$"<Workspace>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj1"" FilePath=""C:\CSProj1.csproj"">
        <Document FilePath=""C:\A.cs"">{markup1}</Document>
        <ProjectReference>CSProj2</ProjectReference>
        <ProjectReference>CSProj3</ProjectReference>
    </Project>
    <Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj2"" FilePath=""C:\CSProj2.csproj"">
        <Document FilePath=""C:\B.cs""></Document>
    </Project>
<Project Language=""C#"" CommonReferences=""true"" AssemblyName=""CSProj3"" FilePath=""C:\CSProj3.csproj"">
        <Document FilePath=""C:\C.cs""></Document>
    </Project>
</Workspace>";

            await using var testLspServer = await CreateTestWorkspaceFromXmlAsync(workspaceXml, BackgroundAnalysisScope.FullSolution, useVSDiagnostics).ConfigureAwait(false);
            var csproj2Document = testLspServer.GetCurrentSolution().Projects.Where(p => p.Name == "CSProj2").Single().Documents.First();

            // Verify we a diagnostic in A.cs since B does not exist
            // and a diagnostic in B.cs since it is missing the class name.
            var results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics);
            AssertEx.NotNull(results);
            Assert.Equal(6, results.Length);
            Assert.Equal("CS0246", results[0].Diagnostics.Single().Code);

            // Reload the project via the workspace.
            var projectInfo = testLspServer.TestWorkspace.Projects.Where(p => p.AssemblyName == "CSProj2").Single().ToProjectInfo();
            testLspServer.TestWorkspace.OnProjectReloaded(projectInfo);
            var operations = testLspServer.TestWorkspace.ExportProvider.GetExportedValue<AsynchronousOperationListenerProvider>();
            await operations.GetWaiter(FeatureAttribute.Workspace).ExpeditedWaitAsync();

            // Get updated workspace diagnostics for the change.
            var previousResults = CreateDiagnosticParamsFromPreviousReports(results);
            var previousResultIds = previousResults.Select(param => param.resultId).ToImmutableArray();
            results = await RunGetWorkspacePullDiagnosticsAsync(testLspServer, useVSDiagnostics, previousResults: previousResults);

            // Verify that since no actual changes have been made we report unchanged diagnostics.
            AssertEx.NotNull(results);
            Assert.Equal(6, results.Length);

            // Diagnostics should be unchanged as a referenced project was unloaded and reloaded.  Order should not matter.
            Assert.Null(results[0].Diagnostics);
            Assert.All(results, result => Assert.Null(result.Diagnostics));
            Assert.All(results, result => Assert.True(previousResultIds.Contains(result.ResultId)));
        }

        #endregion
    }
}
