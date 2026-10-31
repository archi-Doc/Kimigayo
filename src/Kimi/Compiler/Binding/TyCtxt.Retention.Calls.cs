// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call retention, per call (contract in TyCtxt.Retention.cs): TyCtxt.Retains over a callee and a call's Subst (a `drop` over its
// destruction receiver), or over a Value callee's declared signature, and RetainsErasedDestruction over a destroyed part, memoized until
// an edit, and the instantiated contract Binding computes it over (Binding.Retention.cs).
internal sealed partial class TyCtxt
{
    // HandedCallables' depth bound, MirTypes.Holding's: past it a growing recursive Type's parts are the Limit its hold walk reports.
    private const int HandedBound = 32;

    // Per callee, signature or erased destruction's receiver, Type instantiation and the premise scope or binder the computation reads;
    // kept across passes with no edit, whose contracts are the same, and discarded with the source analysis at an edit (Binding.Invalidate).
    private readonly Dictionary<(object Callee, Subst? Types, object? Scope), CallRetention> retentions = new();

    // The Callable bounds of the computation's premises, instantiated as its inputs; reused.
    private readonly List<Binding.ErasedSignature> retentionBounds = new();

    // HandedCallables' answers per interned Type, kept as the retentions are, and its walk's scratch.
    private readonly Dictionary<BoundType, BoundType[]> handedCallables = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundType> handedFound = new();
    private readonly HashSet<BoundType> handedSeen = new(ReferenceEqualityComparer.Instance);

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

        var retention = this.InstanceRetention(callee, types, scope, definition) ?? CallRetention.Limit;
        this.retentions.Add((callee, types, scope), retention);
        return retention;
    }

    // SPEC 15.6.4, 8.4 (β′, DECISIONS R4-M154): the erased signatures of a Contract's requirements on a Self, each `(inputs) -> result` with
    // Self and the Contract reference's bindings substituted (a function requirement's parameters and result, a Property requirement's
    // present accessors' receiver, input and result) and the declaration binding its per-call Origins (the requirement, or the
    // accessor, else its Property), into `signatures`; whether every one substitutes (false: CallRetention.Limit).
    internal bool RequirementSignatures(BoundType self, BindingSymbol contract, List<Binding.ErasedSignature> signatures)
    {
        foreach (var requirement in contract.Contract!.Requirements)
        {
            var subst = requirement.Contract.Type is { } reference ? this.Synthesized(requirement.Symbol, self, reference)
                : this.Intern(requirement.Symbol, self, null, [], [], [], []);
            var substituted = requirement.Symbol.Property is { } property
                ? this.AccessorSignature(property.Getter, subst, signatures) && this.AccessorSignature(property.Setter, subst, signatures)
                : this.FunctionSignature(requirement.Symbol, subst, signatures);
            if (!substituted)
            {
                return false;
            }
        }

        return true;
    }

    // C8: a Value callee's retention, read over the Callable signature it declares, whose abstract Origins are the call's fresh regions
    // (step 1). A closure's or item's is read under the premises of its declaration's body (Binding.SignatureOwner); any other is an
    // erased callable, which reads no declaration's premises: its key is the calling definition's scope, which only decides its pair
    // layers' admitted cases. A Closure's contract has its call receiver at input 0 (HasReceiverInput; SPEC 15.8.2, DECISIONS R4-M148),
    // the Type its body is checked against (Binding.ClosureReceiver), whose environment the walk reads as the body sees it
    // (ClosureEnvironment); its key holds the instance, its definition and closure context. A Closure whose definition did not bind is
    // keyed as an item is, `(signature, null, owner)`, the shape of a bound one without a context, which is complete: a definition's bound
    // state is fixed for one Binding result, whose edit discards the memo.
    internal CallRetention Retains(BoundType callee, BoundType signature, BindingSymbol? definition)
    {
        var closure = Closure(callee);
        var owner = closure is null ? (FunctionKoto?)Binding.SignatureOwner(callee) : (FunctionKoto)closure.Symbol!.Declaration!;
        var use = binding.DefinitionUse(definition);
        (object Callee, Subst? Types, object? Scope) key = closure is null ? (signature, null, owner ?? (object)binding.ConstraintScope(use))
            : (signature, closure.ClosureContext, owner);
        if (this.retentions.TryGetValue(key, out var known))
        {
            return known;
        }

        BoundType[] inputs = ReferenceEquals(signature.Components[0], BoundType.Unit) ? [] : (BoundType[])signature.Components[0].Components;
        CallRetention retention;
        if (owner is null)
        {
            retention = binding.RetainsErased(use, inputs, signature.Components[1], Binding.FunctionTypeBinder(signature));
        }
        else
        {
            inputs = closure is null ? inputs : [binding.ClosureReceiver(owner, closure), .. inputs];
            retention = !binding.HandedSignatures(owner, [.. signature.Components], this.retentionBounds) ? CallRetention.Limit
                : binding.Retains(owner, inputs, signature.Components[1], null, CollectionsMarshal.AsSpan(this.retentionBounds), premises: null);
        }

        this.retentions.Add(key, retention);
        return retention;
    }

    // SPEC 16.3.3, 15.6.4 (DECISIONS BU-47): an erased destruction's retention, of a destroyed part whose `drop` the destruction walk
    // cannot see (a handle whose View Target is not Sealed, a Function value's environment, an abstract part in its generic body): the
    // erased call `(uniq/P) -> ()` (R4-M52) over its destruction receiver, read and keyed as a Value call's erased signature is, under
    // the calling definition's constraint scope.
    internal CallRetention RetainsErasedDestruction(BoundType part, BindingSymbol? definition)
    {
        var use = binding.DefinitionUse(definition);
        var receiver = this.DestructionReceiver(part);
        (object Callee, Subst? Types, object? Scope) key = (receiver, null, binding.ConstraintScope(use));
        if (this.retentions.TryGetValue(key, out var known))
        {
            return known;
        }

        var retention = binding.RetainsErased(use, [receiver], BoundType.Unit, null);
        this.retentions.Add(key, retention);
        return retention;
    }

    // SPEC 15.8.2, 8.10 (DECISIONS R4-M126 D-6, R4-M148): whether a Value callee's contract has the call receiver input, input 0: the
    // callee, without its acquisition layer (Binding.CallableCore), is a Closure whose definition is bound. The one predicate Retains and
    // MirBorrows' slot mapping read; a Function value, a function item and a Callable-bounded value have none.
    internal bool HasReceiverInput(BoundType callee) => Closure(callee) is not null;

    // SPEC 15.8.2 (DECISIONS R4-M148): the environment a Closure Type's value holds as its body sees it, the one reading of every Closure
    // part of a contract input (a Closure callee's receiver, Binding.RetentionInput; one nested in an input, RetentionParts): its definition's
    // environment Type under the Type's closure context (ReadableInstance, DECISIONS BU-36, R4-M139; null where it does not substitute,
    // CallRetention.Limit). A Closure Type a local's initializer inferred names the local's regions, not the Origins its signature and its
    // body name. A Closure whose definition Binding did not bind is read as it is, a dependent of Binding's rejection.
    internal BoundType? ClosureEnvironment(BoundType closure)
    {
        if (BoundClosure.Of(closure)?.EnvironmentType is not { } environment)
        {
            return closure;
        }

        return ClosureInstance(closure) is { } context ? this.ReadableInstance(environment, context) : environment;
    }

    // SPEC 7.7.3 (DECISIONS R4-M128, R4-M148): the call receiver a Closure Type's case admits, given a case Type of a view or the
    // instance: the definition's case-free receiver unless it is Shared, else Exclusive where a capture whose bare acquisition Reborrows
    // by case (BoundCapture.BareReborrows) has an exclusive layer as its component in this Type (`uniq`, `objuniq`, or a pair layer
    // admitting one), else Shared; the instance gives Receiver. PD13's hold compares a call's acquisition with it (MirBorrows.CallRows).
    internal SemanticsKind ClosureReceiverIn(BoundType closure)
    {
        var definition = BoundClosure.Of(closure)!;
        if (definition.CaseFreeReceiver != SemanticsKind.Ref)
        {
            return definition.CaseFreeReceiver;
        }

        for (var i = 0; i < definition.Captures.Count; i++)
        {
            if (definition.Captures[i].BareReborrows && closure.Components[i] is var component &&
                (component is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq } ||
                (binding.LayerAdmitted(component) & (SemanticsMask.Uniq | SemanticsMask.ObjUniq)) != 0))
            {
                return SemanticsKind.Uniq;
            }
        }

        return SemanticsKind.Ref;
    }

    // SPEC 15.6.4, 8.10 (DECISIONS R4-M52, R4-M154): the callables a value of a Type hands a callee's body that holds it, each an erased
    // callable whose relations its retention adds: every Function Type at any depth of its components, a pair layer's target and a
    // Closure part's environment as its body sees it (ClosureEnvironment) included, the Type itself among them; walked once per interned
    // Type (each part once) and kept until an edit, as the retentions are.
    internal ReadOnlySpan<BoundType> HandedCallables(BoundType type)
    {
        if (!this.handedCallables.TryGetValue(type, out var known))
        {
            this.handedFound.Clear();
            this.handedSeen.Clear();
            this.Handed(type, 0);
            known = this.handedFound.Count == 0 ? [] : [.. this.handedFound];
            this.handedCallables.Add(type, known);
        }

        return known;
    }

    internal void ResetRetentions()
    {
        this.retentions.Clear();
        this.handedCallables.Clear();
    }

    // The Closure Type a Value callee is without its acquisition layer (Binding.CallableCore), with a bound definition; null for any other.
    private static BoundType? Closure(BoundType callee)
        => Binding.CallableCore(callee) is { } closure && BoundClosure.Of(closure) is { EnvironmentType: not null } ? closure : null;

    private static bool HasAbstractPart(Subst types)
    {
        var found = types.Self?.HasAbstractPart == true || types.Declaring?.HasAbstractPart == true;
        foreach (var type in types.Types)
        {
            found |= type?.HasAbstractPart == true;
        }

        return found;
    }

    // SPEC 15.6.4 (β′, DECISIONS R4-M154 rule 3, Revision 9): the Binder of a callee's erased signature where its implementation is
    // erased at the call, read as its own instantiated signature (Binding.RetainsErased), else null: a requirement on a Self with an
    // abstract part, whose implementation is any conforming Type's (SPEC 8.15.1), or a virtual or overriding function on a declaring Type
    // with one, whose implementation is any deriving Type's override, bounded by the original's declared contract (SPEC 6.4, 8.13.2); the
    // callee's own declaration binds its per-call Origins, an accessor's Binder an accessor's (the `use` Binding.Retains takes). On a
    // concrete Self the standard computation reads the contract.
    private static Koto? ErasedCallee(FunctionKoto callee, Subst? types)
    {
        var self = callee switch
        {
            { IsRequirement: true } => types?.Self,
            { IsVirtual: true } or { IsOverride: true } => types?.Declaring,
            _ => null,
        };
        return self?.HasAbstractPart == true ? callee.Accessor?.Binder ?? callee : null;
    }

    // A callee's declared parameter and result Types, in its own terms.
    private static BoundType[] Declared(BindingSymbol callee)
    {
        var result = Signature(callee, null, -1, out var count)!;
        var declared = new BoundType[count + 1];
        for (var i = 0; i < count; i++)
        {
            declared[i] = Signature(callee, null, i, out _)!;
        }

        declared[count] = result;
        return declared;
    }

    // The retention of a callee's contract and the callables its premises hand its body (Binding.HandedSignatures, erased callables it may
    // call) as the Types instantiate them, or of an erased callee's own signature (ErasedCallee) in `definition`; null where one of them is
    // past Binding's storage limits or does not substitute (CallRetention.Limit; Instance). A `drop` declares no parameter (Signature
    // numbers none): its input 0 is its destruction receiver over the layer the Types instantiate.
    private CallRetention? InstanceRetention(BindingSymbol callee, Subst? types, BindingScope? scope, BindingSymbol? definition)
    {
        if (this.Instance(callee, types, -1, scope, out var count) is not { } result)
        {
            return null;
        }

        var declaration = (FunctionKoto)callee.Declaration;
        BoundType[] inputs;
        if (declaration.IsDestructor)
        {
            if (this.Instance(AdtDef.ReceiverType(declaration)!, types, scope) is not { } layer)
            {
                return null;
            }

            inputs = [this.DestructionReceiver(layer)];
        }
        else
        {
            inputs = new BoundType[count];
            for (var i = 0; i < count; i++)
            {
                if (this.Instance(callee, types, i, scope, out _) is not { } input)
                {
                    return null;
                }

                inputs[i] = input;
            }
        }

        if (ErasedCallee(declaration, types) is { } binder)
        {
            return binding.RetainsErased(binding.DefinitionUse(definition), inputs, result, binder);
        }

        var bounds = this.retentionBounds;
        if (!binding.HandedSignatures(declaration, Declared(callee), bounds))
        {
            return null;
        }

        for (var i = 0; i < bounds.Count; i++)
        {
            if (this.Instance(bounds[i].Signature, types, scope) is not { } bound)
            {
                return null;
            }

            bounds[i] = bounds[i] with { Signature = bound };
        }

        return binding.Retains(declaration, inputs, result, types, CollectionsMarshal.AsSpan(bounds), scope);
    }

    // SPEC 16.3.1, 15.6.4 (DECISIONS BU-47): the destruction receiver of a destroyed part, input 0 of its destruction's retention: `uniq/P`
    // without an Origin, which no caller names, so the input has no Value atom and its retention no Value row.
    private BoundType DestructionReceiver(BoundType part) => this.Reference(SemanticsKind.Uniq, part);

    // A parameter's Type, or at -1 the result's, as the Types instantiate it (null: CallRetention.Limit). A declared contract without
    // the Type is a broken contract: MIR's validator reads the same Signature.
    private BoundType? Instance(BindingSymbol callee, Subst? types, int slot, BindingScope? scope, out int count)
    {
        var declared = Signature(callee, null, slot, out count) ?? throw new InvalidOperationException($"{callee.Name}'s signature has no Type at {slot}.");
        return this.Instance(declared, types, scope);
    }

    // A declared Type as the Types instantiate it, under `scope`'s premises when given (TyCtxt.Premised's rule); null where its storage is a
    // Limit or the substitution does not complete. Rejected storage is read as instantiated: its parts that do not substitute hold nothing
    // (ReadableInstance, DECISIONS BU-36, R4-M139).
    private BoundType? Instance(BoundType declared, Subst? types, BindingScope? scope)
    {
        var type = types is null ? declared : this.ReadableInstance(declared, types);
        return type is null || scope is null ? type : binding.CallableContractType(type, scope);
    }

    // A function requirement's signature under a Subst into `signatures`; whether it substitutes.
    private bool FunctionSignature(BindingSymbol function, Subst subst, List<Binding.ErasedSignature> signatures)
    {
        var result = this.Instance(function, subst, -1, null, out var count);
        var inputs = new BoundType[count];
        for (var i = 0; i < count; i++)
        {
            if (this.Instance(function, subst, i, null, out _) is not { } input)
            {
                return false;
            }

            inputs[i] = input;
        }

        return result is not null && this.AddSignature(inputs, result, function.Declaration, signatures);
    }

    // A present Property accessor's signature, `(receiver, input) -> result` with each it declares, under a Subst into `signatures`;
    // whether it substitutes.
    private bool AccessorSignature(BoundAccessor accessor, Subst subst, List<Binding.ErasedSignature> signatures)
    {
        if (!accessor.IsPresent)
        {
            return true;
        }

        var inputs = new List<BoundType>(2);
        foreach (var declared in (ReadOnlySpan<BoundType?>)[accessor.Receiver, accessor.Input])
        {
            if (declared is not null && this.Instance(declared, subst, null) is var input)
            {
                if (input is null)
                {
                    return false;
                }

                inputs.Add(input);
            }
        }

        var result = accessor.Result is null ? BoundType.Unit : this.Instance(accessor.Result, subst, null);
        return result is not null && this.AddSignature(CollectionsMarshal.AsSpan(inputs), result, accessor.Binder, signatures);
    }

    // The erased signature of the Function Type `(inputs) -> result` with the Binder of its per-call Origins into `signatures`, once.
    private bool AddSignature(ReadOnlySpan<BoundType> inputs, BoundType result, Koto binder, List<Binding.ErasedSignature> signatures)
    {
        var signature = new Binding.ErasedSignature(
            this.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [inputs.IsEmpty ? BoundType.Unit : this.Tuple(inputs), result]),
            binder);
        if (!signatures.Contains(signature))
        {
            signatures.Add(signature);
        }

        return true;
    }

    // HandedCallables' walk: each part once, to the depth bound past which a growing recursive Type's parts are a Limit Held reports.
    private void Handed(BoundType? type, int depth)
    {
        if (type is null || depth > HandedBound || !this.handedSeen.Add(type))
        {
            return;
        }

        if (type.Kind == BoundTypeKind.Function)
        {
            this.handedFound.Add(type);
        }

        if (Binding.TryPairLayer(type, out _, out var target))
        {
            this.Handed(target, depth + 1);
            return;
        }

        var parts = type.Kind == BoundTypeKind.Closure ? this.ClosureEnvironment(type) : type;
        for (var i = 0; parts is not null && i < parts.Components.Count; i++)
        {
            this.Handed(parts.Components[i], depth + 1);
        }

        var stored = this.StoredParts(type, out var parent);
        for (var i = 0; i < stored.Length; i++)
        {
            this.Handed(stored[i], depth + 1);
        }

        this.Handed(parent, depth + 1);
    }
}
