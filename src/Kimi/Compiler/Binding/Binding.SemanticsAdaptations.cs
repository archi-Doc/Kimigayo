// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // A finite Type family stores complete case Types in SemanticsOrder, packed by its Length mask.
    // Only the selecting binder is a dependency; no source expression or operation plan is retained.
    private enum FamilyTransform : byte
    {
        Adapt,
        Target,
        Origin,
        Apply,
    }

    /// <summary>Gets the pair selector of a layer or finite result-Type family.</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="whole">The selector's whole Type.</param>
    /// <returns>Whether a selector remains.</returns>
    internal static bool TryAdaptationSelector(BoundType type, out BoundType whole)
    {
        if (TryPairLayer(type, out whole, out _))
        {
            return true;
        }

        if (type.Kind == BoundTypeKind.SemanticsAdaptation && type.Symbol?.WholeType is { } selected)
        {
            whole = selected;
            return true;
        }

        whole = null!;
        return false;
    }

    /// <summary>Forms the finite family of complete result Types of a short adaptation.</summary>
    /// <param name="pair">The selector's pair target Symbol.</param>
    /// <param name="source">The independently fixed input Type.</param>
    /// <param name="origin">The Origin of a newly produced borrow.</param>
    /// <returns>An interned family, collapsed when every case is identical.</returns>
    internal BoundType SemanticsAdaptation(BindingSymbol pair, BoundType source, BoundOrigin? origin)
        => this.AdaptationFamily(pair, source, origin);

    /// <summary>Gets the possible outer Semantics of a complete Type or finite result-Type family.</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="scope">The available premises.</param>
    /// <returns>A conservative set of outer Semantics.</returns>
    internal SemanticsMask ResultSemantics(BoundType type, BindingScope scope)
    {
        if (type.Kind != BoundTypeKind.SemanticsAdaptation)
        {
            if (TryPairLayer(type, out var whole, out var target))
            {
                var admitted = this.AdmittedSemantics(whole, scope);
                // Applying owner is transparent; its result can itself be a borrow or handle.
                return type.Kind == BoundTypeKind.SemanticsApplication && (admitted & SemanticsMask.Owner) != 0
                    ? (admitted & ~SemanticsMask.Owner) | this.ResultSemantics(target, scope) : admitted;
            }

            if (AbstractTypes.IsAbstract(type))
            {
                return this.RequestCapability(type, this.Library.ObjectPayload, scope) == ConstraintProof.Proven ||
                    this.RequestCapability(type, this.Library.Sealed, scope) == ConstraintProof.Proven ||
                    this.RequestCapability(type, this.Library.PrimitiveInteger, scope) == ConstraintProof.Proven
                    ? SemanticsMask.Owner : this.AdmittedSemantics(type, scope);
            }

            return type.Semantics.ToMask();
        }

        var result = SemanticsMask.None;
        for (var i = 0; i < type.Components.Count; i++)
        {
            result |= this.ResultSemantics(type.Components[i], scope);
        }

        return result;
    }

    /// <summary>Forms a short adaptation's result after its selected Semantics is known.</summary>
    /// <param name="semantics">The selected Semantics.</param>
    /// <param name="source">The complete input Type.</param>
    /// <param name="origin">The Origin of a newly produced borrow.</param>
    /// <returns>The normalized ordinary Type or a finite family over an independent source selector.</returns>
    internal BoundType AdaptedType(SemanticsKind semantics, BoundType source, BoundOrigin? origin)
    {
        if (ReferenceEquals(source, BoundType.Never) || semantics == SemanticsKind.Owner)
        {
            return source;
        }

        if (IsObjectSemantics(semantics))
        {
            if (source.Kind == BoundTypeKind.SemanticsAdaptation)
            {
                return this.TransformFamily(source, FamilyTransform.Adapt, origin, semantics);
            }

            if (TryPairLayer(source, out var whole, out _))
            {
                return this.AdaptationFamily(whole.Symbol!, source, origin, semantics);
            }

            if (semantics == SemanticsKind.ObjRef && source.Semantics == SemanticsKind.ObjRef)
            {
                return source;
            }

            if (IsBorrow(semantics) && ObjectTypes.IsBorrow(source))
            {
                origin = source.Origin ?? origin;
            }

            if (IsObjectSemantics(source.Semantics) && source.Components.Count == 1)
            {
                source = source.Components[0];
            }
        }

        // Value borrows and addresses wrap the written slot; object targets select its View Target.
        return this.Form(semantics, source, origin);
    }

    private static BoundType? FamilyCase(BoundType family, SemanticsKind mode)
    {
        var mask = (uint)family.Length;
        var bit = (uint)mode.ToMask();
        return (mask & bit) == 0 ? null : family.Components[BitOperations.PopCount(mask & (bit - 1))];
    }

    // Short adaptations select the output mode with the input case; a fixed mode maps every input case.
    private BoundType AdaptationFamily(BindingSymbol pair, BoundType source, BoundOrigin? origin, SemanticsKind? fixedMode = null)
    {
        var admitted = this.AdmittedSemantics(pair.WholeType!, pair.Scope);
        var children = this.RentTypes(BitOperations.PopCount((uint)admitted));
        var cases = this.adaptationCaseScratch.Rent(1);
        try
        {
            var count = 0;
            foreach (var mode in SemanticsOrder)
            {
                if (admitted.Contains(mode))
                {
                    cases[0] = new(pair, mode);
                    children[count++] = this.AdaptedType(fixedMode ?? mode, this.CaseType(source, cases.AsSpan(0, 1)), origin);
                }
            }

            return this.TypeFamily(pair, admitted, children.AsSpan(0, count));
        }
        finally
        {
            this.typeScratch.Return(children, clearArray: true);
            this.adaptationCaseScratch.Return(cases, clearArray: true);
        }
    }

    // A placeholder's Owner tag is not mode evidence. A pair's owner case does establish its target's owner form.
    private bool CanSelectIdentity(BoundType type, BindingScope scope, ReadOnlySpan<PairCase> cases = default)
    {
        if (!AbstractTypes.IsAbstract(type))
        {
            return true;
        }

        if (type.Kind == BoundTypeKind.TargetProjection && type.Symbol is { Kind: BindingSymbolKind.SemanticsTarget, WholeType: { } whole } pair)
        {
            var admitted = CaseOf(cases, pair) is { } selected ? selected.ToMask() : this.AdmittedSemantics(whole, scope);
            if (admitted == SemanticsMask.Owner)
            {
                return true;
            }
        }

        return this.ResultSemantics(type, scope) == SemanticsMask.Owner;
    }

    private BoundType TypeFamily(BindingSymbol pair, SemanticsMask admitted, ReadOnlySpan<BoundType> children)
        => this.InternType(BoundTypeKind.SemanticsAdaptation, pair, SemanticsKind.Parameter, children, length: (long)admitted);

    private BoundType TransformFamily(BoundType family, FamilyTransform operation, BoundOrigin? origin = null, SemanticsKind semantics = SemanticsKind.Owner, BoundType? target = null)
    {
        var children = this.RentTypes(family.Components.Count);
        var cases = this.adaptationCaseScratch.Rent(1);
        try
        {
            var count = 0;
            foreach (var mode in SemanticsOrder)
            {
                if (((SemanticsMask)family.Length).Contains(mode))
                {
                    var child = family.Components[count];
                    cases[0] = new(family.Symbol!, mode);
                    children[count++] = operation switch
                    {
                        FamilyTransform.Adapt => this.AdaptedType(semantics, child, origin),
                        FamilyTransform.Target => this.DirectTarget(child),
                        FamilyTransform.Origin => IsBorrow(child.Semantics) || AbstractTypes.IsAbstract(child) ? this.WithOrigins(child, origin, (BoundOrigin[])child.OriginArguments) : child,
                        _ => this.ApplySemantics(child, this.CaseType(target!, cases.AsSpan(0, 1)), origin),
                    };
                }
            }

            return this.TypeFamily(family.Symbol!, (SemanticsMask)family.Length, children.AsSpan(0, count));
        }
        finally
        {
            this.typeScratch.Return(children, clearArray: true);
            this.adaptationCaseScratch.Return(cases, clearArray: true);
        }
    }

    private BoundType ApplySemantics(BoundType whole, BoundType target, BoundOrigin? origin)
        => whole.Kind == BoundTypeKind.SemanticsAdaptation ? this.TransformFamily(whole, FamilyTransform.Apply, origin, target: target)
            : whole.Kind == BoundTypeKind.Parameter || whole.Semantics == SemanticsKind.Parameter
                ? this.InternType(BoundTypeKind.SemanticsApplication, whole.Symbol, SemanticsKind.Parameter, [target], origin: origin)
                : this.Form(whole.Semantics, target, origin);

    private BoundType? SubstituteFamilyArgument(BoundType family, Koto binder, ReadOnlySpan<BoundType?> arguments, ReadOnlySpan<BoundLength?> lengths, int slot, BoundType whole)
    {
        var children = this.RentTypes(whole.Components.Count);
        var caseArguments = this.typeScratch.Rent(arguments.Length);
        var cases = this.adaptationCaseScratch.Rent(1);
        arguments.CopyTo(caseArguments);
        try
        {
            var count = 0;
            foreach (var mode in SemanticsOrder)
            {
                if (((SemanticsMask)whole.Length).Contains(mode))
                {
                    cases[0] = new(whole.Symbol!, mode);
                    caseArguments[slot] = whole.Components[count];
                    if (this.SubstituteType(family, binder, caseArguments.AsSpan(0, arguments.Length), lengths) is not { } child)
                    {
                        return null;
                    }

                    children[count++] = this.CaseType(child, cases.AsSpan(0, 1));
                }
            }

            return this.TypeFamily(whole.Symbol!, (SemanticsMask)whole.Length, children.AsSpan(0, count));
        }
        finally
        {
            this.typeScratch.Return(children, clearArray: true);
            this.typeScratch.Return(caseArguments, clearArray: true);
            this.adaptationCaseScratch.Return(cases, clearArray: true);
        }
    }

    private BoundType? SubstituteFamily(BoundType family, BoundType whole)
    {
        if (whole.Kind == BoundTypeKind.Parameter || whole.Semantics == SemanticsKind.Parameter)
        {
            if (whole.Symbol is not { } selector)
            {
                return null;
            }

            var admitted = this.ResultSemantics(whole, selector.Scope) & (SemanticsMask)family.Length;
            var children = this.RentTypes(BitOperations.PopCount((uint)admitted));
            try
            {
                var count = 0;
                foreach (var mode in SemanticsOrder)
                {
                    if (admitted.Contains(mode))
                    {
                        children[count++] = FamilyCase(family, mode)!;
                    }
                }

                return this.TypeFamily(selector, admitted, children.AsSpan(0, count));
            }
            finally
            {
                this.typeScratch.Return(children, clearArray: true);
            }
        }

        return FamilyCase(family, whole.Semantics);
    }
}
