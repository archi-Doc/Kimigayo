// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<Koto, ResultContext> resultContexts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BindingSymbol, BoundType?> resultLocalEvidence = new(ReferenceEqualityComparer.Instance);

    private readonly List<ResultContext> resultPool = new();
    private readonly List<Koto> resultChildren = new();
    private readonly List<BoundType> resultTypes = new();
    private ResultCollector? resultCollector;
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

    /// <summary>Selects the result Type that every supplied Type fits, independently of source order (SPEC 14.9.1).</summary>
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

        // SPEC 3.5.3, 14.9.1: sources that differ only in safe reference layers over one read Type unify to that Type,
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
    /// fixed (SPEC 17.4.1).</summary>
    /// <param name="target">A do or a selection.</param>
    /// <returns>Whether the target's final result is that inferred Unit.</returns>
    internal bool InfersUnit(Koto target) => this.resultContexts.TryGetValue(target, out var context) && context.Inferred &&
        target.BindingState == BindingState.Resolved && ReferenceEquals(target.BoundType, BoundType.Unit);

    // Whether each operand of a written `^x` or range is literal-only, already bound or a Name, so binding it while surveying
    // result sources reaches no syntax the survey cannot bind yet.
    private static bool SurveyablePosition(Koto node)
    {
        node = KotoHelper.UnwrapParentheses(node);
        return node switch
        {
            FromEndIndexKoto fromEnd => SurveyablePosition(fromEnd.Operand),
            RangeKoto range => (range.Start is null || SurveyablePosition(range.Start)) && (range.End is null || SurveyablePosition(range.End)),
            _ => IsUnfittedLiteral(node) || node.BindingState == BindingState.Resolved || node is IdentifierNameKoto,
        };
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

    // SPEC 14.9.1: result sources whose reference layers over one read Type differ in number or kind unify to that Type;
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

        // Rent by a pass-local cursor, not by the map size: rebinding a target that is already mapped
        // must not hand the next target a context that is still in use.
        if (this.resultCursor == this.resultPool.Count)
        {
            this.resultPool.Add(new());
        }

        var context = this.resultPool[this.resultCursor++];
        context.HasLiteral = false;
        context.PartialEvidence = false;
        context.Expected = expected;
        context.Inferred = expected is null;
        context.Invalid = context.Pending = false;
        context.Sources.Clear();
        context.Evidence.Clear();
        context.ArrayShape = null;
        context.ArrayElement = context.ArrayDefault = null;
        context.ArrayFailure = null;
        this.resultContexts[target] = context;
        if (expected is null && !deferEvidence)
        {
            this.InferResultExpected(target, scope, context);
        }

        return context;
    }

    private void InferResultExpected(Koto target, BindingScope scope, ResultContext context)
    {
        this.resultLocalEvidence.Clear();
        if (this.arrayInferenceShapes.TryGetValue(target, out var shape))
        {
            context.ArrayShape = shape;
            this.FindResultEvidence(target, scope, context);
            context.ArrayShape = null;
            context.Expected = this.ArrayAnnotationExpectation(shape, scope, context.ArrayElement ?? context.ArrayDefault);
            if (context.Expected is null && context.ArrayFailure is null)
            {
                var leaf = shape;
                while (leaf is FixedArrayTypeKoto array)
                {
                    leaf = ArrayShapeSyntax(array.ElementType);
                }

                if (IsArrayHole(leaf))
                {
                    this.FailExplained(ref this.arrayInferenceFailures, leaf, BindingFailure.ArrayAnnotationInference, new(ArrayInferenceProblem.Element, target));
                }
            }

            return;
        }

        this.FindResultEvidence(target, scope, context);
        if (context.HasLiteral)
        {
            // Only literal fitting needs early read-conversion evidence. Ordinary borrow results must wait for
            // block-local declarations and all their Origins before selecting a common reference Type.
            context.Evidence.Clear();
            context.PartialEvidence = false;
            this.resultLocalEvidence.Clear();
            this.FindResultEvidence(target, scope, context);
        }

        context.Expected = this.SelectCommonType(context.Evidence, scope, out var conflict);
        if ((context.PartialEvidence || !context.HasLiteral) && context.Expected is { CarriesOrigin: true })
        {
            // SPEC 14.9.1, 15.6.5: a borrow result waits for every source; one fixed source's Origin is not the common Type
            // of a later body-local Borrow.
            context.Expected = null;
        }

        if (context.Expected?.Kind == BoundTypeKind.Function)
        {
            // SPEC 10.2: a common-Type search compares each source's own Type; it cannot supply the fixed
            // Function expectation that enables erasure. The ordinary source pass collects those Types below.
            context.Expected = null;
        }

        if (context.HasLiteral && context.Expected is { } common && this.ReadTypeReferent(common, scope) is { } terminal)
        {
            // SPEC 3.5.3, 14.9.1: an unfitted literal is fitted to the terminal read Type of the reference sources,
            // which then supply that Type by a value read.
            context.Expected = terminal;
        }

        context.Invalid |= conflict;
        context.Evidence.Clear();
    }

    private BoundType? ResultEvidence(Koto source, BindingScope scope, bool readConversions = false)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source.BoundType is { } known)
        {
            return known;
        }

        switch (source)
        {
            case TryKoto propagation:
                var operand = this.ResultEvidence(propagation.Expression, scope);
                return operand?.Kind == BoundTypeKind.Constructed && (operand.Symbol == this.Library.Option || operand.Symbol == this.Library.Result) ? operand.Components[0] : null;
            case BoolLiteralKoto or IsKoto { IsRuntimeTest: true }:
                return BoundType.Boolean;
            case StringLiteralKoto or InterpolatedStringKoto:
                return BoundType.String;
            case CharLiteralKoto:
                return BoundType.Char;
            case UnitLiteralKoto:
                return BoundType.Unit;
            case NotKoto or AndKoto or OrKoto or EqualsEqualsKoto or ExclamationEqualsKoto or LessThanKoto or LessThanEqualsKoto or GreaterThanKoto or GreaterThanEqualsKoto:
                return BoundType.Boolean;
            case PrefixMinusKoto or PrefixPlusKoto:
                return this.ResultEvidence(((UnaryKoto)source).Operand, scope);
            case BinaryKoto binary when binary.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals:
                return BoundType.Unit;
            case BinaryKoto binary when binary.Akind is KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan:
                return this.ResultEvidence(binary.Left, scope);
            case BinaryKoto binary when binary.Akind is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent or KotoKind.Ampersand or KotoKind.Bar or KotoKind.Caret:
                return this.ResultEvidence(binary.Left, scope) ?? this.ResultEvidence(binary.Right, scope);
            case ConversionKoto conversion:
                if (!this.ConversionCanComplete(conversion.Left, scope))
                {
                    return BoundType.Never;
                }

                if (IsCopyOperation(conversion))
                {
                    return this.ResultEvidence(conversion.Left, this.NodeScope(source, scope));
                }

                // A literal branch can use `position@ref` as read-Type evidence, provided its operand is already
                // known. Do not bind conversions over unavailable block locals while surveying result sources.
                var conversionScope = this.NodeScope(source, scope);
                var operandSyntax = KotoHelper.UnwrapParentheses(conversion.Left);
                if (readConversions && (operandSyntax.BindingState == BindingState.Resolved || operandSyntax is IdentifierNameKoto) &&
                    this.ResultEvidence(conversion.Left, conversionScope) is { } operandType &&
                    (ScalarTypes.Supports(operandType) || this.IsReadType(operandType, conversionScope) || this.ReadTypeReferent(operandType, conversionScope) is not null))
                {
                    return this.BindNode(conversion, conversionScope);
                }

                return this.BindType(conversion.Right, conversionScope);
            case IdentifierNameKoto name:
                var symbol = this.Lookup(name.IdentifierName, this.NodeScope(name, scope), name, false);
                if (symbol?.Kind == BindingSymbolKind.Function)
                {
                    // A declaration's Type is its return contract, not the Type of a reference to that declaration.
                    // Bind the Item in the ordinary source pass; result inference must not invent an erasure signature.
                    return null;
                }

                if (symbol?.Type is { } type)
                {
                    return type;
                }

                if (symbol?.Declaration is VariableKoto { TypeKoto: { } declared })
                {
                    return this.BindType(declared, symbol.Scope);
                }

                if (symbol is { Kind: BindingSymbolKind.Local, Declaration: VariableKoto { InitializerKoto: { } initializer } })
                {
                    // Survey only independent evidence, without checking or retyping the initializer. A local numeric
                    // literal still commits its own default. Memoization avoids rescanning shared local dependency chains;
                    // the pending entry also stops cycles, which ordinary Name/initialization checking diagnoses.
                    if (!this.resultLocalEvidence.TryGetValue(symbol, out var evidence))
                    {
                        this.resultLocalEvidence.Add(symbol, null);
                        evidence = this.ResultEvidence(initializer, symbol.Scope, readConversions);
                        this.resultLocalEvidence[symbol] = evidence;
                    }

                    return evidence;
                }

                return null;
            case InvocationKoto { Method: IdentifierNameKoto callee }:
                var function = this.Lookup(callee.IdentifierName, this.NodeScope(callee, scope), callee, false);
                return function is { Next: null, Declaration: FunctionKoto { GenericArguments.Count: 0 } } ? this.CallResultEvidence(function.Type, scope, readConversions) : null;
            case InvocationKoto { Method: MemberAccessKoto member } when !this.MayBeValueQualifier(member.Left is GenericsKoto generic ? generic.Identifier! : member.Left, this.NodeScope(member, scope)):
                // A Type-qualified, fully formed container supplies its constructor/Case result without surveying arguments
                // or evaluating a receiver. Unbound qualifiers and overloaded functions still wait for ordinary binding.
                var selected = this.Member(member, this.NodeScope(member, scope));
                var declaring = this.memberSelections.GetValueOrDefault(member).DeclaringType ??
                    (selected?.Declaration is FunctionKoto { IsConstructor: true } ? member.Left.BoundType : null);
                if (declaring is not null && (selected?.EnumCase is not null || selected?.Declaration is FunctionKoto { IsConstructor: true }))
                {
                    if (declaring.Components.Count != (declaring.Symbol?.Schema?.GenericSlots.Count ?? 0) ||
                        declaring.OriginArguments.Count != (declaring.Symbol?.Schema?.Origins.Count ?? 0))
                    {
                        return null;
                    }

                    for (var i = 0; i < declaring.Components.Count; i++)
                    {
                        if (declaring.Components[i] is null)
                        {
                            return null;
                        }
                    }

                    return this.CallResultEvidence(declaring, scope, readConversions);
                }

                return selected is { Next: null, Declaration: FunctionKoto { GenericArguments.Count: 0 }, Type: { } result } ? this.CallResultEvidence(this.MemberType(result, declaring), scope, readConversions) : null;
            case RangeKoto or FromEndIndexKoto when !IsUnfittedLiteral(source) && SurveyablePosition(source):
                // SPEC 4.6.3.1, 14.9.1: a written `^x` or range with a typed operand has an independent Type, which a
                // literal-only source such as `0..3` beside `0..n` then fits.
                return this.BindNode(source, this.NodeScope(source, scope));
            default:
                return null;
        }
    }

    // A signature's result Origins belong to the declaration and must be instantiated by the actual call before a join.
    // Its terminal read Type can still fit a literal; no borrowed Origin is taken from this survey.
    private BoundType? CallResultEvidence(BoundType? result, BindingScope scope, bool readConversions)
        => result is not { CarriesOrigin: true } ? result : readConversions ? this.ReadTypeReferent(result, scope) : null;

    private void FindResultEvidence(Koto target, BindingScope scope, ResultContext context)
    {
        switch (target)
        {
            case FunctionKoto { Body: { } body }:
                this.TransferEvidence(body, target, scope, context);
                return;
            case IfKoto conditional:
                for (var i = 0; i < conditional.Branches.Count; i++)
                {
                    this.BodyEvidence(conditional.Branches[i].Body, scope, context);
                }

                if (conditional.ElseBody is { } other)
                {
                    this.BodyEvidence(other, scope, context);
                }

                break;
            case MatchKoto match:
                for (var i = 0; i < match.Arms.Count; i++)
                {
                    var arm = match.Arms[i];
                    this.BodyEvidence(arm.Body, this.NodeScope(arm.Pattern, scope), context);
                }

                break;
            case DoKoto scoped:
                this.BodyEvidence(scoped.Body, scope, context);
                break;
        }

        this.TransferEvidence(target, target, scope, context);
    }

    private void BodyEvidence(Koto body, BindingScope scope, ResultContext context)
    {
        scope = this.NodeScope(body, scope);
        var expression = body is CodeBlockKoto { IsExpressionBody: true } block ? block.Items[0] : body;
        if (expression is not CodeBlockKoto && KotoHelper.IsValueContext(expression))
        {
            this.SourceEvidence(expression, scope, context);
        }
    }

    private void SourceEvidence(Koto expression, BindingScope scope, ResultContext context)
    {
        expression = KotoHelper.UnwrapParentheses(expression);
        if (context.ArrayShape is { } shape)
        {
            if (context.ArrayFailure is null)
            {
                var element = context.ArrayElement;
                var literalDefault = context.ArrayDefault;
                this.ArrayElementEvidence(shape, expression, this.NodeScope(expression, scope), ref element, ref literalDefault, out var failed);
                context.ArrayElement = element;
                context.ArrayDefault = literalDefault;
                context.ArrayFailure = failed;
            }

            return;
        }

        if (expression is LabeledKoto label)
        {
            expression = label.Target;
        }

        if (expression is not TryKoto && expression is IfKoto or MatchKoto or LoopKoto or DoKoto)
        {
            this.FindResultEvidence(expression, scope, context);
            return;
        }

        var evidence = this.ResultEvidence(expression, scope, context.HasLiteral);
        if (evidence is not null && !ReferenceEquals(evidence, BoundType.Never))
        {
            context.Evidence.Add(evidence);
        }
        else if (evidence is null && IsUnfittedLiteral(expression))
        {
            context.HasLiteral = true;
        }
        else if (evidence is null)
        {
            context.PartialEvidence = true;
        }
    }

    private void TransferEvidence(Koto node, Koto target, BindingScope scope, ResultContext context)
    {
        scope = this.NodeScope(node, scope);
        if (node is TryKoto propagation)
        {
            // The generated failure return is checked against the result, but supplies no result-inference evidence.
            this.TransferEvidence(propagation.Expression, target, scope, context);
            return;
        }

        if (node is JumpKoto { Expression: { } expression } jump && jump is not ContinueKoto && KotoHelper.ResolveTransferTarget(jump) == target)
        {
            this.SourceEvidence(expression, scope, context);
        }

        if (node is FunctionKoto or PropertyAccessorKoto or DeferredBlockKoto or CompileTimeSwitchKoto)
        {
            return;
        }

        if (node is IsKoto { IsRuntimeTest: true } test)
        {
            this.TransferEvidence(test.Left, target, scope, context);
            return;
        }

        var start = this.resultChildren.Count;
        node.VisitChildren(this.resultCollector ??= new(this.resultChildren));
        var end = this.resultChildren.Count;
        for (var i = start; i < end; i++)
        {
            this.TransferEvidence(this.resultChildren[i], target, scope, context);
        }

        this.resultChildren.RemoveRange(start, end - start);
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

    private sealed class ResultCollector(List<Koto> children) : KotoVisitor
    {
        public override void Visit(Koto node) => children.Add(node);
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
    }
}
