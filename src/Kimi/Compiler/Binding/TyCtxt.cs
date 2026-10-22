// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns constructed Type interning and pure Type operations, and the lifetime of TyCtxt's views (ResetViews); substitutions are
// TyCtxt.Substitutions.cs's.
// Input: immutable Type shapes. Output: canonical Types and derived views.
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

    // A Type by its parts, one instance per parts. `origin` is the layer's stated Origin, whose role the Semantics decides: a raw layer's raw
    // Origin (BoundType.RawOrigin; Pointer and Rebuild write it), any other layer's Origin (DECISIONS BU-24).
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
                if (type.Kind != kind || type.Symbol != symbol || !ReferenceEquals(type.ClosureContext, closureContext) || type.Semantics != semantics || type.Length != length || !ReferenceEquals(type.LengthExpression, lengthExpression) || !lengthArguments.SequenceEqual(type.LengthArguments) || type.Components.Count != components.Length || !ReferenceEquals(type.Origin ?? type.RawOrigin, origin) || type.OriginArguments.Count != originArguments.Length)
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

    // SPEC 5.2, 5.7: the raw pointer to a pointee, stating `rawOrigin` (null: unstated).
    internal BoundType Pointer(BoundType referent, BoundOrigin? rawOrigin = null)
        => this.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Raw, [referent], origin: rawOrigin);

    // SPEC 5.7 (design-final §7.3, §13 A5; DECISIONS BU-24): A5's view of a raw layer, any MIR holds (its pointer temporaries state none,
    // a default template's inline copies their substituted Origins): what reaching storage through it is, by the borrow its Origin
    // belongs to. Total over published Types; A5 reads no Origin kind itself. A layer an instance stores is read by the overload below.
    internal RawStep RawStep(BoundType raw) => binding.PublishedStep(raw.RawOrigin);

    // SPEC 5.7 (design-final §15, R4's conditions; DECISIONS BU-24): the step of a raw layer an instance stores, the one reader of one:
    // a struct's Field `part`, or an enum's Case `part` whose first position selects the payload's component, then the inline positions
    // (a Tuple's component, a fixed array's element). It reads the declared Field Type or payload under the instance's substitution,
    // `self` the holding value's own step; A5 never reads an instance's FieldType or CaseTypes for a raw Origin (they state none) nor
    // substitutes one itself. Cost: one substitution of the declared part per call, interned, allocating nothing when warm. Total over
    // what Binding accepts: an Origin without a step (an Unbound slot argument) gives Unstated, never a fault, and an erased instance
    // answers with its erased arguments (`static`, as the user's `during static`); a path that ends at no raw layer is a broken contract.
    internal RawStep RawStep(BoundType instance, int part, ReadOnlySpan<int> positions) => binding.StoredStep(instance, part, positions);

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
            type.ClosureContext,
            type.LengthArguments);

    internal BoundType Tuple(ReadOnlySpan<BoundType> elements) => this.Intern(BoundTypeKind.Tuple, null, SemanticsKind.Owner, elements);

    // R4-M51: every view's memos, which hold across passes with no edit (retentions, instance classes, requirement targets), discarded
    // together at an edit (Binding.Invalidate; DECISIONS R5-M61).
    internal void ResetViews()
    {
        this.ResetRetentions();
        this.ResetInstances();
        this.ResetDispatch();
    }
}
