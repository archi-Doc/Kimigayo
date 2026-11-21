// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ResultContextRows resultContexts;

    private readonly List<ResultContext> resultPool = new();
    private readonly List<BoundType> resultTypes = new();
    private readonly List<Koto?> resultValues = new(); // The source node of each of resultTypes (ResultCandidates), null for a formed Type.
    private StructuralCompletion? resultStructure;
    private int resultCursor;

    /// <summary>Gets the terminal Scalar of a chain of safe value-reference layers (SPEC 3.5.3).</summary>
    /// <param name="type">The source Type.</param>
    /// <returns>The terminal Scalar Type, or null when the chain does not end in a Scalar.</returns>
    internal static BoundType? ScalarReferent(BoundType? type)
    {
        if (type?.SafeReferent is null)
        {
            return null;
        }

        var terminal = ComparisonReferent(type);
        return terminal.IsScalar ? terminal : null;
    }

    /// <summary>Gets the result a function's or accessor's Block body must deliver: the written result, else the bound contract; Unit
    /// for a discarded body and a setter (SPEC 7.1, 11.3).</summary>
    /// <param name="boundary">The function or accessor.</param>
    /// <returns>The result Type, or null while Binding has not resolved it.</returns>
    internal static BoundType? DeclaredResult(Koto boundary) => boundary switch
    {
        FunctionKoto { ReturnType: { BindingState: BindingState.Resolved } written } => written.BoundType,
        FunctionKoto function => DiscardsFunctionBody(function) ? BoundType.Unit : function.BoundSymbol?.Type,
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
                fitsAll = this.IsSubtype(types[j], candidate);
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

        // SPEC 14.9.2 step 2 (PLAN G758, G443's part for sources that differ in Origins alone): their common Type meets the Origins at
        // covariant positions, a pair layer's slot included, and joins differing raw Origins unstated (CommonOriginType). This does not
        // search common bases or change variance.
        var common = types.Count > 0 ? types[0] : null;
        for (var i = 1; i < types.Count && common is not null; i++)
        {
            common = ReferenceEquals(common, BoundType.Never) ? types[i] : ReferenceEquals(types[i], BoundType.Never) ? common : this.CommonOriginType(common, types[i], out _);
        }

        if (common is not null)
        {
            return common;
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

    // SPEC 14.5.2, 14.9.1 (DECISIONS BU-101): the construct a transfer goes to (TransferTarget), which the jump binding writes
    // to the jump's target row. A null row is a jump the HIR lints record as invalid (rows 9-10): it supplies no result source and its
    // operand has no expected Type, and no transfer is built from it.
    private static Koto? BindTransferTarget(JumpKoto jump)
    {
        var target = TransferTarget(jump);
        HirTables.PlanFor(jump, target is not null).Plan = target;
        return target;
    }

    // SPEC 12.3.1, 14.9.2: the position fixes every source's Target Result Type, even with omitted Origins; their fits collect
    // the bounds resolved by LocalOrigins. A discard position and while/for fix Unit.
    private static BoundType? ResultExpectation(Koto target, BoundType? expected)
    {
        if ((target is not (TryKoto or FunctionKoto) && !IsValueContext(target)) || target is WhileKoto or ForKoto)
        {
            return BoundType.Unit;
        }

        return expected;
    }

    // The first array literal source of a join, through the forms that deliver it (DeliveredOperand), that is still unbound: it awaits
    // its position; null when there is none.
    // Temporary path (F's ruling 48): a Tuple literal source keeps step 3 at its typed sources' Type, since classifying it by the call
    // makes an Origins-only difference an inapplicable candidate (g586h3 would give NoApplicableOverload_Kd on a valid program instead of
    // G443's located Unsupported_Kd). Removal condition: PLAN G443's landing (R7-I2 d), which then classifies Tuple sources here too.
    private static Koto? AwaitingAggregateSource(ResultContext context)
    {
        foreach (var source in context.SourceNodes)
        {
            var core = source;
            while (core is not null && DeliveredOperand(core) is { } operand)
            {
                core = operand;
            }

            if (core is ArrayLiteralKoto { BoundType: null })
            {
                return core;
            }
        }

        return null;
    }

    // SPEC 10.5.1 (DECISIONS BU-43, BU-78; PLAN G924 B2, F's ruling 46): whether a join that is no conflict is its call's argument probe
    // control flow, which its call classifies (ClassifiedByCall): a typed one holding a failed source, or one with an array literal
    // source that awaits its position (step 3 at its typed sources' Type would decide it before the call's places meet).
    private bool WaitsForItsCall(Koto node, ResultContext context, bool awaiting)
        => context.HasTyped && context.Cause is not null ? this.ClassifiedByCall(node, null)
            : awaiting && AwaitingAggregateSource(context) is { } shape && this.ClassifiedByCall(node, shape);

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
            while (terminal.SafeReferent is { } referent)
            {
                differ |= first is not { Kind: BoundTypeKind.Semantics, Components.Count: 1 } || first.Semantics != terminal.Semantics;
                terminal = referent;
                first = first is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } ? first.Components[0] : first;
            }

            differ |= first.SafeReferent is not null;
            if ((scalar is not null && !ReferenceEquals(scalar, terminal)) ||
                (!terminal.IsScalar && !this.IsReadType(terminal, scope)))
            {
                return null;
            }

            scalar = terminal;
        }

        return differ ? scalar : null;
    }

    // SPEC 3.5.3, 14.9.2 step 2: beside literal sources, the typed sources' common Type `common` ending in a read Type through safe
    // reference layers gives that read Type: the literals are literals of it and the reference sources supply it by a value read. Every
    // formation with literal sources reads it: a join's own sources (InferResultExpected) and a position's parts (PositionType).
    private BoundType? BesideLiterals(BoundType? common, BindingScope scope)
        => common is not null && this.ReadTypeReferent(common, scope) is { } terminal ? terminal : common;

    private ResultContext BeginResult(Koto target, BindingScope scope, BoundType? expected, bool deferEvidence = false)
    {
        expected = ResultExpectation(target, expected);
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
        if (KotoHelper.IsBodyExpression(item) && IsValueContext(item))
        {
            // SPEC 3.5.3, 10.2: an adapted source supplies the Type of its one adaptation.
            context.Add(body, this.adaptations.TryGetValue(item, out var adaptation) ? adaptation.Type : item.ErasedFunctionType ?? item.BoundType);
        }
        else if (structural.CanComplete(body))
        {
            // DIAGNOSTICS §4.4: the completion of a body the parser supplied is a guess, which decides no inferred Target Result Type.
            context.Add(null, context.Expected is null && IsRecovery(body, out _) ? null : BoundType.Unit);
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
        var structural = this.resultStructure ??= new(this, this.ResultNeverEvidence, null, static (ref NodeColumns columns) => ref columns.ResultCompletions);
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
            case FunctionKoto function:
                this.AddBodyResult((function.Body ?? function.ExpressionBody)!, context, structural);
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
            context.Add(null, BoundType.Unit);
        }

        context.Collecting = false;
        return this.DecideResult(node, context);
    }

    // SPEC 14.9.2: the Target Result Type of the collected sources, each source checked against it, and the Expression Type.
    private BoundType? DecideResult(Koto node, ResultContext context)
    {
        var (types, values) = (this.resultTypes, this.resultValues);
        types.Clear();
        values.Clear();
        var awaiting = this.ResultCandidates(context, types, values);
        if (context.Expected is null && types.Count == 0 && awaiting && this.AwaitsWithoutCandidate(node, context, types))
        {
            return Complete(node, null); // SPEC 14.9.2: nested results propagate the same way; its position decides its Type.
        }

        var suppliedValue = types.Count > 0 || awaiting;
        var common = context.Expected;
        if (common is not null && types.Count > 0 && HasArrayLengthHole(common))
        {
            common = this.CompleteArrayExpectation(common, types[0]);
        }

        BoundType? failed = null;
        var failure = default(ComparisonFailure);
        var conflict = false;
        if (common is null)
        {
            common = this.SelectCommonType(types, this.ConstraintScope(node), out conflict);
        }
        else
        {
            // SPEC 15.6.1, 14.9: each result source fits the expected Type, its Origin positions rows of the body that A5 judges at the
            // source's value, the value that supplies the fit's longer end (the use-site fit; R7-O1; H11b D5), a structural Unit at the
            // join; the first that does not fit otherwise is the explained one. A region on either side leaves rows, a regional target its
            // source edges too.
            for (var i = 0; i < types.Count; i++)
            {
                ComparisonFailure fit;
                var value = values[i] ?? node;
                if (!(this.RegionalAt(types[i], common, value) ? this.CheckTypeUse(types[i], common, value, out fit) : this.FitsValueAt(types[i], common, node, value, out fit)))
                {
                    context.Conflict();
                    if (failed is null)
                    {
                        (failed, failure) = (types[i], fit);
                    }
                }
            }
        }

        // SPEC 10.5.1, 23.1.2: a typed join fixes no complete Type without its call when its typed parts have no common Type (U1.6g
        // (a)) or, holding a typed part (HasTyped), it holds a failed source (G368 (1), DECISIONS BU-78 as amended), or an aggregate
        // source awaits its position (PLAN G924 B2: its sources meet at the parameter's places, 10.5.1 ¶1, 4.3.1); under its call's
        // argument probe it waits, and its call classifies it and binds it at the selected candidate's Type.
        if (conflict ? this.ConflictWaits(node) : this.WaitsForItsCall(node, context, awaiting))
        {
            context.Wait(this.SourcesAwait(context));
            return Complete(node, null);
        }

        if (conflict)
        {
            context.Conflict();
        }

        // SPEC 14.9.2 step 3: each source that awaits its position takes the Target Result Type, by its late Type (no Never fitting).
        if (awaiting && common is not null && this.BindWaitedSources(node, context, common) is { } misfit)
        {
            context.Conflict();
            failed ??= misfit;
        }

        types.Clear();
        values.Clear();
        if (context.Outcome == JoinOutcome.Conflict)
        {
            return failed is not null && common is not null ? this.FailMismatch(node, node, failed, common, failure)
                : this.Fail(node, BindingFailure.TypeMismatch);
        }

        context.DecideRest();
        if (context.Outcome == JoinOutcome.Dependent)
        {
            return this.CompleteDependent(node, context.Cause!); // SPEC 23.1.2: it rests on its failed source.
        }

        if (context.Pending)
        {
            return Complete(node, null); // Its source waits for its call.
        }

        context.Outcome = JoinOutcome.Decided;

        var completion = node is FunctionKoto function ? (function.Body ?? function.ExpressionBody)! : node;
        return Complete(node, !suppliedValue && !this.ResultStructure().CanComplete(completion) ? BoundType.Never : common);
    }

    // A join's outcome (SPEC 14.9.2, 23.1.2; DECISIONS BU-43): undecided while it collects its sources and forms its Type; waiting for
    // its position's Type (an outer join's, its call's); failed by a conflict it found (Conflict, its own record) or resting on a failed
    // part or on the join it waited for (Dependent, no record); or decided (bound, or pending on a source that waits for its call).
    internal enum JoinOutcome : byte
    {
        Undecided,
        Waiting,
        Conflict,
        Dependent,
        Decided,
    }

    /// <summary>The `resultContexts` family's one accessor (DECISIONS BU-220): a working-state family, the context a result target's
    /// binding rents for it (BeginResult) and every reader takes inside the same pass, kept in the target's module's NodeColumns at its
    /// SyntaxId (null for none); the library's rows are released at its freeze (NodeColumns.Freeze), so a library node reads none after
    /// the library pass, as each later pass's reset left it. Its members keep the names its readers use.</summary>
    private readonly struct ResultContextRows(Binding binding)
    {
        internal ResultContext this[Koto target]
        {
            get => this.TryGetValue(target, out var context) ? context : throw new KeyNotFoundException($"`{target}` has no result context in this pass.");
            set => HirTables.Of(target).Families.ResultContexts.Ref(target.SyntaxId, binding) = value;
        }

        internal bool ContainsKey(Koto target) => this.TryGetValue(target, out _);

        internal bool TryGetValue(Koto target, [NotNullWhen(true)] out ResultContext? context)
            => (context = target.SyntaxOwner is { } table ? table.Hir.Families.ResultContexts.Get(target.SyntaxId, binding) : null) is not null;
    }

    internal sealed class ResultContext
    {
        internal Koto? ArrayShape { get; set; }

        internal BoundType? ArrayElement { get; set; }

        internal BoundType? ArrayDefault { get; set; }

        internal Koto? ArrayFailure { get; set; }

        internal BoundType? Expected { get; set; }

        // Whether no declaration, construct rule or caller's expected Type fixed the result, so Binding infers it.
        internal bool Inferred { get; set; }

        internal List<BoundType?> Sources { get; } = new();

        // Each source's node beside its Type: the body or transfer operand that supplies it, or null for a structural Unit.
        internal List<Koto?> SourceNodes { get; } = new();

        internal List<BoundType> Evidence { get; } = new();

        internal bool Pending { get; set; }

        // Whether the target collects its sources after its survey, so that a literal source waits for its Type (SPEC 14.9.2 step 3).
        internal bool Collecting { get; set; }

        // The join's one outcome (DECISIONS BU-43), written once where it becomes true and read by every enclosing join and call, and
        // what a Dependent one rests on: its failed part, or the join it waited for.
        internal JoinOutcome Outcome { get; set; }

        internal Koto? Cause { get; set; }

        // What a Waiting join awaits its position for (SPEC 14.9.2 step 3; DECISIONS BU-87): published with the outcome (Wait) from its
        // sources as they are then, and again when its call takes its parts (DecideTaken); every reader of a Waiting join's waiting
        // reads it (AwaitsFor).
        internal Awaiting Awaiting { get; private set; }

        internal bool HasLiteral { get; set; }

        // Whether a source is an owner value of a literal form (SPEC 3.6, 14.9.2; PLAN G924 rule B): a Tuple, array, fill, mixed range or
        // Dictionary literal, at any depth of nested control flow; an untyped literal is HasLiteral's.
        internal bool HasValue { get; set; }

        // Whether the join holds a typed part (G368 (1)): its survey found one at any depth (its evidence, ResultSurvey.Typed), kept
        // after the survey drops its evidence and whatever Expected it chose, or a collected source is one (ResultCandidates).
        internal bool HasTyped { get; set; }

        internal bool PartialEvidence { get; set; }

        internal bool HasNever { get; set; }

        // A survey's Type for the literal sources, whether one does not fit it, and the first literal's default.
        internal BoundType? LiteralFit { get; set; }

        internal bool LiteralMisfit { get; set; }

        internal BoundType? LiteralDefault { get; set; }

        internal void Clear(BoundType? expected)
        {
            this.HasLiteral = this.HasValue = this.HasTyped = this.PartialEvidence = this.HasNever = this.LiteralMisfit = false;
            this.LiteralFit = this.LiteralDefault = null;
            this.Expected = expected;
            this.Inferred = expected is null;
            this.Pending = this.Collecting = false;
            this.Outcome = JoinOutcome.Undecided;
            this.Awaiting = Awaiting.None;
            this.Cause = null;
            this.Sources.Clear();
            this.SourceNodes.Clear();
            this.Evidence.Clear();
            this.ArrayShape = null;
            this.ArrayElement = this.ArrayDefault = null;
            this.ArrayFailure = null;
        }

        internal void Add(Koto? node, BoundType? type)
        {
            this.Sources.Add(type);
            this.SourceNodes.Add(node);
        }

        // The one writer of Waiting (DECISIONS BU-87): the join waits for its position or its call, awaiting what its sources await
        // (SourcesAwait), each held join's own fact already published.
        internal void Wait(Awaiting awaiting)
        {
            this.Outcome = JoinOutcome.Waiting;
            this.Awaiting = awaiting;
        }

        // Records the join's own conflict, whose record it writes; it prevails over a dependence (RestOn).
        internal void Conflict() => this.Outcome = JoinOutcome.Conflict;

        // Records the first failure the join did not find and will rest on (SPEC 23.1.2), unless it found a conflict. U1.6h: a failed source
        // takes no part in its join's kind, so recording it decides nothing; the join rests on it at its verdict (DecideRest).
        internal void RestOn(Koto cause)
        {
            if (this.Outcome != JoinOutcome.Conflict)
            {
                this.Cause ??= cause;
            }
        }

        // U1.6h: the verdict, the one writer of Dependent: when the join decides, it rests on its recorded cause unless it found a conflict.
        internal void DecideRest()
        {
            if (this.Outcome != JoinOutcome.Conflict && this.Cause is not null)
            {
                this.Outcome = JoinOutcome.Dependent;
            }
        }
    }
}
