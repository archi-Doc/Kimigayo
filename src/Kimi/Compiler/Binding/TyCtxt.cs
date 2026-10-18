// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns constructed Type and substitution interning and pure Type operations.
// Input: immutable Type shapes and substitutions. Output: canonical Types, substitutions and derived views.
// Interned identity is equality; values live with the compilation and survive warm rebinds.
// Must not resolve source expressions or let consumers duplicate Type construction.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The Type context of a compilation (<c>Compilation.Types</c>): it interns every constructed Type and every
/// substitution, so identity is equality for both, forms the Types the MIR builder needs and applies a substitution. It lives as
/// long as its Binding and its interning is never cleared, since it holds only immutable hash-consed values; a view of a pass's
/// contracts (Retains) is discarded with that pass.</summary>
internal sealed partial class TyCtxt(Binding binding, KimiLibrary library)
{
    private readonly Dictionary<int, List<BoundType>> types = new();
    private readonly Dictionary<int, List<Subst>> substs = new();

    // Compose's bindings before interning, reused.
    private BoundType?[] composedTypes = [];
    private BoundLength?[] composedLengths = [];
    private BoundOrigin[] composedOrigins = [];

    // The lengths of a substitution whose binder has no length slot; never written, so every entry is null.
    private BoundLength?[] noLengths = [];

    // SPEC 3.4.3, 13.5.5: the layer selection follows implicitly at a Type, a Deref of a reference or object handle or a PairLayer of a
    // pair layer, with the Type below it and the pair binder whose Semantics case decides the PairLayer (its SemanticsTarget, the
    // PairBinder.Target); Value, below which is the Type itself, for any other Type.
    internal static HirPlaceKind Layer(BoundType? type, out BoundType? below, out BindingSymbol? binder)
    {
        binder = null;
        if (type is { Kind: BoundTypeKind.Semantics, Components: [var referent], Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc or SemanticsKind.ObjRef or SemanticsKind.ObjUniq })
        {
            below = referent;
            return HirPlaceKind.Deref;
        }

        if (Binding.TryPairLayer(type, out _, out var target))
        {
            (below, binder) = (target, type!.Symbol);
            return HirPlaceKind.PairLayer;
        }

        below = type;
        return HirPlaceKind.Value;
    }

    // SPEC 23.1: a Type as diagnostics spell it, which the MIR dump writes.
    internal static string Name(BoundType type) => Binding.DiagnosticTypeName(type);

    // SPEC 7.3, 6.3.2: a function's parameter Type at a slot as it declares it (an accessor's self and value included), or at -1 the Type
    // its call gives (a constructor's: its object), under a call's substitution; null past its parameters or where the substitution does
    // not instantiate it. The MIR validator checks calls and bodies by it.
    internal static BoundType? Signature(BindingSymbol function, Subst? subst, int slot, out int count)
    {
        var declaration = (FunctionKoto)function.Declaration;
        count = declaration.Parameters.Count;
        var type = slot >= count ? null : slot >= 0 ? CallPlan.Parameter(function, slot)!.Type : declaration.IsConstructor ? AdtDef.ReceiverType(declaration) : function.Type;
        return type is null || subst is null ? type : declaration.CodeContext.Compilation.Types.Apply(type, subst);
    }

    // SPEC 8.2, 8.4: a Type in a definition's body as its premises make it (S for E under `E is S`, T.Element under `T.Element is i32`), as
    // Binding compares Types there.
    internal static BoundType Premised(BindingSymbol definition, BoundType type)
    {
        var binding = definition.Declaration.CodeContext.Compilation.Binding;
        return binding.CallableContractType(type, binding.ConstraintScope(definition.Declaration));
    }

    // SPEC 8.14, 13.5.3: the operation an explicit adaptation from a complete Type to a complete Type is, Binding's one selection, by which
    // the MIR's case views resolve a case adaptation.
    internal static ConversionBinding Adaptation(BoundType source, BoundType target, bool shorthand)
        => ExplicitAdaptationPlan.Select(source, target, shorthand);

    // SPEC 8.15: the complete Type a Type takes in one Semantics case, each pair layer of a binder the cases name the Type of its Semantics
    // over its direct target, which the MIR's case views give every Type by.
    internal BoundType CaseType(BoundType type, ReadOnlySpan<PairCase> cases) => binding.CaseType(type, cases);

    internal BoundType Intern(BoundTypeKind kind, BindingSymbol? symbol, SemanticsKind semantics, ReadOnlySpan<BoundType> components, long length = 0, BoundOrigin? origin = null, ReadOnlySpan<BoundOrigin> originArguments = default, BoundLength? lengthExpression = null, Subst? closureContext = null, ReadOnlySpan<BoundLength?> lengthArguments = default)
    {
        if (kind == BoundTypeKind.SemanticsAdaptation && components.Length > 0)
        {
            var same = true;
            for (var i = 1; i < components.Length; i++)
            {
                same &= ReferenceEquals(components[0], components[i]);
            }

            if (same)
            {
                return components[0];
            }
        }

        // SPEC 3.1.1: Wrapping<T> over an integer Type is the interned wrapping Scalar of that Type, identified by Core
        // and never represented as the declared struct; over a Type parameter it stays constructed until substitution.
        if (symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping && components.Length == 1 && components[0].IsInteger &&
            semantics == SemanticsKind.Owner && origin is null && originArguments.Length == 0)
        {
            return BoundType.WrappingOf(components[0]);
        }

        // SPEC 4.5: Array<T> is the compiler-managed owning dynamic sequence behind the library declaration.
        if (kind == BoundTypeKind.Array || (symbol is not null && ReferenceEquals(symbol.Declaration, library.DynamicArray.Declaration)))
        {
            kind = BoundTypeKind.Array;
            symbol = library.DynamicArray.Declaration.BoundSymbol ?? library.DynamicArray;
        }

        if (symbol?.LibraryDeclaration == KimiDeclarationId.Dictionary)
        {
            kind = BoundTypeKind.Dictionary;
        }

        if (kind == BoundTypeKind.Slice || (symbol is not null && ReferenceEquals(symbol.Declaration, library.Slice.Declaration)))
        {
            kind = BoundTypeKind.Slice;
            symbol = library.Slice.Declaration.BoundSymbol ?? library.Slice;
            if (originArguments.Length == 1)
            {
                origin ??= originArguments[0];
                originArguments = default;
            }
        }

        var hash = default(HashCode);
        hash.Add(kind);
        hash.Add(closureContext is null ? 0 : RuntimeHelpers.GetHashCode(closureContext));
        hash.Add(symbol is null ? 0 : RuntimeHelpers.GetHashCode(symbol));
        hash.Add(semantics);
        hash.Add(length);
        hash.Add(lengthExpression is null ? 0 : RuntimeHelpers.GetHashCode(lengthExpression));
        foreach (var argument in lengthArguments)
        {
            hash.Add(argument is null ? 0 : RuntimeHelpers.GetHashCode(argument));
        }

        hash.Add(origin is null ? 0 : RuntimeHelpers.GetHashCode(origin));
        for (var i = 0; i < originArguments.Length; i++)
        {
            hash.Add(RuntimeHelpers.GetHashCode(originArguments[i]));
        }

        for (var i = 0; i < components.Length; i++)
        {
            hash.Add(RuntimeHelpers.GetHashCode(components[i]));
        }

        var key = hash.ToHashCode();
        if (this.types.TryGetValue(key, out var bucket))
        {
            for (var i = 0; i < bucket.Count; i++)
            {
                var type = bucket[i];
                if (type.Kind != kind || type.Symbol != symbol || !ReferenceEquals(type.ClosureContext, closureContext) || type.Semantics != semantics || type.Length != length || !ReferenceEquals(type.LengthExpression, lengthExpression) || !lengthArguments.SequenceEqual(type.LengthArguments) || type.Components.Count != components.Length || !ReferenceEquals(type.Origin, origin) || type.OriginArguments.Count != originArguments.Length)
                {
                    continue;
                }

                var equal = true;
                for (var j = 0; j < components.Length; j++)
                {
                    equal &= ReferenceEquals(type.Components[j], components[j]);
                }

                for (var j = 0; j < originArguments.Length; j++)
                {
                    equal &= ReferenceEquals(type.OriginArguments[j], originArguments[j]);
                }

                if (equal)
                {
                    return type;
                }
            }
        }
        else
        {
            this.types.Add(key, bucket = new(1));
        }

        var created = new BoundType(symbol?.Name ?? kind.ToString(), kind, symbol, semantics, components.ToArray(), length, origin, originArguments.ToArray(), lengthExpression, lengthArguments.ToArray()) { ClosureContext = closureContext };
        bucket.Add(created);
        return created;
    }

    // SPEC 3.4: the reference of a Semantics kind (ref, uniq, objref, objuniq) to a referent; a null Origin is the ownership analysis's to infer.
    internal BoundType Reference(SemanticsKind semantics, BoundType referent, BoundOrigin? origin = null) => this.Intern(BoundTypeKind.Semantics, null, semantics, [referent], origin: origin);

    // SPEC 5.2: the raw pointer to a pointee; a raw pointer carries no Origin.
    internal BoundType Pointer(BoundType referent) => this.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Raw, [referent]);

    internal BoundType Tuple(ReadOnlySpan<BoundType> elements) => this.Intern(BoundTypeKind.Tuple, null, SemanticsKind.Owner, elements);

    // SPEC 8.10, 10.5: a selected call's substitution, as its selection committed it; a value call has none. A direct base call's is its
    // implementation's in the Type implementing it, which it calls (virtual-dispatch 4).
    internal Subst? SubstOf(CallPlan call) => call.Kind == CalleeKind.Value ? null
        : call is { Kind: CalleeKind.Virtual, VirtualIsDirect: true, VirtualImplementation.BoundSymbol: { } implementation }
            ? this.Intern(implementation, null, call.VirtualImplementingType, call.TypeArguments, call.LengthArguments, call.Origins, call.InputOrigins)
            : this.Intern(call.Target, call.ConformingType, call.DeclaringType ?? BoundContract(call), call.TypeArguments, call.LengthArguments, call.Origins, call.InputOrigins);

    // SPEC 8.2: a closure's substitution, the context of the instance that closed it; a closure no instance closed is in the identity of
    // the declaration enclosing it.
    internal Subst? SubstOfClosure(BoundType closure)
    {
        if (closure.Symbol?.Declaration is not FunctionKoto { BoundClosure: not null } binder)
        {
            return null;
        }

        while (binder is { IsAnonymous: true } && binder.BoundSymbol?.Scope.Function is { } enclosing)
        {
            binder = enclosing;
        }

        return closure.ClosureContext ?? this.Identity(binder);
    }

    // SPEC 8.2: the context of a closure an instance of its enclosing function closes, that instance's substitution (DECISIONS R5-M36), as
    // its call's would be: one interned value per instance, which all of the instance's closures share.
    internal Subst ClosureContext(
        BindingSymbol enclosing,
        BoundType? declaring,
        ReadOnlySpan<BoundType?> types,
        ReadOnlySpan<BoundLength?> lengths,
        ReadOnlySpan<BoundOrigin> origins,
        ReadOnlySpan<BoundOrigin> inputs)
        => this.Intern(enclosing, null, declaring, types, lengths, origins, inputs);

    // SPEC 7.3: a function item's substitution, the context its Item Type binds; an Item keeps its per-call Origins in place.
    internal Subst? SubstOfItem(BoundType item) => binding.FunctionItemContext(item, false) is { } context ? this.SubstOf(context) : null;

    // The substitution that binds none of a binder's slots: its own body's.
    internal Subst Identity(Koto binder) => this.Intern(binder.BoundSymbol!, null, null, [], [], [], []);

    // A call the MIR builder synthesizes, of the function serving a built-in form (R3 decision 5): its declaring Type's instance and its Type
    // arguments; its Origins are the analyses' to relate.
    internal Subst Synthesized(BindingSymbol target, BoundType? declaring, ReadOnlySpan<BoundType?> types) => this.Intern(target, null, declaring, types, [], [], []);

    // SPEC 8.2, 8.10: a declared Type in the instance a substitution makes, which Binding's storage instantiation computes until R7 moves it
    // here; the identity keeps the Type.
    internal BoundType? Apply(BoundType type, Subst subst)
        => subst.IsIdentity ? type : binding.InstantiateStorageType(type, subst.Target, subst.Declaring, subst.Types, subst.Lengths, subst.Origins, subst.Inputs, subst.Self);

    // Plan 5.6.2: the substitution of a call in a body that another substitution instantiates, as a call in a default template folds under
    // the substitution of the call omitting the default: each binding of the inner one in the outer instance, as Binding forwards a call of
    // a generic body (InstantiateForwardedCall), so Apply(t, Compose(inner, outer)) is that call's instantiation of t. Null when the outer
    // instance does not substitute an inner Type or the declaring Type.
    internal Subst? Compose(Subst inner, Subst outer)
    {
        if (outer.IsIdentity)
        {
            return inner;
        }

        var types = Fit(ref this.composedTypes, inner.Types.Length);
        var lengths = Fit(ref this.composedLengths, inner.Lengths.Length);
        var origins = Fit(ref this.composedOrigins, inner.Origins.Length + inner.Inputs.Length);
        var declaring = inner.Declaring is { } owner ? this.Apply(owner, outer) : null;
        var failed = declaring is null && inner.Declaring is not null;
        for (var i = 0; i < types.Length; i++)
        {
            types[i] = inner.Types[i] is { } type ? this.Apply(type, outer) : null;
            failed |= types[i] is null && inner.Types[i] is not null;
        }

        for (var i = 0; i < lengths.Length; i++)
        {
            lengths[i] = inner.Lengths[i] is { } length ? binding.SubstituteLength(length, outer.Binder, outer.Lengths) : null;
            failed |= lengths[i] is null && inner.Lengths[i] is not null;
        }

        for (var i = 0; i < origins.Length; i++)
        {
            origins[i] = (i < inner.Origins.Length ? inner.Origins[i] : inner.Inputs[i - inner.Origins.Length]) is { } origin
                ? binding.ForwardOrigin(origin, outer.Target, outer.Declaring, outer.Origins, outer.Inputs, outer.Types) : null!;
        }

        return failed ? null : this.Intern(inner.Target, inner.Self is { } self ? this.Apply(self, outer) : null, declaring, types, lengths, origins[..inner.Origins.Length], origins[inner.Origins.Length..]);
    }

    // SPEC 8.4.3: the bound Contract reference a requirement call dispatches through (Indexable<isize>), which declares the requirement as a
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
