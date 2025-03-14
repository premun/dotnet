﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal partial class Binder
    {
        // An anonymous function can be of the form:
        // 
        // delegate { }              (missing parameter list)
        // delegate (int x) { }      (typed parameter list)
        // x => ...                  (type-inferred parameter list)
        // (x) => ...                (type-inferred parameter list)
        // (x, y) => ...             (type-inferred parameter list)
        // ( ) => ...                (typed parameter list)
        // (ref int x) => ...        (typed parameter list)
        // (int x, out int y) => ... (typed parameter list)
        //
        // and so on. We want to canonicalize these various ways of writing the signatures.
        // 
        // If we are in the first case then the name, modifier and type arrays are all null.
        // If we have a parameter list then the names array is non-null, but possibly empty.
        // If we have types then the types array is non-null, but possibly empty.
        // If we have no modifiers then the modifiers array is null; if we have any modifiers
        // then the modifiers array is non-null and not empty.

        private UnboundLambda AnalyzeAnonymousFunction(
            AnonymousFunctionExpressionSyntax syntax, BindingDiagnosticBag diagnostics)
        {
            Debug.Assert(syntax != null);
            Debug.Assert(syntax.IsAnonymousFunction());

            ImmutableArray<string> names = default;
            ImmutableArray<RefKind> refKinds = default;
            ImmutableArray<DeclarationScope> scopes = default;
            ImmutableArray<TypeWithAnnotations> types = default;
            RefKind returnRefKind = RefKind.None;
            TypeWithAnnotations returnType = default;
            ImmutableArray<SyntaxList<AttributeListSyntax>> parameterAttributes = default;

            var namesBuilder = ArrayBuilder<string>.GetInstance();
            ImmutableArray<bool> discardsOpt = default;
            SeparatedSyntaxList<ParameterSyntax>? parameterSyntaxList = null;
            bool hasSignature;

            if (syntax is LambdaExpressionSyntax lambdaSyntax)
            {
                checkAttributes(syntax, lambdaSyntax.AttributeLists, diagnostics);
            }

            switch (syntax.Kind())
            {
                default:
                case SyntaxKind.SimpleLambdaExpression:
                    // x => ...
                    hasSignature = true;
                    var simple = (SimpleLambdaExpressionSyntax)syntax;
                    namesBuilder.Add(simple.Parameter.Identifier.ValueText);
                    break;
                case SyntaxKind.ParenthesizedLambdaExpression:
                    // (T x, U y) => ...
                    // (x, y) => ...
                    hasSignature = true;
                    var paren = (ParenthesizedLambdaExpressionSyntax)syntax;
                    if (paren.ReturnType is { } returnTypeSyntax)
                    {
                        (returnRefKind, returnType) = BindExplicitLambdaReturnType(returnTypeSyntax, diagnostics);
                    }
                    parameterSyntaxList = paren.ParameterList.Parameters;
                    CheckParenthesizedLambdaParameters(parameterSyntaxList.Value, diagnostics);
                    break;
                case SyntaxKind.AnonymousMethodExpression:
                    // delegate (int x) { }
                    // delegate { }
                    var anon = (AnonymousMethodExpressionSyntax)syntax;
                    hasSignature = anon.ParameterList != null;
                    if (hasSignature)
                    {
                        parameterSyntaxList = anon.ParameterList!.Parameters;
                    }
                    break;
            }

            var isAsync = syntax.Modifiers.Any(SyntaxKind.AsyncKeyword);
            var isStatic = syntax.Modifiers.Any(SyntaxKind.StaticKeyword);

            if (parameterSyntaxList != null)
            {
                var hasExplicitlyTypedParameterList = true;

                var typesBuilder = ArrayBuilder<TypeWithAnnotations>.GetInstance();
                var refKindsBuilder = ArrayBuilder<RefKind>.GetInstance();
                var scopesBuilder = ArrayBuilder<DeclarationScope>.GetInstance();
                var attributesBuilder = ArrayBuilder<SyntaxList<AttributeListSyntax>>.GetInstance();

                // In the batch compiler case we probably should have given a syntax error if the
                // user did something like (int x, y)=>x+y -- but in the IDE scenario we might be in
                // this case. If we are, then rather than try to make partial deductions from the
                // typed formal parameters, simply bail out and treat it as an untyped lambda.
                //
                // However, we still want to give errors on every bad type in the list, even if one
                // is missing.

                int underscoresCount = 0;
                foreach (var p in parameterSyntaxList.Value)
                {
                    if (p.Identifier.IsUnderscoreToken())
                    {
                        underscoresCount++;
                    }

                    checkAttributes(syntax, p.AttributeLists, diagnostics);

                    if (p.Default != null)
                    {
                        Error(diagnostics, ErrorCode.ERR_DefaultValueNotAllowed, p.Default.EqualsToken);
                    }

                    if (p.IsArgList)
                    {
                        Error(diagnostics, ErrorCode.ERR_IllegalVarArgs, p);
                        continue;
                    }

                    var typeSyntax = p.Type;
                    TypeWithAnnotations type = default;
                    var refKind = RefKind.None;
                    var scope = DeclarationScope.Unscoped;

                    if (typeSyntax == null)
                    {
                        hasExplicitlyTypedParameterList = false;
                    }
                    else
                    {
                        type = BindType(typeSyntax, diagnostics);
                        ParameterHelpers.CheckParameterModifiers(p, diagnostics, parsingFunctionPointerParams: false, parsingLambdaParams: true);
                        refKind = ParameterHelpers.GetModifiers(p.Modifiers, out _, out _, out _, out scope);
                    }

                    namesBuilder.Add(p.Identifier.ValueText);
                    typesBuilder.Add(type);
                    refKindsBuilder.Add(refKind);
                    scopesBuilder.Add(scope);
                    attributesBuilder.Add(syntax.Kind() == SyntaxKind.ParenthesizedLambdaExpression ? p.AttributeLists : default);
                }

                discardsOpt = computeDiscards(parameterSyntaxList.Value, underscoresCount);

                if (hasExplicitlyTypedParameterList)
                {
                    types = typesBuilder.ToImmutable();
                }

                if (refKindsBuilder.Any(r => r != RefKind.None))
                {
                    refKinds = refKindsBuilder.ToImmutable();
                }

                if (scopesBuilder.Any(s => s != DeclarationScope.Unscoped))
                {
                    scopes = scopesBuilder.ToImmutable();
                }

                if (attributesBuilder.Any(a => a.Count > 0))
                {
                    parameterAttributes = attributesBuilder.ToImmutable();
                }

                typesBuilder.Free();
                scopesBuilder.Free();
                refKindsBuilder.Free();
                attributesBuilder.Free();
            }

            if (hasSignature)
            {
                names = namesBuilder.ToImmutable();
            }

            namesBuilder.Free();

            return UnboundLambda.Create(syntax, this, diagnostics.AccumulatesDependencies, returnRefKind, returnType, parameterAttributes, refKinds, scopes, types, names, discardsOpt, isAsync, isStatic);

            static ImmutableArray<bool> computeDiscards(SeparatedSyntaxList<ParameterSyntax> parameters, int underscoresCount)
            {
                if (underscoresCount <= 1)
                {
                    return default;
                }

                // When there are two or more underscores, they are discards
                var discardsBuilder = ArrayBuilder<bool>.GetInstance(parameters.Count);
                foreach (var p in parameters)
                {
                    discardsBuilder.Add(p.Identifier.IsUnderscoreToken());
                }

                return discardsBuilder.ToImmutableAndFree();
            }

            static void checkAttributes(AnonymousFunctionExpressionSyntax syntax, SyntaxList<AttributeListSyntax> attributeLists, BindingDiagnosticBag diagnostics)
            {
                foreach (var attributeList in attributeLists)
                {
                    if (syntax.Kind() == SyntaxKind.ParenthesizedLambdaExpression)
                    {
                        MessageID.IDS_FeatureLambdaAttributes.CheckFeatureAvailability(diagnostics, attributeList);
                    }
                    else
                    {
                        Error(diagnostics, syntax.Kind() == SyntaxKind.SimpleLambdaExpression ? ErrorCode.ERR_AttributesRequireParenthesizedLambdaExpression : ErrorCode.ERR_AttributesNotAllowed, attributeList);
                    }
                }
            }
        }

        private (RefKind, TypeWithAnnotations) BindExplicitLambdaReturnType(TypeSyntax syntax, BindingDiagnosticBag diagnostics)
        {
            MessageID.IDS_FeatureLambdaReturnType.CheckFeatureAvailability(diagnostics, syntax);

            Debug.Assert(syntax is not ScopedTypeSyntax);
            syntax = syntax.SkipScoped(out _).SkipRef(out RefKind refKind);
            if ((syntax as IdentifierNameSyntax)?.Identifier.ContextualKind() == SyntaxKind.VarKeyword)
            {
                diagnostics.Add(ErrorCode.ERR_LambdaExplicitReturnTypeVar, syntax.Location);
            }

            var returnType = BindType(syntax, diagnostics);
            var type = returnType.Type;

            if (returnType.IsStatic)
            {
                diagnostics.Add(ErrorFacts.GetStaticClassReturnCode(useWarning: false), syntax.Location, type);
            }
            else if (returnType.IsRestrictedType(ignoreSpanLikeTypes: true))
            {
                diagnostics.Add(ErrorCode.ERR_MethodReturnCantBeRefAny, syntax.Location, type);
            }

            return (refKind, returnType);
        }

        private static void CheckParenthesizedLambdaParameters(
            SeparatedSyntaxList<ParameterSyntax> parameterSyntaxList, BindingDiagnosticBag diagnostics)
        {
            if (parameterSyntaxList.Count > 0)
            {
                var hasTypes = parameterSyntaxList[0].Type != null;

                for (int i = 1, n = parameterSyntaxList.Count; i < n; i++)
                {
                    var parameter = parameterSyntaxList[i];

                    // Ignore parameters with missing names.  We'll have already reported an error
                    // about them in the parser.
                    if (!parameter.Identifier.IsMissing)
                    {
                        var thisParameterHasType = parameter.Type != null;
                        if (hasTypes != thisParameterHasType)
                        {
                            diagnostics.Add(ErrorCode.ERR_InconsistentLambdaParameterUsage,
                                parameter.Type?.GetLocation() ?? parameter.Identifier.GetLocation());
                        }
                    }
                }
            }
        }

        private UnboundLambda BindAnonymousFunction(AnonymousFunctionExpressionSyntax syntax, BindingDiagnosticBag diagnostics)
        {
            Debug.Assert(syntax != null);
            Debug.Assert(syntax.IsAnonymousFunction());

            var lambda = AnalyzeAnonymousFunction(syntax, diagnostics);
            var data = lambda.Data;
            if (data.HasExplicitlyTypedParameterList)
            {
                for (int i = 0; i < lambda.ParameterCount; i++)
                {
                    // UNDONE: Where do we report improper use of pointer types?
                    var type = data.ParameterTypeWithAnnotations(i).Type;
                    if (type is { })
                    {
                        if (type.IsStatic)
                        {
                            Error(diagnostics, ErrorFacts.GetStaticClassParameterCode(useWarning: false), syntax, type);
                        }
                        if (data.DeclaredScope(i) == DeclarationScope.ValueScoped && !type.IsErrorTypeOrRefLikeType())
                        {
                            diagnostics.Add(ErrorCode.ERR_ScopedRefAndRefStructOnly, data.ParameterLocation(i));
                        }
                    }
                }
            }

            // Parser will only have accepted static/async as allowed modifiers on this construct.
            // However, it may have accepted duplicates of those modifiers.  Ensure that any dupes
            // are reported now.
            ModifierUtils.ToDeclarationModifiers(syntax.Modifiers, diagnostics.DiagnosticBag ?? new DiagnosticBag());

            if (data.HasSignature)
            {
                var binder = new LocalScopeBinder(this);
                bool allowShadowingNames = binder.Compilation.IsFeatureEnabled(MessageID.IDS_FeatureNameShadowingInNestedFunctions);
                var pNames = PooledHashSet<string>.GetInstance();
                bool seenDiscard = false;

                for (int i = 0; i < lambda.ParameterCount; i++)
                {
                    var name = lambda.ParameterName(i);

                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    if (lambda.ParameterIsDiscard(i))
                    {
                        if (seenDiscard)
                        {
                            // We only report the diagnostic on the second and subsequent underscores
                            MessageID.IDS_FeatureLambdaDiscardParameters.CheckFeatureAvailability(
                                diagnostics,
                                binder.Compilation,
                                lambda.ParameterLocation(i));
                        }

                        seenDiscard = true;
                        continue;
                    }

                    if (!pNames.Add(name))
                    {
                        // The parameter name '{0}' is a duplicate
                        diagnostics.Add(ErrorCode.ERR_DuplicateParamName, lambda.ParameterLocation(i), name);
                    }
                    else if (!allowShadowingNames)
                    {
                        binder.ValidateLambdaParameterNameConflictsInScope(lambda.ParameterLocation(i), name, diagnostics);
                    }
                }
                pNames.Free();
            }

            return lambda;
        }
    }
}
