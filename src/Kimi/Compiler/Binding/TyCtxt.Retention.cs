// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns call retention (SPEC 15.6.4; design A3 4.3, C1+C8): which outputs of a call may retain which arguments, as A5's Loan flow reads it.
// Input: a callee with its call Subst, or a Value callee operand's Type (CallPlan.CalleeType, which MIR's operand reproduces) with the
// Callable signature it declares (CallPlan.DeclaredSignature, DECISIONS R4-M41); the calling definition (null: top-level statements).
// Output: an immutable CallRetention, rows Retention(Input, Kind, Output) and Untied, one per callee or signature, Type instantiation and
// the premise scope or declaration it reads, for the latest Binding pass and reused by later passes until an edit; CallRetention.Limit
// where Binding's storage instantiation does not complete the contract (storage nested past its depth bound, which a growing recursive
// Type of SPEC 15.3.5 always reaches), never an exception.
// Invariants: an output retains an input exactly when the callee's body may move a value of the input there under the premises its
// body is checked under: the instantiated contract (the Subst's Types, Self and declaring Type substituted, the callee's own Origins
// left as the call's fresh regions, so retention is per argument) and the premises of its enclosing declarations; or when both name
// one abstract carrier. An erased callable, one whose implementation the call cannot see (a Function Type value or a value of a
// Callable-bounded Type parameter, each callable Type its contract names and each Callable bound of the callee's premises), was compared
// under premises no view reads, so it relates each fixed Origin of an input to each fixed Origin of an output, an abstract carrier's
// hidden Origins among them (owner decision 2026-10-17, DECISIONS R4-M52). An unproven absence of retention is a row. Raw layers
// (SPEC 5.7 Calls; D6, D10, D15; DECISIONS BU-24): nothing is written past a shared layer or a raw layer whose step is Shared; a raw
// parameter layer without an Origin, or with `static`, lets the call write its pointee, one whose Origin steps Exclusive what that
// Origin's borrows admit (their referents, a slot's Keepers) as that input's storage; a raw result layer with an Origin retains as that
// borrow would, one without retains every input and is untied. The row shape is the same for every layer.
// Must not: read the call's solved Origins or its arguments, judge an Origin relation, cover unknown callees or the operands an intrinsic
// takes beyond its library declaration (A5's; an intrinsic's declaration is a callee like any other, A3 E14), or expose its premise walk as
// a view of its own (A3 9, item 9).

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
/// reached from no input and is not <c>static</c>, so the result may refer to storage the caller does not own (design A3's Φ_E); or
/// <see cref="Limit"/>, which has neither.</summary>
internal sealed class CallRetention(Retention[]? rows, bool untied)
{
    /// <summary>The retention of a call whose contract Binding's storage instantiation does not complete under its Subst (storage nested
    /// past its depth bound, which a growing recursive Type of SPEC 15.3.5 always reaches), as A4's effects take it: no rows are computed,
    /// and A5 records a located Unsupported_Kd at the call. Reading its rows or Untied is a broken contract.</summary>
    internal static readonly CallRetention Limit = new(null, untied: false);

    private readonly Retention[]? rows = rows;
    private readonly bool untied = untied;

    internal bool IsLimit => this.rows is null;

    internal ReadOnlySpan<Retention> Rows => this.rows ?? throw new InvalidOperationException("The Limit retention has no rows.");

    internal bool Untied => this.rows is null ? throw new InvalidOperationException("The Limit retention has no Untied.") : this.untied;
}

internal sealed partial class TyCtxt
{
    // Per callee or signature, Type instantiation and the premise scope or binder the computation reads; kept across passes with no edit,
    // whose contracts are the same, and discarded with the source analysis at an edit (Binding.Invalidate).
    private readonly Dictionary<(object Callee, Subst? Types, object? Scope), CallRetention> retentions = new();

    // The Callable bounds of the computation's premises, instantiated as its inputs; reused.
    private readonly List<BoundType> retentionBounds = new();

    // SPEC 15.6.4: a callee's retention under a call's Subst (a Fn, Requirement or Virtual MIR callee, the function the Subst targets, or an
    // Intrinsic one's library declaration; its default templates are A5's), called in `definition`.
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

        var retention = this.InstanceRetention(callee, types, scope) ?? CallRetention.Limit;
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

    // The retention of a callee's contract and Callable bounds (erased callables its body may call through a carrier) as the Types
    // instantiate them; null where Binding's storage instantiation does not complete one of them (CallRetention.Limit).
    private CallRetention? InstanceRetention(BindingSymbol callee, Subst? types, BindingScope? scope)
    {
        if (this.Instance(callee, types, -1, scope, out var count) is not { } result)
        {
            return null;
        }

        var inputs = new BoundType[count];
        for (var i = 0; i < count; i++)
        {
            if (this.Instance(callee, types, i, scope, out _) is not { } input)
            {
                return null;
            }

            inputs[i] = input;
        }

        var declaration = (FunctionKoto)callee.Declaration;
        var bounds = this.retentionBounds;
        binding.CallableBoundSignatures(declaration, bounds);
        for (var i = 0; i < bounds.Count; i++)
        {
            if (this.Instance(bounds[i], types, scope) is not { } bound)
            {
                return null;
            }

            bounds[i] = bound;
        }

        return binding.Retains(declaration, inputs, result, types?.Declaring, CollectionsMarshal.AsSpan(bounds));
    }

    // A parameter's Type, or at -1 the result's, as the Types instantiate it (null: CallRetention.Limit). A declared contract without
    // the Type is a broken contract: MIR's validator reads the same Signature.
    private BoundType? Instance(BindingSymbol callee, Subst? types, int slot, BindingScope? scope, out int count)
    {
        var declared = Signature(callee, null, slot, out count) ?? throw new InvalidOperationException($"{callee.Name}'s signature has no Type at {slot}.");
        return this.Instance(declared, types, scope);
    }

    // A declared Type as the Types instantiate it, under `scope`'s premises when given (TyCtxt.Premised's rule); null where Binding's storage
    // instantiation does not complete it.
    private BoundType? Instance(BoundType declared, Subst? types, BindingScope? scope)
    {
        var type = types is null ? declared : this.Apply(declared, types);
        return type is null || scope is null ? type : binding.CallableContractType(type, scope);
    }
}
