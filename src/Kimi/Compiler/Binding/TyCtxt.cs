// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns constructed Type interning and pure Type operations, among them a built-in Core's stored content (CoreContent), an Integer
// constant's one encoding (Normalize, DECISIONS BU-133), a safe reference's stored representation (IsStoredReference) and two Types'
// storage agreement with their Origins erased (StorageMatches), and the lifetime of TyCtxt's views (ResetViews); substitutions are
// TyCtxt.Substitutions.cs's, the raw-layer views TyCtxt.Raw.cs's, the values of the interpolation terms (SPEC 22.10.5's bounds and rule L's stack limit: FormatBound, StackBound, HintBound) TyCtxt.Formatting.cs's,
// and R4-M118's chain judgment of one Origin graph, a pure function family A5 and the declaration judge read (OriginChains),
// TyCtxt.OriginChains.cs's.
// Input: immutable Type shapes and the compilation's Binding, whose facts its members read. Output: canonical Types and derived views.
// Interned identity is equality, over two levels (PLAN G949 Stage 2 U4; Binding.PassPart.Levels): the library layer's frozen level, filled
// until the library's seal, and the compilation's, filled after it; values live with the compilation and survive warm rebinds. A
// compilation-level Type's id is above L (LibraryLayer.TypeCount), from which the numbering goes on. No instance or Item states a raw Origin on
// a pair slot's argument: Intern binds each by PairBinding (SPEC 8.1.1; DECISIONS BU-34), but Unstated in kept parts, so there one SPEC
// Type is two interned Types, a declaration's (`PS<raw/u8 during Unstated>`) and a parameter's (`PS<raw/u8>`) (DECISIONS BU-46).
// Must not resolve source expressions or let consumers duplicate Type construction: every Type but BoundType's statics is made and
// numbered here (Intern, Parameter; the dense id, DECISIONS BU-220).

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The Type context of a compilation (<c>Compilation.Types</c>): it interns every constructed Type and every
/// substitution, so identity is equality for both, forms the Types the MIR builder needs and applies a substitution. It lives as
/// long as its Binding and its interning is never cleared, since it holds only immutable hash-consed values; a view of a pass's
/// contracts (Retains) is discarded with that pass.</summary>
internal sealed partial class TyCtxt(Binding binding, KimiLibrary library)
{
    // The compilation's level of the interned Types (PLAN G949 Stage 2 U4): the Types interned after the library's seal; the library's
    // are LibraryLayer.Types, found first (Intern).
    private readonly Dictionary<int, List<BoundType>> types = new();

    // DECISIONS BU-220: the last id this compilation gave a Type (Intern, Parameter), numbering on from the library's L once its layer is
    // sealed (U4), so every Type of the compilation's level has an id above L.
    private int typeCount = Math.Max(BoundType.StaticCount, library.Layer.TypeCount);

    /// <summary>Gets the last id this compilation gave a Type (Binding's freeze reads it as the library's L).</summary>
    internal int TypeCount => this.typeCount;

    // The Components PairBindings binds, reused by every Intern (nothing interned while it is filled reaches PairBindings).
    private BoundType[] pairScratch = [];

    // SPEC 3.1.2 (DECISIONS R4-M154, r2.2): the stored content of a built-in Core Type, whose layout is hidden, the one fact such a Type
    // publishes to retention's walks (Binding.Holdings, read for the positions and the sources alike): a `string`'s value holds its text's
    // bytes, elements of Type `u8`, by value, as a fixed array holds its elements; every other Core holds none (null). Never "holds any".
    internal static BoundType? CoreContent(BoundType type) => ReferenceEquals(type, BoundType.String) ? BoundType.Primitives["u8"] : null;

    // DECISIONS BU-133: an Integer constant's one encoding, its Type's N-bit payload sign-extended, also for an unsigned language Type,
    // which Binding writes (a literal's fit, a folded conversion) and MirValidator checks. Width 0 is for an integer or wrapping integer
    // Type whose instance decides its width (a proven-PrimitiveInteger parameter or projection, Wrapping<T> over one; SPEC 8.7.4), and
    // for no other widthless Type: it keeps the 128-bit payload, the widest instance's, whose low N bits are each instance's.
    internal static Int128 Normalize(Int128 bits, int width) => width is > 0 and <= 64
        ? (unchecked((long)bits) << (64 - width)) >> (64 - width) : bits;

    // A safe reference (BoundType.SafeReferent) whose referent has a stored reference representation: a struct, enum, fixed array, Tuple,
    // string or Unit; a borrowed Array or Slice, a reference to its handle storage (SPEC 4.5, 4.8.1; a Slice's elements keep their own
    // source Loan); a safe layer below; an object handle or borrow; a callable; an associated projection, a pair target or a Semantics
    // application, which stands for a complete Type like a parameter, each instance checking its substitution (SPEC 8.5, 8.1.1); a Scalar
    // or a raw pointer (Binding verifies Origin obligations; inferred initializer aliases use the same representation).
    internal static bool IsStoredReference(BoundType? type) => type?.SafeReferent is { } referent &&
        (AdtDef.IsStruct(referent) || AdtDef.IsEnum(referent) || ReferenceEquals(referent, BoundType.String) || ReferenceEquals(referent, BoundType.Unit) ||
            referent.SafeReferent is not null || referent.HandleMode is not null || referent.IsObjectBorrow ||
            referent.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Tuple or BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Slice or BoundTypeKind.Closure or
                BoundTypeKind.Function or BoundTypeKind.FunctionItem or BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or
                BoundTypeKind.SemanticsApplication ||
            referent.IsScalar || referent.IsPointer);

    // Physical agreement of a call's formal and actual Types: Kind, Symbol, Semantics, lengths and components. Origins are erased:
    // Binding and ownership analysis verified them, and lowering never distinguishes two storages by Origin (SPEC 21.3.1).
    internal static bool StorageMatches(BoundType? formal, BoundType? actual)
    {
        if (formal is null || actual is null)
        {
            return false;
        }

        if (ReferenceEquals(formal, actual))
        {
            return true;
        }

        // Primitive Types are singletons distinguished by name only, so two different instances never share storage.
        if (formal.Kind == BoundTypeKind.Primitive || formal.Kind != actual.Kind || formal.Symbol != actual.Symbol || formal.Semantics != actual.Semantics ||
            formal.Length != actual.Length || !ReferenceEquals(formal.LengthExpression, actual.LengthExpression) ||
            !formal.LengthArguments.AsSpan().SequenceEqual(actual.LengthArguments) || formal.Components.Count != actual.Components.Count)
        {
            return false;
        }

        for (var i = 0; i < formal.Components.Count; i++)
        {
            if (!StorageMatches(formal.Components[i], actual.Components[i]))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 13.5.5, 8.15.2: the pair binder whose Semantics case decides a Type's case Type at its outer layer (its SemanticsTarget, the
    // PairBinder.Target): the original `s/T`, an application `s/U` and an adaptation family `p@s` (Binding.TryAdaptationSelector); null for
    // any other Type. MIR's one binder fact: the Semantics-case views (MirCases.Binder, so a family's call input reserves, A3 E13 (4)),
    // the validator's PairAcquire and PairLayer checks (MirBody.ProjectedType), MirTypes.IsReference (an EndBorrow ends a family's
    // borrow; A5's Borrows and Storing rows count it). Layer's PairLayer is the original's and an application's, whose one direct
    // target a follow selects.
    internal static BindingSymbol? PairBinder(BoundType? type) => type is not null && Binding.TryAdaptationSelector(type, out _) ? type.Symbol : null;

    // SPEC 3.4.3, 13.5.5: the layer selection follows implicitly at a Type, a Deref of a reference or object handle or a PairLayer of a
    // pair layer, with the Type below it and, for a PairLayer, its pair binder (PairBinder's fact); Value, below which is the Type
    // itself, for any other Type.
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
            (below, binder) = (target, PairBinder(type));
            return HirPlaceKind.PairLayer;
        }

        below = type;
        return HirPlaceKind.Value;
    }

    // SPEC 23.1: a Type as diagnostics spell it, which the MIR dump writes.
    internal static string Name(BoundType type) => Binding.DiagnosticTypeName(type);

    // SPEC 23.1: a length as diagnostics spell it, which the MIR dump writes.
    internal static string Name(BoundLength length) => Binding.DiagnosticLengthName(length);

    // SPEC 8.14, 13.5.3: the operation an explicit adaptation from a complete Type to a complete Type is, Binding's one selection, by which
    // the MIR's case views resolve a case adaptation.
    internal static ConversionBinding Adaptation(BoundType source, BoundType target, bool shorthand)
        => ExplicitAdaptationPlan.Select(source, target, shorthand);

    // SPEC 8.2, 8.4: a Type in a definition's body as its premises make it (S for E under `E is S`, T.Element under `T.Element is i32`), as
    // Binding compares Types there: the one premising (Binding.CallableContractType), which Binding.OperandTerminal applies too.
    internal BoundType Premised(BindingSymbol definition, BoundType type)
        => binding.CallableContractType(type, binding.ConstraintScope(definition.Declaration));

    // SPEC 7.3, 6.3.2: a function's parameter Type at a slot as it declares it (an accessor's self and value included), or at -1 the Type
    // its call gives (a constructor's: its object), under a call's substitution, by the substitution alone (Apply: its storage unread);
    // null past its parameters or where the substitution does not form it. The MIR validator checks calls and bodies by it; the MIR
    // builder, code generation and the instance collector's property-witness agreement read it.
    internal BoundType? Signature(BindingSymbol function, Subst? subst, int slot, out int count)
    {
        var declaration = (FunctionKoto)function.Declaration;
        count = declaration.Parameters.Count;
        var type = slot >= count ? null : slot >= 0 ? CallPlan.Parameter(binding, function, slot)!.Type : declaration.IsConstructor ? AdtDef.ReceiverType(binding, declaration) : function.Type;
        return type is null || subst is null ? type : this.Apply(type, subst).Type;
    }

    // SPEC 15.6.4, 5.7 Calls, 8.2.3 (PLAN G823): a called function's parameter key: the root key of `input` (the slot's instantiated Type
    // as the caller reads it) on the path of the Type the function's declaration writes at `slot` (Signature without the substitution), so
    // that OwnLayers there, and at every key Select, Base, Leading and OwnLayers give past it, marks as Hidden each part whose declared
    // counterpart, in the function's view (its identity classes at every part, Binding.TypeIdentity), is an abstract Type of the function's
    // own declaration (a Type parameter, or a pair target or projection rooted at one). Shown: a part an identity premise gives a form
    // (`S.Item is raw/u8` inside `HA<S>`), an enclosing Type's parameter, which the receiver's instance completes (DECISIONS BU-50); a Type
    // the declaration and the input write with different constructors ends the path there, shown (named limit: a pair layer's owner case,
    // `s/U` read as U itself). Must not decide what a hidden layer reaches (A5's).
    internal PlaceKey ParameterKey(BindingSymbol function, int slot, BoundType input)
        => PlaceKey.Root(input) with { Signature = this.Signature(function, null, slot, out _) };

    // SPEC 8.15: the complete Type a Type takes in one Semantics case, each pair layer of a binder the cases name the Type of its Semantics
    // over its direct target, which the MIR's case views give every Type by.
    internal BoundType CaseType(BoundType type, ReadOnlySpan<PairCase> cases) => binding.CaseType(type, cases);

    // A Type parameter's own Type, which no interning shares: a generic parameter's whole Type and a pair parameter's target
    // projection (Binding.Indexing), a Contract's Self (Binding.ContractSelfType); each is made once and kept on its symbol.
    internal BoundType Parameter(string name, BoundTypeKind kind, BindingSymbol symbol) => new(++this.typeCount, name, kind, symbol);

    // A Type by its parts, one instance per parts. `origin` is the layer's stated Origin, whose role the Semantics decides: a raw layer's raw
    // Origin (BoundType.RawOrigin; Pointer and Rebuild write it), any other layer's Origin (DECISIONS BU-24).
    internal BoundType Intern(BoundTypeKind kind, BindingSymbol? symbol, SemanticsKind semantics, ReadOnlySpan<BoundType> components, long length = 0, BoundOrigin? origin = null, ReadOnlySpan<BoundOrigin> originArguments = default, BoundLength? lengthExpression = null, Subst? instanceContext = null, ReadOnlySpan<BoundLength?> lengthArguments = default, Koto? binder = null)
    {
        // SPEC 8.10: a Function Type keeps its per-call binder only while it quantifies one of its direct inputs, so equal Types
        // intern to one instance whichever path formed them.
        if (binder is not null && !(kind == BoundTypeKind.Function && components.Length == 2 && QuantifiesInput(components[0], binder)))
        {
            binder = null;
        }

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

        // SPEC 3.1.3, 22.1: the Kimi declaration `Never` is the compiler-intrinsic uninhabited Type, never represented as the
        // declared struct: every written form that lookup resolves to the declaration forms this one instance.
        if (symbol?.LibraryDeclaration == KimiDeclarationId.Never)
        {
            return BoundType.Never;
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

        if (kind is BoundTypeKind.Constructed or BoundTypeKind.FunctionItem && AnyCarriesRawOrigin(components))
        {
            components = this.PairBindings(kind, symbol, components);
        }

        var hash = default(HashCode);
        hash.Add(kind);
        hash.Add(instanceContext is null ? 0 : RuntimeHelpers.GetHashCode(instanceContext));
        hash.Add(binder is null ? 0 : RuntimeHelpers.GetHashCode(binder));
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

        // The one find over the two levels (Binding.PassPart.Levels; PLAN G949 Stage 2 U4): content in no level is inserted at the open
        // one, so equal content is one Type in both levels.
        var key = hash.ToHashCode();
        var levels = Binding.PassPart.Interned(library, library.Layer.Types, this.types);
        foreach (var level in levels)
        {
            if (!level.TryGetValue(key, out var bucket))
            {
                continue;
            }

            for (var i = 0; i < bucket.Count; i++)
            {
                var type = bucket[i];
                if (type.Kind != kind || type.Symbol != symbol || !ReferenceEquals(type.InstanceContext, instanceContext) || !ReferenceEquals(type.Binder, binder) || type.Semantics != semantics || type.Length != length || !ReferenceEquals(type.LengthExpression, lengthExpression) || !lengthArguments.SequenceEqual(type.LengthArguments) || type.Components.Count != components.Length || !ReferenceEquals(type.Origin ?? type.RawOrigin, origin) || type.OriginArguments.Count != originArguments.Length)
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

        var open = levels.Open;
        if (!open.TryGetValue(key, out var into))
        {
            open.Add(key, into = new(1));
        }

        var created = new BoundType(++this.typeCount, symbol?.Name ?? kind.ToString(), kind, symbol, semantics, components.ToArray(), length, origin, originArguments.ToArray(), lengthExpression, lengthArguments.ToArray(), instanceContext, binder);
        into.Add(created);
        return created;

        static bool QuantifiesInput(BoundType inputs, Koto binder)
        {
            for (var i = 0; i < inputs.Components.Count; i++)
            {
                if (inputs.Components[i].Origin is { Kind: OriginKind.Input, Occurrence: null } origin && ReferenceEquals(origin.Binder, binder) && origin.Slot == i)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // SPEC 3.4: the reference of a Semantics kind (ref, uniq, objref, objuniq) to a referent; a null Origin is the ownership analysis's to infer.
    internal BoundType Reference(SemanticsKind semantics, BoundType referent, BoundOrigin? origin = null) => this.Intern(BoundTypeKind.Semantics, null, semantics, [referent], origin: origin);

    // SPEC 5.2, 5.7: the raw pointer to a pointee, stating `rawOrigin` (null: unstated).
    internal BoundType Pointer(BoundType referent, BoundOrigin? rawOrigin = null)
        => this.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Raw, [referent], origin: rawOrigin);

    // SPEC 8.1.1, 8.1.2: the binding a pair slot holds for a Type argument, which the slot's original `s/T` denotes and from which `s/U`
    // is formed: a raw layer states no Origin in a pair (`raw/T during o` fits by dropping it) but Unstated, a kept part's mark of a layer
    // its declaration writes (DECISIONS BU-46; the one exception, so the mark follows the binding); any other argument is itself, its
    // components' Origins kept.
    internal BoundType PairBinding(BoundType whole)
        => whole is { IsPointer: true, RawOrigin: not (null or { Kind: OriginKind.Unstated }) }
            ? this.Pointer(whole.Components[0]) : whole;

    // The same Type with these components, Origin and Origin arguments, every other part kept, a raw layer's Origin included: the one form
    // of a rebuild that keeps a Type's shape (DECISIONS BU-24). A raw layer takes no `origin` (its role's getter gives none).
    internal BoundType Rebuild(BoundType type, ReadOnlySpan<BoundType> components, BoundOrigin? origin, ReadOnlySpan<BoundOrigin> originArguments)
        => this.Intern(
            type.Kind,
            type.Symbol,
            type.Semantics,
            components,
            type.Length,
            origin ?? type.RawOrigin,
            originArguments,
            type.LengthExpression,
            type.InstanceContext,
            type.LengthArguments,
            type.Binder);

    internal BoundType Tuple(ReadOnlySpan<BoundType> elements) => this.Intern(BoundTypeKind.Tuple, null, SemanticsKind.Owner, elements);

    // SPEC 22.10.5 and rule L (StackLimit): the interpolation-bound term of a direct interpolation with these value Types
    // (a Tuple) and literal bytes, its stack region's size: the constant StackBound decides, else the term (Term), which a
    // substitution folds again (Refold). R8-W2 a2's builder forms it.
    internal BoundLength InterpolationBound(BoundType values, long literal) => this.Term(LengthOperation.InterpolationBound, values, literal);

    // SPEC 22.10.5 (R8-W2 c2t): the interpolation-hint term of an owning interpolation's write position with the later value Types (a
    // Tuple) and the later literal bytes, the bytes its adapter's later writes may still reserve: the constant HintBound
    // decides, else the term (Term), which a substitution folds again (Refold). R8-W2 c2b's builder forms it; until then nothing does.
    internal BoundLength InterpolationHint(BoundType values, long literal) => this.Term(LengthOperation.InterpolationHint, values, literal);

    // An interpolation term over its value Types' image, as a substitution or an instance makes them (Binding.SubstituteLength,
    // Binding.InstanceLength): the term of its own operation and literal bytes, folded again.
    internal BoundLength Refold(BoundLength term, BoundType values) => this.Term(term.Operation, values, term.Value);

    // SPEC 4.4.1: a length slot's term, the value its Name reads (MirConstKind.Length), as Binding binds the slot in a length.
    internal BoundLength SlotLength(BindingSymbol slot) => binding.InternLength(LengthOperation.Parameter, parameter: slot);

    // R4-M51: every view's memos, which hold across passes with no edit (retentions, instance classes, requirement targets, Copy facts),
    // discarded together at an edit (Binding.Invalidate; DECISIONS R5-M61, BU-88).
    internal void ResetViews()
    {
        this.ResetRetentions();
        this.ResetInstances();
        this.ResetDispatch();
        this.copies.Clear();
        this.topLevelCopies = null;
    }

    // Intern's guard of PairBindings: whether a Component's subtree states a raw Origin (BoundType.CarriesRawOrigin), so any other instance
    // or Item pays one summary bit per Component, inlined in Binding's hottest path.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AnyCarriesRawOrigin(ReadOnlySpan<BoundType> components)
    {
        var carries = false;
        for (var i = 0; i < components.Length; i++)
        {
            carries |= components[i].CarriesRawOrigin;
        }

        return carries;
    }

    // SPEC 8.1.1 (this file's invariant): an instance's (Constructed) or Item's (FunctionItem) Components, some carrying a raw Origin, with
    // each pair slot's argument its PairBinding, in the reused scratch when one changes, else `components`. A Constructed's Component i
    // holds its schema's slot i (an enclosing declaration's slots included), an Item's the Component Binding.ItemComponent gives.
    // PairBinding interns a Semantics layer, which never comes here.
    private ReadOnlySpan<BoundType> PairBindings(BoundTypeKind kind, BindingSymbol? symbol, ReadOnlySpan<BoundType> components)
    {
        if (symbol?.Schema is not { } schema)
        {
            return components;
        }

        var bound = Span<BoundType>.Empty;
        for (var slot = 0; slot < schema.GenericSlots.Count; slot++)
        {
            if (schema.GenericSlots[slot].Kind != GenericSlotKind.Pair)
            {
                continue;
            }

            var at = kind == BoundTypeKind.Constructed ? slot : Binding.ItemComponent((FunctionKoto)symbol.Declaration, slot);
            if (at < components.Length && this.PairBinding(components[at]) is var binding && !ReferenceEquals(binding, components[at]))
            {
                if (bound.IsEmpty)
                {
                    this.pairScratch = this.pairScratch.Length < components.Length ? new BoundType[components.Length] : this.pairScratch;
                    bound = this.pairScratch.AsSpan(0, components.Length);
                    components.CopyTo(bound);
                }

                bound[at] = binding;
            }
        }

        return bound.IsEmpty ? components : bound;
    }

    // The one fold and interning of an interpolation term: the constant its operation's formatting rule (TyCtxt.Formatting.cs) gives,
    // else the term over the erased value Types (so Erase keeps it, keyed by them). Only the two term rows carry value Types.
    private BoundLength Term(LengthOperation operation, BoundType values, long literal)
    {
        var folded = operation switch
        {
            LengthOperation.InterpolationBound => StackBound(values, literal),
            LengthOperation.InterpolationHint => HintBound(values, literal),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        return folded >= 0 ? binding.InternLength(LengthOperation.Constant, folded) : binding.InternLength(operation, literal, values: this.Erase(values));
    }
}
