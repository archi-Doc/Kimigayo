// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<Koto, ResultContext> resultContexts = new(ReferenceEqualityComparer.Instance);

    private readonly List<ResultContext> resultPool = new();
    private readonly List<BoundType> resultTypes = new();
    private StructuralCompletion? resultStructure;
    private int resultCursor;

    /// <summary>Gets the terminal Scalar of a chain of safe value-reference layers (SPEC 3.5.3).</summary>
    /// <param name="type">The source Type.</param>
    /// <returns>The terminal Scalar Type, or null when the chain does not end in a Scalar.</returns>
    internal static BoundType? ScalarReferent(BoundType? type)
    {
        if (type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            return null;
        }

        var terminal = ComparisonReferent(type);
        return terminal.Kind == BoundTypeKind.Primitive && ScalarTypes.Supports(terminal) ? terminal : null;
    }

    /// <summary>Gets the result a function's or accessor's Block body must deliver: the written result, else the bound contract; Unit
    /// for a discarded body and a setter (SPEC 7.1, 11.3).</summary>
    /// <param name="boundary">The function or accessor.</param>
    /// <returns>The result Type, or null while Binding has not resolved it.</returns>
    internal static BoundType? DeclaredResult(Koto boundary) => boundary switch
    {
        FunctionKoto { ReturnType: { BindingState: BindingState.Resolved } written } => written.BoundType,
        FunctionKoto function => KotoHelper.DiscardsFunctionBody(function) ? BoundType.Unit : function.BoundSymbol?.Type,
        PropertyAccessorKoto { AccessorKind: PropertyAccessorKind.Set } => BoundType.Unit,
        PropertyAccessorKoto { ReturnType: { BindingState: BindingState.Resolved } written } => written.BoundType,
        PropertyAccessorKoto { Parent: PropertyKoto { BindingState: BindingState.Resolved } property } => property.BoundType,
        PropertyAccessorKoto accessor => accessor.ReturnType?.BoundType ?? accessor.BoundType,
        _ => null,
    };

    /// <summary>Selects the result Type that every supplied Type fits, independently of source order (SPEC 14.9.2).</summary>
    /// <param name="types">The non-Never source Types.</param>
    /// <param name="scope">The scope supplying read-Type Constraints.</param>
    /// <param name="conflict">Whether no single supplied Type accepts all sources.</param>
    /// <returns>The common Type, or null when none is supplied or the sources conflict.</returns>
    internal BoundType? SelectCommonType(List<BoundType> types, BindingScope scope, out bool conflict)
    {
        conflict = false;
        for (var i = 0; i < types.Count; i++)
        {
            var candidate = types[i];
            var fitsAll = true;
            for (var j = 0; j < types.Count && fitsAll; j++)
            {
                fitsAll = FitsType(types[j], candidate);
            }

            if (fitsAll)
            {
                return candidate;
            }
        }

        // SPEC 3.5.3, 14.9.2: sources that differ only in safe reference layers over one read Type unify to that Type,
        // and each reference source is value-read. Sources with the same layers keep the borrow rule below.
        if (this.ReadTypeUnification(types, scope) is { } scalar)
        {
            return scalar;
        }

        // Borrow results with the same referent retain every incoming dependency.
        // This does not search common bases or change referent variance.
        var borrowed = types.Count > 0 ? types[0] : null;
        for (var i = 1; i < types.Count && borrowed is not null; i++)
        {
            borrowed = this.CommonBorrowResult(borrowed, types[i]);
        }

        if (borrowed is not null)
        {
            return borrowed;
        }

        // No common base is searched; unrelated sources require an annotation.
        conflict = types.Count > 0;
        return null;
    }

    /// <summary>Gets whether Binding inferred Unit for a result target that no declaration, construct rule or expected Type
    /// fixed (SPEC 17.5.2).</summary>
    /// <param name="target">A do or a selection.</param>
    /// <returns>Whether the target's final result is that inferred Unit.</returns>
    internal bool InfersUnit(Koto target) => this.resultContexts.TryGetValue(target, out var context) && context.Inferred &&
        target.BindingState == BindingState.Resolved && ReferenceEquals(target.BoundType, BoundType.Unit);

    // SPEC 14.5.2: a transfer's lexical target, which the jump binding writes to the jump's target row when the transfer may go there
    // (KotoHelper.IsValidTransfer). A null row is a jump the HIR lints record as invalid (rows 9-10), so no transfer is built from it.
    private static Koto? BindTransferTarget(JumpKoto jump)
    {
        var target = KotoHelper.ResolveTransferTarget(jump);
        var valid = KotoHelper.IsValidTransfer(jump, target);
        HirTables.PlanFor(jump, valid).Plan = valid ? target : null;
        return target;
    }

    private BoundType? CommonBorrowResult(BoundType left, BoundType right)
    {
        if (ReferenceEquals(left, BoundType.Never))
        {
            return right;
        }

        if (ReferenceEquals(right, BoundType.Never) || ReferenceEquals(left, right))
        {
            return left;
        }

        if (left.Kind != BoundTypeKind.Semantics || right.Kind != BoundTypeKind.Semantics ||
            !IsBorrow(left.Semantics) || left.Semantics != right.Semantics ||
            left.Origin is not { } a || right.Origin is not { } b ||
            left.Components.Count != 1 || right.Components.Count != 1 ||
            !ReferenceEquals(left.Components[0], right.Components[0]) ||
            left.OriginArguments.Count != 0 || right.OriginArguments.Count != 0)
        {
            return null;
        }

        return this.WithOrigins(left, this.Meet(a, b), []);
    }

    // SPEC 14.9.2: result sources whose reference layers over one read Type differ in number or kind unify to that Type;
    // sources with the same layers keep the ordinary common-borrow rule.
    private BoundType? ReadTypeUnification(List<BoundType> types, BindingScope scope)
    {
        BoundType? scalar = null;
        var differ = false;
        for (var i = 0; i < types.Count; i++)
        {
            var terminal = types[i];
            var first = types[0];
            while (terminal is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
            {
                differ |= first is not { Kind: BoundTypeKind.Semantics, Components.Count: 1 } || first.Semantics != terminal.Semantics;
                terminal = terminal.Components[0];
                first = first is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } ? first.Components[0] : first;
            }

            differ |= first is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 };
            if ((scalar is not null && !ReferenceEquals(scalar, terminal)) ||
                (!(terminal.Kind == BoundTypeKind.Primitive && ScalarTypes.Supports(terminal)) && !this.IsReadType(terminal, scope)))
            {
                return null;
            }

            scalar = terminal;
        }

        return differ ? scalar : null;
    }

    private ResultContext BeginResult(Koto target, BindingScope scope, BoundType? expected, bool deferEvidence = false)
    {
        if (expected?.Origin?.Kind == OriginKind.Inference)
        {
            // Infer the result's actual dependency before completing a local annotation.
            expected = null;
        }

        if ((target is not (TryKoto or FunctionKoto) && !KotoHelper.IsValueContext(target)) || target is WhileKoto or ForKoto)
        {
            expected = BoundType.Unit;
        }

        var context = this.RentResult(expected);
        this.resultContexts[target] = context;
        if (expected is null && !deferEvidence)
        {
            this.InferResultExpected(target, scope, context);
        }

        return context;
    }

    // Rents a cleared context by the pass-local cursor, not by the map size: rebinding a target that is already mapped must not hand
    // the next target a context that is still in use.
    private ResultContext RentResult(BoundType? expected)
    {
        if (this.resultCursor == this.resultPool.Count)
        {
            this.resultPool.Add(new());
        }

        var context = this.resultPool[this.resultCursor++];
        context.Clear(expected);
        return context;
    }

    private void AddBodyResult(Koto body, ResultContext context, StructuralCompletion structural)
    {
        var item = body is CodeBlockKoto { IsExpressionBody: true } block ? block.Items[0] : body;
        if (KotoHelper.IsBodyExpression(item) && KotoHelper.IsValueContext(item))
        {
            // SPEC 3.5.3, 10.2: an adapted source supplies the Type of its one adaptation.
            context.Sources.Add(this.adaptations.TryGetValue(item, out var adaptation) ? adaptation.Type : item.ErasedFunctionType ?? item.BoundType);
        }
        else if (structural.CanComplete(body))
        {
            context.Sources.Add(BoundType.Unit);
        }
    }

    // SPEC 7.1, 14.9.3 (DECISIONS PD32): the structural end of a function's or accessor's Block body supplies Unit, which a declared
    // result other than Unit cannot take, so a body that can complete there is HIR lint row 12 at the body. The Final walk asks once
    // per function and accessor, with its structural completion.
    private void EndArrival(Koto owner, StructuralCompletion structural)
    {
        var body = owner switch { FunctionKoto function => function.Body, PropertyAccessorKoto accessor => accessor.Body, _ => null };
        if (body is CodeBlockKoto && DeclaredResult(owner) is { } result && !ReferenceEquals(result, BoundType.Unit) && structural.CanComplete(body))
        {
            this.lints.Add((body, LintRow.FunctionFallthrough, null));
        }
    }

    // The one structural completion of result, body and conversion checks, cleared for a new query. Its Never evidence is the
    // bound Type, or during a conversion probe the operand's signature (ResultNeverEvidence), whichever check asks first.
    private StructuralCompletion ResultStructure()
    {
        var structural = this.resultStructure ??= new(this.ResultNeverEvidence);
        structural.Clear();
        return structural;
    }

    private BoundType? FinishResult(Koto node, ResultContext context)
    {
        if (context.ArrayFailure is { } failedEvidence)
        {
            // The bodies have still been checked for independent errors. Their join needs the shape evidence that failed.
            return this.CompleteDependent(node, failedEvidence);
        }

        var structural = this.ResultStructure();
        switch (node)
        {
            case FunctionKoto { Body: { } body }:
                this.AddBodyResult(body, context, structural);
                break;
            case IfKoto conditional:
                for (var i = 0; i < conditional.Branches.Count; i++)
                {
                    this.AddBodyResult(conditional.Branches[i].Body, context, structural);
                }

                if (conditional.ElseBody is { } other)
                {
                    this.AddBodyResult(other, context, structural);
                }

                break;
            case MatchKoto match:
                for (var i = 0; i < match.Arms.Count; i++)
                {
                    this.AddBodyResult(match.Arms[i].Body, context, structural);
                }

                break;
            case DoKoto scoped:
                this.AddBodyResult(scoped.Body, context, structural);
                break;
        }

        if (node is IfKoto { ElseBody: null } ||
            (node is WhileKoto loop && structural.CanComplete(loop.Condition)) ||
            (node is ForKoto iteration && structural.CanComplete(iteration.Iterable)))
        {
            context.Sources.Add(BoundType.Unit);
        }

        var types = this.resultTypes;
        types.Clear();
        foreach (var source in context.Sources)
        {
            if (source is null)
            {
                context.Pending = true;
            }
            else if (!ReferenceEquals(source, BoundType.Never))
            {
                types.Add(source);
            }
        }

        var suppliedValue = types.Count > 0;
        var common = context.Expected;
        if (common is not null && suppliedValue && HasArrayLengthHole(common))
        {
            common = this.CompleteArrayExpectation(common, types[0]);
        }

        BoundType? failed = null;
        if (common is null)
        {
            common = this.SelectCommonType(types, this.ConstraintScope(node), out var conflict);
            context.Invalid |= conflict;
        }
        else
        {
            // SPEC 15.6.1, 14.9: each result source fits the expected Type under the premises in scope; the first that does not
            // is the explained one, an Origin relation when only its Origin part fails.
            for (var i = 0; i < types.Count; i++)
            {
                if (!this.FitsTypeAt(types[i], common, node))
                {
                    context.Invalid = true;
                    failed ??= types[i];
                }
            }
        }

        types.Clear();
        if (context.Invalid)
        {
            return failed is not null && common is not null ? this.FailMismatch(node, node, failed, common) : this.Fail(node, BindingFailure.TypeMismatch);
        }

        if (context.Pending)
        {
            return Complete(node, null);
        }

        var completion = node is FunctionKoto { Body: { } functionBody } ? functionBody : node;
        return Complete(node, !suppliedValue && !structural.CanComplete(completion) ? BoundType.Never : common);
    }

    private sealed class ResultContext
    {
        internal Koto? ArrayShape { get; set; }

        internal BoundType? ArrayElement { get; set; }

        internal BoundType? ArrayDefault { get; set; }

        internal Koto? ArrayFailure { get; set; }

        internal BoundType? Expected { get; set; }

        // Whether no declaration, construct rule or caller's expected Type fixed the result, so Binding infers it.
        internal bool Inferred { get; set; }

        internal List<BoundType?> Sources { get; } = new();

        internal List<BoundType> Evidence { get; } = new();

        internal bool Pending { get; set; }

        internal bool Invalid { get; set; }

        internal bool HasLiteral { get; set; }

        internal bool PartialEvidence { get; set; }

        internal bool HasNever { get; set; }

        // A survey's Type for the literal sources, whether one does not fit it, and the first literal's default.
        internal BoundType? LiteralFit { get; set; }

        internal bool LiteralMisfit { get; set; }

        internal BoundType? LiteralDefault { get; set; }

        internal void Clear(BoundType? expected)
        {
            this.HasLiteral = this.PartialEvidence = this.HasNever = this.LiteralMisfit = false;
            this.LiteralFit = this.LiteralDefault = null;
            this.Expected = expected;
            this.Inferred = expected is null;
            this.Invalid = this.Pending = false;
            this.Sources.Clear();
            this.Evidence.Clear();
            this.ArrayShape = null;
            this.ArrayElement = this.ArrayDefault = null;
            this.ArrayFailure = null;
        }
    }
}
