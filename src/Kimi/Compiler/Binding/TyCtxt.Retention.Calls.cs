// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call retention, per call (contract in TyCtxt.Retention.cs): TyCtxt.Retains over a callee and a call's Subst, or over a Value
// callee's declared signature, memoized until an edit, and the instantiated contract Binding computes it over (Binding.Retention.cs).
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
