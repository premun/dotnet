﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Symbols;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Emit
{
    internal abstract class SymbolMatcher
    {
        public abstract Cci.ITypeReference? MapReference(Cci.ITypeReference reference);
        public abstract Cci.IDefinition? MapDefinition(Cci.IDefinition definition);
        public abstract Cci.INamespace? MapNamespace(Cci.INamespace @namespace);

        public ISymbolInternal? MapDefinitionOrNamespace(ISymbolInternal symbol)
        {
            var adapter = symbol.GetCciAdapter();
            return (adapter is Cci.IDefinition definition) ?
                MapDefinition(definition)?.GetInternalSymbol() :
                MapNamespace((Cci.INamespace)adapter)?.GetInternalSymbol();
        }

        public EmitBaseline MapBaselineToCompilation(
            EmitBaseline baseline,
            Compilation targetCompilation,
            CommonPEModuleBuilder targetModuleBuilder,
            ImmutableDictionary<ISymbolInternal, ImmutableArray<ISymbolInternal>> mappedSynthesizedMembers,
            ImmutableDictionary<ISymbolInternal, ImmutableArray<ISymbolInternal>> mappedDeletedMembers)
        {
            // Map all definitions to this compilation.
            var typesAdded = MapDefinitions(baseline.TypesAdded);
            var eventsAdded = MapDefinitions(baseline.EventsAdded);
            var fieldsAdded = MapDefinitions(baseline.FieldsAdded);
            var methodsAdded = MapDefinitions(baseline.MethodsAdded);
            var propertiesAdded = MapDefinitions(baseline.PropertiesAdded);
            var generationOrdinals = MapDefinitions(baseline.GenerationOrdinals);

            return baseline.With(
                targetCompilation,
                targetModuleBuilder,
                baseline.Ordinal,
                baseline.EncId,
                generationOrdinals,
                typesAdded,
                eventsAdded,
                fieldsAdded,
                methodsAdded,
                firstParamRowMap: baseline.FirstParamRowMap,
                propertiesAdded,
                eventMapAdded: baseline.EventMapAdded,
                propertyMapAdded: baseline.PropertyMapAdded,
                methodImplsAdded: baseline.MethodImplsAdded,
                customAttributesAdded: baseline.CustomAttributesAdded,
                tableEntriesAdded: baseline.TableEntriesAdded,
                blobStreamLengthAdded: baseline.BlobStreamLengthAdded,
                stringStreamLengthAdded: baseline.StringStreamLengthAdded,
                userStringStreamLengthAdded: baseline.UserStringStreamLengthAdded,
                guidStreamLengthAdded: baseline.GuidStreamLengthAdded,
                anonymousTypeMap: MapAnonymousTypes(baseline.AnonymousTypeMap),
                anonymousDelegates: MapAnonymousDelegates(baseline.AnonymousDelegates),
                anonymousDelegatesWithFixedTypes: MapAnonymousDelegatesWithFixedTypes(baseline.AnonymousDelegatesWithFixedTypes),
                synthesizedMembers: mappedSynthesizedMembers,
                deletedMembers: mappedDeletedMembers,
                addedOrChangedMethods: MapAddedOrChangedMethods(baseline.AddedOrChangedMethods),
                debugInformationProvider: baseline.DebugInformationProvider,
                localSignatureProvider: baseline.LocalSignatureProvider);
        }

        private IReadOnlyDictionary<K, V> MapDefinitions<K, V>(IReadOnlyDictionary<K, V> items)
            where K : class, Cci.IDefinition
        {
            var result = new Dictionary<K, V>(Cci.SymbolEquivalentEqualityComparer.Instance);
            foreach (var pair in items)
            {
                var key = (K?)MapDefinition(pair.Key);

                // Result may be null if the definition was deleted, or if the definition
                // was synthesized (e.g.: an iterator type) and the method that generated
                // the synthesized definition was unchanged and not recompiled.
                if (key != null)
                {
                    result.Add(key, pair.Value);
                }
            }

            return result;
        }

        private IReadOnlyDictionary<int, AddedOrChangedMethodInfo> MapAddedOrChangedMethods(IReadOnlyDictionary<int, AddedOrChangedMethodInfo> addedOrChangedMethods)
        {
            var result = new Dictionary<int, AddedOrChangedMethodInfo>();

            foreach (var pair in addedOrChangedMethods)
            {
                result.Add(pair.Key, pair.Value.MapTypes(this));
            }

            return result;
        }

        private IReadOnlyDictionary<AnonymousTypeKey, AnonymousTypeValue> MapAnonymousTypes(IReadOnlyDictionary<AnonymousTypeKey, AnonymousTypeValue> anonymousTypeMap)
        {
            var result = new Dictionary<AnonymousTypeKey, AnonymousTypeValue>();

            foreach (var (key, value) in anonymousTypeMap)
            {
                var type = (Cci.ITypeDefinition?)MapDefinition(value.Type);
                RoslynDebug.Assert(type != null);
                result.Add(key, new AnonymousTypeValue(value.Name, value.UniqueIndex, type));
            }

            return result;
        }

        private IReadOnlyDictionary<SynthesizedDelegateKey, SynthesizedDelegateValue> MapAnonymousDelegates(IReadOnlyDictionary<SynthesizedDelegateKey, SynthesizedDelegateValue> anonymousDelegates)
        {
            var result = new Dictionary<SynthesizedDelegateKey, SynthesizedDelegateValue>();

            foreach (var (key, value) in anonymousDelegates)
            {
                var delegateTypeDef = (Cci.ITypeDefinition?)MapDefinition(value.Delegate);
                RoslynDebug.Assert(delegateTypeDef != null);
                result.Add(key, new SynthesizedDelegateValue(delegateTypeDef));
            }

            return result;
        }

        private IReadOnlyDictionary<string, AnonymousTypeValue> MapAnonymousDelegatesWithFixedTypes(IReadOnlyDictionary<string, AnonymousTypeValue> anonymousDelegates)
        {
            var result = new Dictionary<string, AnonymousTypeValue>();

            foreach (var (key, value) in anonymousDelegates)
            {
                var type = (Cci.ITypeDefinition?)MapDefinition(value.Type);
                RoslynDebug.Assert(type != null);
                result.Add(key, new AnonymousTypeValue(value.Name, value.UniqueIndex, type));
            }

            return result;
        }

        /// <summary>
        /// Merges synthesized or deleted members generated during lowering, or emit, of the current compilation with aggregate
        /// synthesized or deleted members from all previous source generations (gen >= 1).
        /// </summary>
        /// <remarks>
        /// Suppose {S -> {A, B, D}, T -> {E, F}} are all synthesized members in previous generations,
        /// and {S' -> {A', B', C}, U -> {G, H}} members are generated in the current compilation.
        /// 
        /// Where X matches X' via this matcher, i.e. X' is from the new compilation and 
        /// represents the same metadata entity as X in the previous compilation.
        /// 
        /// Then the resulting collection shall have the following entries:
        /// {S' -> {A', B', C, D}, U -> {G, H}, T -> {E, F}}
        /// </remarks>
        internal ImmutableDictionary<ISymbolInternal, ImmutableArray<ISymbolInternal>> MapSynthesizedOrDeletedMembers(
            ImmutableDictionary<ISymbolInternal, ImmutableArray<ISymbolInternal>> previousMembers,
            ImmutableDictionary<ISymbolInternal, ImmutableArray<ISymbolInternal>> newMembers,
            bool isDeletedMemberMapping)
        {
            // Note: we can't just return previous members if there are no new members, since we still need to map the symbols to the new compilation.

            if (previousMembers.Count == 0)
            {
                return newMembers;
            }

            var synthesizedMembersBuilder = ImmutableDictionary.CreateBuilder<ISymbolInternal, ImmutableArray<ISymbolInternal>>();

            synthesizedMembersBuilder.AddRange(newMembers);

            foreach (var (previousContainer, members) in previousMembers)
            {
                var mappedContainer = MapDefinitionOrNamespace(previousContainer);
                if (mappedContainer == null)
                {
                    // No update to any member of the container type.  
                    synthesizedMembersBuilder.Add(previousContainer, members);
                    continue;
                }

                if (!newMembers.TryGetValue(mappedContainer, out var newSynthesizedMembers))
                {
                    // The container has been updated but the update didn't produce any synthesized members.
                    synthesizedMembersBuilder.Add(mappedContainer, members);
                    continue;
                }

                // The container has been updated and synthesized members produced.
                // They might be new or replacing existing ones. Merge existing with new.
                var memberBuilder = ArrayBuilder<ISymbolInternal>.GetInstance();
                memberBuilder.AddRange(newSynthesizedMembers);

                foreach (var member in members)
                {
                    var mappedMember = MapDefinitionOrNamespace(member);
                    if (mappedMember != null)
                    {
                        // If the matcher found a member in the current compilation corresponding to previous memberDef,
                        // then the member has to be synthesized and produced as a result of a method update 
                        // and thus already contained in newSynthesizedMembers.
                        // However, because this method is also used to map deleted members, it's possible that a method
                        // could be renamed in the previous generation, and renamed back in this generation, which would
                        // mean it could be mapped, but isn't in the newSynthesizedMembers list, so we allow the flag to
                        // override this behaviour for deleted methods.
                        Debug.Assert(isDeletedMemberMapping || newSynthesizedMembers.Contains(mappedMember));
                    }
                    else
                    {
                        memberBuilder.Add(member);
                    }
                }

                synthesizedMembersBuilder[mappedContainer] = memberBuilder.ToImmutableAndFree();
            }

            return synthesizedMembersBuilder.ToImmutable();
        }
    }
}
