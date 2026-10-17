// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns call retention (SPEC 15.6.4; design A3 4.3, C1+C8): which outputs of a call may retain which arguments, as A5's Loan flow reads it.
// Input: a callee with its call Subst, or a Value callee operand's Type (CallPlan.CalleeType, which MIR's operand reproduces) with the
// Callable signature it declares (CallPlan.DeclaredSignature, DECISIONS R4-M41); the calling definition (null: top-level statements).
// Output: an immutable CallRetention, rows Retention(Input, Kind, Output) and Untied, one per callee or signature, Type instantiation and
// the premise scope or declaration it reads, for the latest Binding pass.
// Invariants: an output retains an input exactly when the callee's body may move a value of the input there under the premises its
// body is checked under: the instantiated contract (the Subst's Types, Self and declaring Type substituted, the callee's own Origins
// left as the call's fresh regions, so retention is per argument) and the premises of its enclosing declarations; or when both name
// one abstract carrier. An erased callable, one whose implementation the call cannot see (a Function Type value or a value of a
// Callable-bounded Type parameter, each callable Type its contract names and each Callable bound of the callee's premises), was compared
// under premises no view reads, so it relates each fixed Origin of an input to each fixed Origin of an output, an abstract carrier's
// hidden Origins among them (owner decision 2026-10-17, DECISIONS R4-M52). An unproven absence of retention is a row.
// Must not: read the call's solved Origins or its arguments, judge an Origin relation, cover intrinsics or unknown callees (A5's), or
// expose its premise walk as a view of its own (A3 9, item 9).

using System.Diagnostics;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>How an output may retain an input (SPEC 15.6.4).</summary>
internal enum RetentionKind : byte
{
    /// <summary>An Origin of the input outside a borrowed input's referent reaches the output, which carries the argument's Loans and,
    /// for a borrow, those a read through it carries.</summary>
    Value,

    /// <summary>Only Origins inside a borrowed input's referent Type reach the output (step 4: its OwnedOrigins), which carries the
    /// Loans a read through the argument carries.</summary>
    Content,
}

/// <summary>One retention of a call: <see cref="Output"/>, the result (<see cref="Result"/>) or the writable storage of input j (j: its
/// storage at invariant positions, such as an exclusive input's referent or a raw pointee), may retain <see cref="Input"/>. Inputs are
/// the parameter slots <see cref="TyCtxt.Signature"/> numbers, the receiver at its written slot, or a Callable signature's input
/// positions.</summary>
internal readonly record struct Retention(int Input, RetentionKind Kind, int Output)
{
    internal const int Result = -1;
}

/// <summary>A call's retention: its rows, by output (the result, then each written input) and input, and whether a result atom is
/// reached from no input and is not <c>static</c>, so the result may refer to storage the caller does not own (design A3's Φ_E).</summary>
internal sealed class CallRetention(Retention[] rows, bool untied)
{
    private readonly Retention[] rows = rows;
    private readonly bool untied = untied;

    internal ReadOnlySpan<Retention> Rows => this.rows;

    internal bool Untied => this.untied;
}

internal sealed partial class TyCtxt
{
    // Per callee or signature, Type instantiation and the premise scope or binder the computation reads; discarded with each Binding pass,
    // since a callee's contract may change (ResetPass).
    private readonly Dictionary<(object Callee, Subst? Types, object? Scope), CallRetention> retentions = new();

    // The Callable bounds of the computation's premises, instantiated as its inputs; reused.
    private readonly List<BoundType> retentionBounds = new();

    // SPEC 15.6.4: a callee's retention under a call's Subst (a Fn, Requirement or Virtual MIR callee, the function the Subst targets; its
    // default templates are A5's), called in `definition`.
    internal CallRetention Retains(BindingSymbol callee, Subst? subst, BindingSymbol? definition)
    {
        Debug.Assert(subst is null || ReferenceEquals(subst.Target, callee));

        // Steps 1-2: the Types the call instantiates; the callee's own Origins stay its fresh regions. An instance with an abstract part is
        // read as the calling definition's premises make it (a requirement on an abstract Self: `I.Item`, not `I.LentItem(step)`, under
        // `I is Iterator`), so its key holds that premise scope; any other reads alike in every definition.
        var types = subst is null ? null : this.Intern(subst.Target, subst.Self, subst.Declaring, subst.Types, subst.Lengths, [], []);
        var scope = types is not null && HasAbstractPart(types) ? binding.EffectPremises(definition) : null;
        if (this.retentions.TryGetValue((callee, types, scope), out var known))
        {
            return known;
        }

        var result = this.Instance(callee, types, -1, scope, out var count);
        var inputs = new BoundType[count];
        for (var i = 0; i < count; i++)
        {
            inputs[i] = this.Instance(callee, types, i, scope, out _);
        }

        // Its Callable bounds, erased callables its body may call through a carrier, as the call instantiates them.
        var declaration = (FunctionKoto)callee.Declaration;
        var bounds = this.retentionBounds;
        binding.CallableBoundSignatures(declaration, bounds);
        for (var i = 0; i < bounds.Count; i++)
        {
            var bound = types is null ? bounds[i] : this.Apply(bounds[i], types) ??
                throw new InvalidOperationException($"The call substitution does not instantiate a Callable bound of {callee.Name}.");
            bounds[i] = scope is null ? bound : binding.CallableContractType(bound, scope);
        }

        var retention = binding.Retains(declaration, inputs, result, types?.Declaring, CollectionsMarshal.AsSpan(bounds));
        this.retentions.Add((callee, types, scope), retention);
        return retention;
    }

    // C8: a Value callee's retention, read over the Callable signature it declares, whose abstract Origins are the call's fresh regions
    // (step 1). A closure's or item's is read under the premises of its declaration's body (Binding.SignatureOwner); any other is an
    // erased callable, which reads no declaration's premises: its key is the calling definition's scope, which only decides its pair
    // layers' admitted cases.
    internal CallRetention Retains(BoundType callee, BoundType signature, BindingSymbol? definition)
    {
        var owner = (FunctionKoto?)Binding.SignatureOwner(callee);
        var use = binding.DefinitionUse(definition);
        var key = owner ?? (object)binding.ConstraintScope(use);
        if (this.retentions.TryGetValue((signature, null, key), out var known))
        {
            return known;
        }

        BoundType[] inputs = ReferenceEquals(signature.Components[0], BoundType.Unit) ? [] : (BoundType[])signature.Components[0].Components;
        CallRetention retention;
        if (owner is null)
        {
            retention = binding.RetainsErased(use, inputs, signature.Components[1]);
        }
        else
        {
            binding.CallableBoundSignatures(owner, this.retentionBounds);
            retention = binding.Retains(owner, inputs, signature.Components[1], null, CollectionsMarshal.AsSpan(this.retentionBounds));
        }

        this.retentions.Add((signature, null, key), retention);
        return retention;
    }

    internal void ResetRetentions() => this.retentions.Clear();

    private static bool HasAbstractPart(Subst types)
    {
        var found = types.Self?.HasAbstractPart == true || types.Declaring?.HasAbstractPart == true;
        foreach (var type in types.Types)
        {
            found |= type?.HasAbstractPart == true;
        }

        return found;
    }

    // A parameter's Type, or at -1 the result's, as the Types instantiate it, under `scope`'s premises when given (TyCtxt.Premised's
    // rule). A call MIR built has a contract its Subst instantiates (the validator reads the same Signature); one it does not is a
    // broken contract.
    private BoundType Instance(BindingSymbol callee, Subst? types, int slot, BindingScope? scope, out int count)
    {
        var type = Signature(callee, types, slot, out count) ??
            throw new InvalidOperationException($"The call substitution does not instantiate {callee.Name}'s signature.");
        return scope is null ? type : binding.CallableContractType(type, scope);
    }
}
