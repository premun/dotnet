﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.CSharp.CodeGen
{
    internal partial class CodeGenerator
    {
        private enum ArrayInitializerStyle
        {
            // Initialize every element
            Element,

            // Initialize all elements at once from a metadata blob
            Block,

            // Mixed case where there are some initializers that are constants and
            // there is enough of them so that it makes sense to use block initialization
            // followed by individual initialization of non-constant elements
            Mixed,
        }

        /// <summary>
        /// Entry point to the array initialization.
        /// Assumes that we have newly created array on the stack.
        /// 
        /// inits could be an array of values for a single dimensional array
        /// or an array (of array)+ of values for a multidimensional case
        /// 
        /// in either case it is expected that number of leaf values will match number 
        /// of elements in the array and nesting level should match the rank of the array.
        /// </summary>
        private void EmitArrayInitializers(ArrayTypeSymbol arrayType, BoundArrayInitialization inits)
        {
            var initExprs = inits.Initializers;
            var initializationStyle = ShouldEmitBlockInitializer(arrayType.ElementType, initExprs);

            if (initializationStyle == ArrayInitializerStyle.Element)
            {
                this.EmitElementInitializers(arrayType, initExprs, true);
            }
            else
            {
                ImmutableArray<byte> data = this.GetRawData(initExprs);
                _builder.EmitArrayBlockInitializer(data, inits.Syntax, _diagnostics);

                if (initializationStyle == ArrayInitializerStyle.Mixed)
                {
                    EmitElementInitializers(arrayType, initExprs, false);
                }
            }
        }

        private void EmitElementInitializers(ArrayTypeSymbol arrayType,
                                            ImmutableArray<BoundExpression> inits,
                                            bool includeConstants)
        {
            if (!IsMultidimensionalInitializer(inits))
            {
                EmitVectorElementInitializers(arrayType, inits, includeConstants);
            }
            else
            {
                EmitMultidimensionalElementInitializers(arrayType, inits, includeConstants);
            }
        }

        private void EmitVectorElementInitializers(ArrayTypeSymbol arrayType,
                    ImmutableArray<BoundExpression> inits,
                    bool includeConstants)
        {
            for (int i = 0; i < inits.Length; i++)
            {
                var init = inits[i];
                if (ShouldEmitInitExpression(includeConstants, init))
                {
                    _builder.EmitOpCode(ILOpCode.Dup);
                    _builder.EmitIntConstant(i);
                    EmitExpression(init, true);
                    EmitVectorElementStore(arrayType, init.Syntax);
                }
            }
        }

        // if element init is not a constant we have no choice - we need to emit it
        // if element is a default value - no need to emit initializer, arrays are created zero inited.
        // if element is a not a constant or includeConstants flag is set, return true
        private static bool ShouldEmitInitExpression(bool includeConstants, BoundExpression init)
        {
            if (init.IsDefaultValue())
            {
                return false;
            }

            return includeConstants || init.ConstantValue == null;
        }

        /// <summary>
        /// To handle array initialization of arbitrary rank it is convenient to 
        /// approach multidimensional initialization as a recursively nested.
        /// 
        /// ForAll{i, j, k} Init(i, j, k) ===> 
        /// ForAll{i} ForAll{j, k} Init(i, j, k) ===>
        /// ForAll{i} ForAll{j} ForAll{k} Init(i, j, k)
        /// 
        /// This structure is used for capturing initializers of a given index and 
        /// the index value itself.
        /// </summary>
        private readonly struct IndexDesc
        {
            public IndexDesc(int index, ImmutableArray<BoundExpression> initializers)
            {
                this.Index = index;
                this.Initializers = initializers;
            }

            public readonly int Index;
            public readonly ImmutableArray<BoundExpression> Initializers;
        }

        private void EmitMultidimensionalElementInitializers(ArrayTypeSymbol arrayType,
                                                            ImmutableArray<BoundExpression> inits,
                                                            bool includeConstants)
        {
            // Using a List for the stack instead of the framework Stack because IEnumerable from Stack is top to bottom.
            // This algorithm requires the IEnumerable to be from bottom to top. See extensions for List in CollectionExtensions.vb.

            var indices = new ArrayBuilder<IndexDesc>();

            // emit initializers for all values of the leftmost index.
            for (int i = 0; i < inits.Length; i++)
            {
                indices.Push(new IndexDesc(i, ((BoundArrayInitialization)inits[i]).Initializers));
                EmitAllElementInitializersRecursive(arrayType, indices, includeConstants);
            }

            Debug.Assert(!indices.Any());
        }

        /// <summary>
        /// Emits all initializers that match indices on the stack recursively.
        /// 
        /// Example: 
        ///  if array has [0..2, 0..3, 0..2] shape
        ///  and we have {1, 2} indices on the stack
        ///  initializers for 
        ///              [1, 2, 0]
        ///              [1, 2, 1]
        ///              [1, 2, 2]
        /// 
        ///  will be emitted and the top index will be pushed off the stack 
        ///  as at that point we would be completely done with emitting initializers 
        ///  corresponding to that index.
        /// </summary>
        private void EmitAllElementInitializersRecursive(ArrayTypeSymbol arrayType,
                                                         ArrayBuilder<IndexDesc> indices,
                                                         bool includeConstants)
        {
            var top = indices.Peek();
            var inits = top.Initializers;

            if (IsMultidimensionalInitializer(inits))
            {
                // emit initializers for the less significant indices recursively
                for (int i = 0; i < inits.Length; i++)
                {
                    indices.Push(new IndexDesc(i, ((BoundArrayInitialization)inits[i]).Initializers));
                    EmitAllElementInitializersRecursive(arrayType, indices, includeConstants);
                }
            }
            else
            {
                // leaf case
                for (int i = 0; i < inits.Length; i++)
                {
                    var init = inits[i];
                    if (ShouldEmitInitExpression(includeConstants, init))
                    {
                        // emit array ref
                        _builder.EmitOpCode(ILOpCode.Dup);

                        Debug.Assert(indices.Count == arrayType.Rank - 1);

                        // emit values of all indices that are in progress
                        foreach (var row in indices)
                        {
                            _builder.EmitIntConstant(row.Index);
                        }

                        // emit the leaf index
                        _builder.EmitIntConstant(i);

                        var initExpr = inits[i];
                        EmitExpression(initExpr, true);
                        EmitArrayElementStore(arrayType, init.Syntax);
                    }
                }
            }

            indices.Pop();
        }

        private static ConstantValue AsConstOrDefault(BoundExpression init)
        {
            ConstantValue initConstantValueOpt = init.ConstantValue;

            if (initConstantValueOpt != null)
            {
                return initConstantValueOpt;
            }

            TypeSymbol type = init.Type.EnumUnderlyingTypeOrSelf();
            return ConstantValue.Default(type.SpecialType);
        }

        /// <summary>
        /// Determine if enum arrays can be initialized using block initialization.
        /// </summary>
        /// <returns>True if it's safe to use block initialization for enum arrays.</returns>
        /// <remarks>
        /// In NetFx 4.0, block array initializers do not work on all combinations of {32/64 X Debug/Retail} when array elements are enums.
        /// This is fixed in 4.5 thus enabling block array initialization for a very common case.
        /// We look for the presence of <see cref="System.Runtime.GCLatencyMode.SustainedLowLatency"/> which was introduced in .NET Framework 4.5
        /// </remarks>
        private bool EnableEnumArrayBlockInitialization
        {
            get
            {
                var sustainedLowLatency = _module.Compilation.GetWellKnownTypeMember(WellKnownMember.System_Runtime_GCLatencyMode__SustainedLowLatency);
                return sustainedLowLatency != null && sustainedLowLatency.ContainingAssembly == _module.Compilation.Assembly.CorLibrary;
            }
        }

        private ArrayInitializerStyle ShouldEmitBlockInitializer(TypeSymbol elementType, ImmutableArray<BoundExpression> inits)
        {
            if (!_module.SupportsPrivateImplClass)
            {
                return ArrayInitializerStyle.Element;
            }

            if (elementType.IsEnumType())
            {
                if (!EnableEnumArrayBlockInitialization)
                {
                    return ArrayInitializerStyle.Element;
                }
                elementType = elementType.EnumUnderlyingTypeOrSelf();
            }

            if (elementType.SpecialType.IsBlittable())
            {
                if (_module.GetInitArrayHelper() == null)
                {
                    return ArrayInitializerStyle.Element;
                }

                int initCount = 0;
                int constCount = 0;
                InitializerCountRecursive(inits, ref initCount, ref constCount);

                if (initCount > 2)
                {
                    if (initCount == constCount)
                    {
                        return ArrayInitializerStyle.Block;
                    }

                    int thresholdCnt = Math.Max(3, (initCount / 3));

                    if (constCount >= thresholdCnt)
                    {
                        return ArrayInitializerStyle.Mixed;
                    }
                }
            }

            return ArrayInitializerStyle.Element;
        }

        /// <summary>
        /// Count of all nontrivial initializers and count of those that are constants.
        /// </summary>
        private void InitializerCountRecursive(ImmutableArray<BoundExpression> inits, ref int initCount, ref int constInits)
        {
            if (inits.Length == 0)
            {
                return;
            }

            foreach (var init in inits)
            {
                var asArrayInit = init as BoundArrayInitialization;

                if (asArrayInit != null)
                {
                    InitializerCountRecursive(asArrayInit.Initializers, ref initCount, ref constInits);
                }
                else
                {
                    // NOTE: default values do not need to be initialized. 
                    //       .NET arrays are always zero-inited.
                    if (!init.IsDefaultValue())
                    {
                        initCount += 1;
                        if (init.ConstantValue != null)
                        {
                            constInits += 1;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Produces a serialized blob of all constant initializers.
        /// Non-constant initializers are matched with a zero of corresponding size.
        /// </summary>
        private ImmutableArray<byte> GetRawData(ImmutableArray<BoundExpression> initializers)
        {
            // the initial size is a guess.
            // there is no point to be precise here as MemoryStream always has N + 1 storage 
            // and will need to be trimmed regardless
            var writer = new BlobBuilder(initializers.Length * 4);

            SerializeArrayRecursive(writer, initializers);

            return writer.ToImmutableArray();
        }

        private void SerializeArrayRecursive(BlobBuilder bw, ImmutableArray<BoundExpression> inits)
        {
            if (inits.Length != 0)
            {
                if (inits[0].Kind == BoundKind.ArrayInitialization)
                {
                    foreach (var init in inits)
                    {
                        SerializeArrayRecursive(bw, ((BoundArrayInitialization)init).Initializers);
                    }
                }
                else
                {
                    foreach (var init in inits)
                    {
                        AsConstOrDefault(init).Serialize(bw);
                    }
                }
            }
        }

        /// <summary>
        /// Check if it is a regular collection of expressions or there are nested initializers.
        /// </summary>
        private static bool IsMultidimensionalInitializer(ImmutableArray<BoundExpression> inits)
        {
            Debug.Assert(inits.All((init) => init.Kind != BoundKind.ArrayInitialization) ||
                         inits.All((init) => init.Kind == BoundKind.ArrayInitialization),
                         "all or none should be nested");

            return inits.Length != 0 && inits[0].Kind == BoundKind.ArrayInitialization;
        }

#nullable enable

        private bool TryEmitReadonlySpanAsBlobWrapper(NamedTypeSymbol spanType, BoundExpression wrappedExpression, bool used, bool inPlace, BoundExpression? start = null, BoundExpression? length = null)
        {
            Debug.Assert(start is null == length is null);

            ImmutableArray<byte> data = default;
            int elementCount = -1;

            if (!_module.SupportsPrivateImplClass)
            {
                return false;
            }

            var ctor = ((MethodSymbol?)this._module.Compilation.GetWellKnownTypeMember(WellKnownMember.System_ReadOnlySpan_T__ctor_Pointer));
            if (ctor == null)
            {
                return false;
            }

            if (wrappedExpression is BoundArrayCreation ac)
            {
                var arrayType = (ArrayTypeSymbol)ac.Type;
                TypeSymbol elementType = arrayType.ElementType.EnumUnderlyingTypeOrSelf();

                // NB: we cannot use this approach for element types larger than one byte
                //     the issue is that metadata stores blobs in little-endian format
                //     so anything that is larger than one byte will be incorrect on a big-endian machine
                //     With additional runtime support it might be possible, but not yet.
                //     See: https://github.com/dotnet/corefx/issues/26948 for more details
                if (elementType.SpecialType.SizeInBytes() != 1)
                {
                    return false;
                }

                elementCount = TryGetRawDataForArrayInit(ac.InitializerOpt, out data);
            }

            if (elementCount < 0)
            {
                return false;
            }

            if (start is null != length is null)
            {
                return false;
            }

            int lengthForConstructor;

            if (start is not null)
            {
                if (start.ConstantValue?.IsDefaultValue != true || start.ConstantValue.Discriminator != ConstantValueTypeDiscriminator.Int32)
                {
                    return false;
                }

                Debug.Assert(length is not null);

                if (length.ConstantValue?.Discriminator != ConstantValueTypeDiscriminator.Int32)
                {
                    return false;
                }

                lengthForConstructor = length.ConstantValue.Int32Value;

                if (lengthForConstructor > elementCount || lengthForConstructor < 0)
                {
                    return false;
                }
            }
            else
            {
                lengthForConstructor = elementCount;
            }

            if (!inPlace && !used)
            {
                // emitting a value that no one will see
                return true;
            }

            if (elementCount == 0)
            {
                if (inPlace)
                {
                    _builder.EmitOpCode(ILOpCode.Initobj);
                    EmitSymbolToken(spanType, wrappedExpression.Syntax);
                }
                else
                {
                    EmitDefaultValue(spanType, used, wrappedExpression.Syntax);
                }
            }
            else
            {
                if (IsPeVerifyCompatEnabled())
                {
                    return false;
                }

                _builder.EmitArrayBlockFieldRef(data, wrappedExpression.Syntax, _diagnostics);
                _builder.EmitIntConstant(lengthForConstructor);

                if (inPlace)
                {
                    // consumes target ref, data ptr and size, pushes nothing
                    _builder.EmitOpCode(ILOpCode.Call, stackAdjustment: -3);
                }
                else
                {
                    // consumes data ptr and size, pushes the instance
                    _builder.EmitOpCode(ILOpCode.Newobj, stackAdjustment: -1);
                }

                EmitSymbolToken(ctor.AsMember(spanType), wrappedExpression.Syntax, optArgList: null);
            }

            return true;
        }

#nullable disable

        /// <summary>
        ///  Returns a byte blob that matches serialized content of single array initializer.    
        ///  returns -1 if the initializer is null or not an array of literals
        /// </summary>
        private int TryGetRawDataForArrayInit(BoundArrayInitialization initializer, out ImmutableArray<byte> data)
        {
            data = default;

            if (initializer == null)
            {
                return -1;
            }

            var initializers = initializer.Initializers;
            if (initializers.Any(static init => init.ConstantValue == null))
            {
                return -1;
            }

            var elementCount = initializers.Length;
            if (elementCount == 0)
            {
                data = ImmutableArray<byte>.Empty;
                return 0;
            }

            var writer = new BlobBuilder(initializers.Length * 4);

            foreach (var init in initializer.Initializers)
            {
                init.ConstantValue.Serialize(writer);
            }

            data = writer.ToImmutableArray();
            return elementCount;
        }
    }
}
