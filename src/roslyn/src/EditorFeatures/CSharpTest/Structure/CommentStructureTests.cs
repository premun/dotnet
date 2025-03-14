﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Structure;
using Microsoft.CodeAnalysis.Editor.UnitTests.Structure;
using Microsoft.CodeAnalysis.Structure;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Roslyn.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.Structure
{
    [Trait(Traits.Feature, Traits.Features.Outlining)]
    public class CommentTests : AbstractSyntaxStructureProviderTests
    {
        protected override string LanguageName => LanguageNames.CSharp;

        internal override async Task<ImmutableArray<BlockSpan>> GetBlockSpansWorkerAsync(Document document, BlockStructureOptions options, int position)
        {
            var root = await document.GetSyntaxRootAsync();
            var trivia = root.FindTrivia(position, findInsideTrivia: true);

            var token = trivia.Token;

            if (token.LeadingTrivia.Contains(trivia))
            {
                return CSharpStructureHelpers.CreateCommentBlockSpan(token.LeadingTrivia);
            }
            else if (token.TrailingTrivia.Contains(trivia))
            {
                return CSharpStructureHelpers.CreateCommentBlockSpan(token.TrailingTrivia);
            }

            throw Roslyn.Utilities.ExceptionUtilities.Unreachable();
        }

        [Fact]
        public async Task TestSimpleComment1()
        {
            const string code = @"
{|span:// Hello
// $$C#|}
class C
{
}
";

            await VerifyBlockSpansAsync(code,
                Region("span", "// Hello ...", autoCollapse: true));
        }

        [Fact]
        public async Task TestSimpleComment2()
        {
            const string code = @"
{|span:// Hello
//
// $$C#!|}
class C
{
}
";

            await VerifyBlockSpansAsync(code,
                Region("span", "// Hello ...", autoCollapse: true));
        }

        [Fact]
        public async Task TestSimpleComment3()
        {
            const string code = @"
{|span:// Hello

// $$C#!|}
class C
{
}
";

            await VerifyBlockSpansAsync(code,
                Region("span", "// Hello ...", autoCollapse: true));
        }

        [Fact]
        public async Task TestSingleLineCommentGroupFollowedByDocumentationComment()
        {
            const string code = @"
{|span:// Hello

// $$C#!|}
/// <summary></summary>
class C
{
}
";

            await VerifyBlockSpansAsync(code,
                Region("span", "// Hello ...", autoCollapse: true));
        }
    }
}
