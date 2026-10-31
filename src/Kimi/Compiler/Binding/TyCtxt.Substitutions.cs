// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns substitution interning (Subst): the substitutions of selected calls, closures, function items and the calls the compiler
// synthesizes, and their application to a Type and composition (SPEC 8.1.3, 8.6.4, 8.13.3; plan 5.6.2; DECISIONS R5-M36, R5-M43, R5-M70).
// Input: CallPlans, Closure and function item Types, a binder, a target with its slot bindings; Binding's storage instantiation, length
// substitution and Origin forwarding.
// Output: interned Subst values, identity being equality, kept as long as the TyCtxt; a Type or a substitution in an instance with its
// storage verdict (Instance; Compose with the part deciding it; DECISIONS BU-36, BU-38, R4-M139), Apply the instance when Prepared;
// Binding's completeness, the premise of a Rejected verdict's contract (BindingComplete); whether a reader may read a verdict, guarded by
// it (Readable), and the instance a reader of an instance reads (ReadableInstance: Instance, then Readable; R4-M139).
// Invariants: one Subst per (target, Self, declaring Type, Types, lengths, Origins, inputs), with one length entry per slot; the scratch
// arrays Fit grows are reused.
// Must not: clear its interning, resolve source expressions, or construct a Subst outside Intern.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class TyCtxt
{
    private readonly Dictionary<int, List<Subst>> substs = new();

    // Compose's bindings before interning, reused.
    private BoundType?[] composedTypes = [];
    private BoundLength?[] composedLengths = [];
    private BoundOrigin[] composedOrigins = [];

    // A function item's own Type arguments before interning, reused.
    private BoundType?[] itemTypes = [];

    // The lengths of a substitution whose binder has no length slot; never written, so every entry is null.
    private BoundLength?[] noLengths = [];

    // DIAGNOSTICS rule 8 (DECISIONS BU-38): whether Binding is complete, so has rejected no part: a Rejected storage verdict (Instance,
    // Compose) met in a complete compilation is a broken contract, the premise of Readable's guard and R4-M118's OriginFinding.
    internal bool BindingComplete => binding.Result.IsComplete;

    // DECISIONS BU-60: the Type instance a Closure Type's context holds (HeldContext); null for a context a function's instance made, and
    // for none.
    internal static BoundType? HeldType(BoundType closure)
        => closure.ClosureContext is { Declaring: { } held } context && ReferenceEquals(context.Target, closure.Symbol) ? held : null;

    // SPEC 6.1.3, 15.8.2 (DECISIONS BU-60): the substitution a Closure Type's readers apply, the one view of its context that its signature
    // (Binding.ClosureDefinitionSignature), its environment (ClosureEnvironment) and its body (SubstOfClosure) read: the context, except a
    // held instance with an Origin position (HasOriginPosition), which is closed in no instance, the EnclosingInstance gap until PLAN G300's
    // per-use opening. A reader reads a Type after its closing step, so the gate is decided on the closed, normalized instance.
    internal static Subst? ClosureInstance(BoundType closure)
        => HeldType(closure) is { } held && HasOriginPosition(held) ? null : closure.ClosureContext;

    // Whether a reader of an instance may read a Type whose storage has this verdict (Binding.Readable; DECISIONS BU-36, R4-M139):
    // Prepared or Rejected, not a Limit; Rejected in a complete compilation throws, a broken contract.
    internal bool Readable(BoundType type, Binding.StorageOutcome storage) => binding.Readable(type, storage);

    // DECISIONS R4-M139: a declared Type in the instance a substitution makes, as every reader of an instance reads one: Instance, then
    // Readable; null where Readable refuses (a Limit) or the substitution does not complete, throwing where it throws.
    internal BoundType? ReadableInstance(BoundType type, Subst subst)
        => this.Instance(type, subst) is ({ } instance, var storage) && this.Readable(instance, storage) ? instance : null;

    // SPEC 8.1.3, 10.5: a selected call's substitution, as its selection committed it; a value call has none. A direct base call's is its
    // implementation's in the Type implementing it, which it calls (virtual-dispatch 4).
    internal Subst? SubstOf(CallPlan call) => call.Kind == CalleeKind.Value ? null
        : call is { Kind: CalleeKind.Virtual, VirtualIsDirect: true, VirtualImplementation.BoundSymbol: { } implementation }
            ? this.Intern(implementation, null, call.VirtualImplementingType, call.TypeArguments, call.LengthArguments, call.Origins, call.InputOrigins)
            : this.Intern(call.Target, call.ConformingType, call.DeclaringType ?? BoundContract(call), call.TypeArguments, call.LengthArguments, call.Origins, call.InputOrigins);

    // SPEC 6.1.3, 8.1.3: a closure's substitution, the context of the instance that closed it as its readers see it (ClosureInstance); a
    // closure no instance closed is in the identity of the declaration whose instance it holds (EnclosingDeclaration), else in its own, as
    // a held instance its readers do not see is (DECISIONS BU-60). The closure is the Type's definition (BoundClosure.Of; DECISIONS R5-M63,
    // R4-M152).
    internal Subst? SubstOfClosure(BoundType closure)
    {
        if (closure is not { Symbol: { Declaration: FunctionKoto { ClosureDefinition: not null } } symbol })
        {
            return null;
        }

        if (ClosureInstance(closure) is { } instance)
        {
            return instance;
        }

        var frame = closure.ClosureContext is null ? this.EnclosingDeclaration(symbol) ?? symbol : symbol;
        return this.Identity(frame.Declaration);
    }

    // DECISIONS BU-60: the one producer of the context of a closure a Type's instance holds (a Field initializer's): the instance modulo
    // Origins (Erase), as the declaring Type of a substitution whose target is the closure itself; none where it is the erased identity of
    // the closure's declaring Type (EnclosingDeclaration), the identity test, so every read in that Type's own frame keeps its identity. A
    // function of the erased instance alone, so every order of steps that reaches one closed, normalized instance gives one context. Open
    // while the instance is, a part of the Closure Type that each walk rewriting a binder's slots, projections, identities or Semantics
    // cases rewrites (HeldType) and gives back here.
    internal Subst? HeldContext(BindingSymbol closure, BoundType instance)
    {
        var erased = this.Erase(instance);
        return ReferenceEquals(erased, this.Erase(binding.SelfType(this.EnclosingDeclaration(closure)!))) ? null
            : this.Intern(closure, null, erased, [], [], [], []);
    }

    // The substitution Binding's storage instantiation of a call applies (Binding.InstantiateStorageType(type, call)): its target's slots,
    // declaring Type and Origins, without the conforming Self, the bound Contract reference or the implementation of a direct base call
    // that SubstOf adds, as the old analyses have always instantiated a call's Types. A second substitution per call beside SubstOf, it
    // goes when the readers of that CallPlan form apply SubstOf instead: the old analyses' at R6-check and Binding's own with R7's CallPlan
    // pipeline (DECISIONS R5-M36).
    [Transitional("R7", Reason = "R7's CallPlan pipeline moves the last readers of the CallPlan form of InstantiateStorageType to SubstOf.")]
    internal Subst StorageSubst(CallPlan call)
        => this.Intern(call.Target, null, call.DeclaringType, call.TypeArguments, call.LengthArguments, call.Origins, call.InputOrigins);

    // SPEC 7.3: a function item's substitution, the context its Item Type binds: its function's own Type arguments, the declaring Type its
    // last Component holds and its length arguments, without Origins (an Item keeps its per-call Origins in place); a function of the Type
    // alone, which caches and prepares nothing (DECISIONS R5-M43). A requirement Item's is its context's call (Binding's): its witness's for a
    // closed Self, which A4 enters, the requirement's on an abstract one; what an Item runs in the checked set is Witness(item) (R5-M70).
    internal Subst? SubstOfItem(BoundType item)
    {
        if (item is not { Kind: BoundTypeKind.FunctionItem, Symbol: { Declaration: FunctionKoto function } target })
        {
            return null;
        }

        if (function.IsRequirement)
        {
            return binding.FunctionItemContext(item) is { } context ? this.SubstOf(context) : null;
        }

        var count = function.GenericArguments.Count;
        _ = Fit(ref this.itemTypes, count);
        Binding.CopyItemArguments(item, function, this.itemTypes);
        return this.Intern(target, null, Binding.ItemDeclaringType(item, function), this.itemTypes.AsSpan(0, count), item.LengthArguments, [], []);
    }

    // The substitution that binds none of a binder's slots: its own body's.
    internal Subst Identity(Koto binder) => this.Intern(binder.BoundSymbol!, null, null, [], [], [], []);

    // A call the MIR builder synthesizes, of the function serving a built-in form (R3 decision 5): its declaring Type's instance and its Type
    // arguments; its Origins are the analyses' to relate.
    internal Subst Synthesized(BindingSymbol target, BoundType? declaring, ReadOnlySpan<BoundType?> types) => this.Intern(target, null, declaring, types, [], [], []);

    // A call of a requirement on a Self through a Contract reference that a compiler function or a requirement's function item makes (a
    // comparison's Requirement leaf, a compiler function's call of a user witness, an Item's call): a bound reference is its declaring Type
    // (F fact 5, as SubstOf holds a call's), a Contract's own Type binds nothing; its Origins are the analyses' to relate.
    internal Subst Synthesized(BindingSymbol requirement, BoundType self, BoundType reference)
        => this.Intern(requirement, self, ReferenceEquals(reference.Symbol?.Type, reference) ? null : reference, [], [], [], []);

    // SPEC 8.1.3, 8.15.1: a declared Type in the instance a substitution makes whose storage is Prepared (Instance); null otherwise.
    internal BoundType? Apply(BoundType type, Subst subst) => this.Instance(type, subst) is ({ } instance, Binding.StorageOutcome.Prepared) ? instance : null;

    // A declared Type in the instance a substitution makes with its storage's verdict (DECISIONS BU-36), which Binding computes until R7
    // moves it here (Binding.StorageInstance, then Binding.PrepareTypeStorage): the identity keeps the Type, whose storage it does not query
    // (Prepared); null and Limit where the substitution does not complete.
    internal (BoundType? Type, Binding.StorageOutcome Storage) Instance(BoundType type, Subst subst)
    {
        if (subst.IsIdentity)
        {
            return (type, Binding.StorageOutcome.Prepared);
        }

        var instance = binding.StorageInstance(type, subst);
        return (instance, instance is null ? Binding.StorageOutcome.Limit : binding.PrepareTypeStorage(instance));
    }

    // Plan 5.6.2: the substitution of a call in a body that another substitution instantiates, as a call in a default template folds under
    // the substitution of the call omitting the default: each binding of the inner one in the outer instance, as Binding forwards a call of
    // a generic body (InstantiateForwardedCall), so Instance(t, Compose(inner, outer).Subst) is that call's instantiation of t; with the
    // verdict of its parts' storage in the outer instance (Instance), the worst one, so a limit wins (DECISIONS BU-36, BU-38). Rejected
    // keeps the composed substitution, as the forwarded call keeps its Types; null and Limit when the outer instance does not substitute an
    // inner Type, length, conforming Self or declaring Type, or one's storage is a limit. Part is the verdict's deciding part (R4-M139):
    // the instance of the first part reaching it in the order Compose walks (conforming Self, declaring Type, Types by index); null when
    // Prepared, when a length decides, or when that part has no instance. Instance gives a Limit exactly where it gives no instance, so
    // Part is never null when Rejected.
    internal (Subst? Subst, Binding.StorageOutcome Storage, BoundType? Part) Compose(Subst inner, Subst outer)
    {
        if (outer.IsIdentity)
        {
            return (inner, Binding.StorageOutcome.Prepared, null);
        }

        var storage = Binding.StorageOutcome.Prepared;
        var part = default(BoundType);
        var types = Fit(ref this.composedTypes, inner.Types.Length);
        var lengths = Fit(ref this.composedLengths, inner.Lengths.Length);
        var origins = Fit(ref this.composedOrigins, inner.Origins.Length + inner.Inputs.Length);
        var self = inner.Self is { } conforming ? Part(conforming) : null;
        var declaring = inner.Declaring is { } owner ? Part(owner) : null;
        for (var i = 0; i < types.Length; i++)
        {
            types[i] = inner.Types[i] is { } type ? Part(type) : null;
        }

        for (var i = 0; i < lengths.Length; i++)
        {
            lengths[i] = inner.Lengths[i] is { } length ? binding.SubstituteLength(length, outer.Binder, outer.Lengths) : null;
            if (lengths[i] is null && inner.Lengths[i] is not null && storage != Binding.StorageOutcome.Limit)
            {
                (storage, part) = (Binding.StorageOutcome.Limit, null);
            }
        }

        if (storage == Binding.StorageOutcome.Limit)
        {
            return (null, storage, part);
        }

        for (var i = 0; i < origins.Length; i++)
        {
            origins[i] = (i < inner.Origins.Length ? inner.Origins[i] : inner.Inputs[i - inner.Origins.Length]) is { } origin
                ? binding.ForwardOrigin(origin, outer.Target, outer.Declaring, outer.Origins, outer.Inputs, outer.Types) : null!;
        }

        return (this.Intern(inner.Target, self, declaring, types, lengths, origins[..inner.Origins.Length], origins[inner.Origins.Length..]), storage, part);

        // A part in the outer instance; its verdict joins the composition's in the verdicts' order (a limit, then Rejected), the first part
        // reaching the worst one deciding it.
        BoundType? Part(BoundType type)
        {
            var (instance, verdict) = this.Instance(type, outer);
            if (verdict > storage)
            {
                (storage, part) = (verdict, instance);
            }

            return instance;
        }
    }

    // SPEC 8.4.1, 8.6.4: the bound Contract reference a requirement call dispatches through (Indexable<isize>), which declares the requirement as a
    // generic container's instance declares a member; null for a reference to the requirement's own Contract or another call.
    private static BoundType? BoundContract(CallPlan call)
        => call is { Kind: CalleeKind.Requirement, RequirementContract: { Type: { } bound } contract } && !ReferenceEquals(bound.Symbol, contract) ? bound : null;

    private static Span<T> Fit<T>(ref T[] scratch, int length)
    {
        if (scratch.Length < length)
        {
            scratch = new T[length * 2];
        }

        return scratch.AsSpan(0, length);
    }

    // SPEC 8.13.3: a substitution holds one length entry per slot, the selection key's form, as a CallPlan does. The lengths are empty (the
    // binder has no length slot) or one per slot of the Types.
    private Subst Intern(BindingSymbol target, BoundType? self, BoundType? declaring, ReadOnlySpan<BoundType?> types, ReadOnlySpan<BoundLength?> lengths, ReadOnlySpan<BoundOrigin> origins, ReadOnlySpan<BoundOrigin> inputs)
    {
        System.Diagnostics.Debug.Assert(lengths.IsEmpty || lengths.Length == types.Length);
        lengths = lengths.IsEmpty ? Fit(ref this.noLengths, types.Length) : lengths;
        var hash = default(HashCode);
        hash.Add(RuntimeHelpers.GetHashCode(target));
        hash.Add(self is null ? 0 : RuntimeHelpers.GetHashCode(self));
        hash.Add(declaring is null ? 0 : RuntimeHelpers.GetHashCode(declaring));
        foreach (var type in types)
        {
            hash.Add(type is null ? 0 : RuntimeHelpers.GetHashCode(type));
        }

        foreach (var length in lengths)
        {
            hash.Add(length is null ? 0 : RuntimeHelpers.GetHashCode(length));
        }

        foreach (var origin in origins)
        {
            hash.Add(RuntimeHelpers.GetHashCode(origin));
        }

        foreach (var input in inputs)
        {
            hash.Add(RuntimeHelpers.GetHashCode(input));
        }

        var key = hash.ToHashCode();
        if (this.substs.TryGetValue(key, out var bucket))
        {
            for (var i = 0; i < bucket.Count; i++)
            {
                var subst = bucket[i];
                if (ReferenceEquals(subst.Target, target) && ReferenceEquals(subst.Self, self) && ReferenceEquals(subst.Declaring, declaring) && subst.Types.SequenceEqual(types) &&
                    subst.Lengths.SequenceEqual(lengths) && subst.Origins.SequenceEqual(origins) && subst.Inputs.SequenceEqual(inputs))
                {
                    return subst;
                }
            }
        }
        else
        {
            this.substs.Add(key, bucket = new(1));
        }

        var created = new Subst(target, self, declaring, types.ToArray(), lengths.ToArray(), origins.ToArray(), inputs.ToArray());
        bucket.Add(created);
        return created;
    }
}

/// <summary>The substitution of a binder's generic slots that a selected call, an instantiated closure or a function item makes: its Type
/// and length arguments (a pair slot holds the whole Type), the conforming Type of a requirement, the Type declaring a member of a generic
/// container and the principal Origin substitution the selection committed. <see cref="TyCtxt"/> interns it, so identity is equality.</summary>
internal sealed class Subst(BindingSymbol target, BoundType? self, BoundType? declaring, BoundType?[] types, BoundLength?[] lengths, BoundOrigin[] origins, BoundOrigin[] inputs)
{
    // The symbol whose slots it binds: a call's callee as its selection names it (an instantiated witness of a declaration too), or the
    // function enclosing a closure.
    internal BindingSymbol Target => target;

    internal Koto Binder => target.Declaration;

    internal BoundType? Self => self;

    internal BoundType? Declaring => declaring;

    // In declaration-slot order; length slots are null.
    internal ReadOnlySpan<BoundType?> Types => types;

    // In declaration-slot order, one per slot of Types; Type slots are null.
    internal ReadOnlySpan<BoundLength?> Lengths => lengths;

    // The substitution of the binder's declared Origins.
    internal ReadOnlySpan<BoundOrigin> Origins => origins;

    // The input Origins in parameter-slot order.
    internal ReadOnlySpan<BoundOrigin> Inputs => inputs;

    // Binds nothing: the binder's own body.
    internal bool IsIdentity => self is null && declaring is null && types.Length + origins.Length + inputs.Length == 0;
}
