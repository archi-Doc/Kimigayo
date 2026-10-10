// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    internal readonly record struct CaseDestruction(BoundType? Type, Koto Source, ulong Cases);

    private List<CaseDestruction>? caseDestructions;
    private Dictionary<(BoundType? Type, Koto Source), int>? caseDestructionIndex;
    private List<PairCase>? cleanupCases;
    private int cleanupCaseWidth;

    internal bool HasCaseDestructions => this.cleanupCaseWidth != 0;

    internal IReadOnlyList<CaseDestruction> CaseDestructions => this.caseDestructions ?? (IReadOnlyList<CaseDestruction>)Array.Empty<CaseDestruction>();

    // Side bodies are reused for the next definition. Keep only their finite selectors and destroyed value parts.
    internal void AppendCleanupCase(ReadOnlySpan<PairCase> cases)
    {
        this.cleanupCaseWidth = cases.Length;
        var storage = this.cleanupCases ??= new();
        for (var i = 0; i < cases.Length; i++)
        {
            storage.Add(cases[i]);
        }
    }

    internal void AddCaseDestruction(BoundType? type, Koto source, ulong cases)
    {
        var storage = this.caseDestructions ??= new();
        var index = this.caseDestructionIndex ??= new();
        var key = (type, source);
        if (index.TryGetValue(key, out var at))
        {
            storage[at] = storage[at] with { Cases = storage[at].Cases | cases };
        }
        else
        {
            index.Add(key, storage.Count);
            storage.Add(new(type, source, cases));
        }
    }

    // A closed caller sees only its selected case. A still-symbolic binder keeps every case not ruled out by the call.
    internal ulong ApplicableCleanupCases(Binding binding, CallPlan? call)
    {
        if (call is null || this.cleanupCaseWidth == 0)
        {
            return ulong.MaxValue;
        }

        var cases = this.cleanupCases!;
        var count = cases.Count / this.cleanupCaseWidth;
        var applicable = count == OwnershipAnalysis.CaseBound ? ulong.MaxValue : (1UL << count) - 1;
        for (var slot = 0; slot < this.cleanupCaseWidth && applicable != 0; slot++)
        {
            if (cases[slot].Target.WholeType is not { } whole || binding.InstantiateStorageType(whole, call) is not { } resolved)
            {
                continue;
            }

            var scope = Binding.TryAdaptationSelector(resolved, out var wholeSelector) && wholeSelector.Symbol is { } selector
                ? selector.Scope : call.Target.Scope;
            var admitted = binding.ResultSemantics(resolved, scope);
            if (admitted == SemanticsMask.All)
            {
                continue;
            }

            var matching = 0UL;
            for (var index = 0; index < count; index++)
            {
                if ((cases[(index * this.cleanupCaseWidth) + slot].Semantics.ToMask() & admitted) != 0)
                {
                    matching |= 1UL << index;
                }
            }

            applicable &= matching;
        }

        return applicable;
    }

    private void ResetCleanupEffects()
    {
        this.caseDestructions?.Clear();
        this.caseDestructionIndex?.Clear();
        this.cleanupCases?.Clear();
        this.cleanupCaseWidth = 0;
    }
}
