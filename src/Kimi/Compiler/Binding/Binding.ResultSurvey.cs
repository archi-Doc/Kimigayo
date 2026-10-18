// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The result survey (SPEC 14.9.1, 14.9.2 steps 1 and 2): the result sources of a control-flow expression or a function, found without
// binding them, and the Type they supply independently, read before the sources are bound: the Target Result Type a join infers
// (InferResultExpected), a control-flow part's argument kind (SurveyResult, ResultSurvey.AllLiteral) and its inverse (ResultTarget).
// It binds no source and fixes no literal; the join itself is Binding.ControlFlow (BeginResult, FinishResult).
public sealed partial class Binding
{
    private readonly Dictionary<BindingSymbol, BoundType?> resultLocalEvidence = new(ReferenceEqualityComparer.Instance);
    private readonly List<Koto> resultChildren = new();
    private ResultCollector? resultCollector;

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

    // SPEC 14.9: the control-flow expression an argument or operand is, through parentheses and a label, or null.
    private static Koto? ControlFlowExpression(Koto node)
    {
        node = KotoHelper.UnwrapParentheses(node);
        node = node is LabeledKoto label ? label.Target : node;
        return node is IfKoto or MatchKoto or LoopKoto or DoKoto ? node : null;
    }

    // SPEC 14.9.1: the target to which the expression is a result source, as a single-item body's value or a transfer's operand, or
    // null; the inverse of FindResultEvidence's sources.
    private static Koto? ResultTarget(Koto source)
    {
        var node = source;
        while (node.Parent is ParenthesizedKoto || (node.Parent is CodeBlockKoto { IsExpressionBody: true } block && ReferenceEquals(block.Items[0], node)))
        {
            node = node.Parent;
        }

        return node.Parent switch
        {
            JumpKoto jump when jump is not ContinueKoto && ReferenceEquals(jump.Expression, node) => KotoHelper.ResolveTransferTarget(jump),
            IfKoto or MatchKoto when KotoHelper.IsSelectionBody(node.Parent, node) => node.Parent,
            DoKoto scoped when ReferenceEquals(scoped.Body, node) => scoped,
            FunctionKoto function when ReferenceEquals(function.ExpressionBody, node) => function,
            _ => null,
        };
    }

    // SPEC 14.9.1, 10.5.1: the survey of a control-flow expression's result sources without binding them, for its argument or operand
    // kind; with `fit`, whether every literal source fits it (SPEC 12.3). Its context is released unless a nested result took the next.
    private ResultSurvey SurveyResult(Koto target, BindingScope scope, BoundType? fit = null)
    {
        var cursor = this.resultCursor;
        var context = this.RentResult(null);
        context.LiteralFit = fit;
        this.resultLocalEvidence.Clear();
        this.FindResultEvidence(target, scope, context);
        var survey = new ResultSurvey(
            context.HasLiteral, context.Evidence.Count > 0, context.PartialEvidence || context.HasNever, !context.LiteralMisfit, context.LiteralDefault);
        if (this.resultCursor == cursor + 1)
        {
            this.resultCursor = cursor;
        }

        return survey;
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

            context.Collecting = true;
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
            // SPEC 14.9.2, 15.6.5: a borrow result waits for every source; one fixed source's Origin is not the common Type
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
            // SPEC 3.5.3, 14.9.2: an unfitted literal is fitted to the terminal read Type of the reference sources,
            // which then supply that Type by a value read.
            context.Expected = terminal;
        }

        context.Invalid |= conflict;
        context.Evidence.Clear();
        context.Collecting = true; // The survey binds what it reads at once; the sources bound after it collect.
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
            case UnaryKoto { Akind: KotoKind.Not } or BinaryKoto { Akind: KotoKind.And or KotoKind.Or or KotoKind.EqualsEquals or KotoKind.ExclamationEquals or KotoKind.LessThan or KotoKind.LessThanEquals or KotoKind.GreaterThan or KotoKind.GreaterThanEquals }:
                return BoundType.Boolean;
            case UnaryKoto { Akind: KotoKind.PrefixMinus } negation:
                return this.BuiltinRouteEvidence(negation.Operand, null, scope);
            case UnaryKoto { Akind: KotoKind.PrefixPlus } sign:
                return this.ResultEvidence(sign.Operand, scope);
            case BinaryKoto binary when binary.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals:
                return BoundType.Unit;
            case BinaryKoto binary when binary.Akind is KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan:
                return this.ResultEvidence(binary.Left, scope);
            case BinaryKoto binary when binary.Akind is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent:
                return this.BuiltinRouteEvidence(binary.Left, binary.Right, scope);
            case BinaryKoto binary when binary.Akind is KotoKind.Ampersand or KotoKind.Bar or KotoKind.Caret:
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
                // SPEC 4.6.2, 14.9.2: a written `^x` or range with a typed operand has an independent Type, which a
                // literal-only source such as `0..3` beside `0..n` then fits.
                return this.BindNode(source, this.NodeScope(source, scope));
            default:
                return null;
        }
    }

    // SPEC 13.3.4, 14.9.2: an operator a user provider may route (binary +, -, *, /, % and unary -) has its operands' Type only on
    // the built-in route, which the survey selects when every operand is literal-only or ends, past safe reference layers, in a
    // built-in numeric Type; that Type is the evidence. A user provider's result is its conformance's Output and an unknown operand
    // may be one, so either supplies none. Built-in-only operators (bitwise, shift, unary +) keep their operands' Type.
    private BoundType? BuiltinRouteEvidence(Koto left, Koto? right, BindingScope scope)
    {
        BoundType? rightType = null;
        return this.BuiltinOperandEvidence(left, scope, out var leftType) && (right is null || this.BuiltinOperandEvidence(right, scope, out rightType))
            ? leftType ?? rightType : null;
    }

    // A literal-only operand selects nothing; any other operand selects the built-in route by a surveyed built-in numeric terminal.
    private bool BuiltinOperandEvidence(Koto operand, BindingScope scope, out BoundType? terminal)
    {
        terminal = null;
        if (IsUnfittedLiteral(operand))
        {
            return true;
        }

        if (this.ResultEvidence(operand, scope) is not { } type)
        {
            return false;
        }

        terminal = this.ArithmeticTerminal(operand, type);
        return this.BuiltinNumeric(terminal, scope);
    }

    // A signature's result Origins belong to the declaration and must be instantiated by the actual call before a join.
    // Its terminal read Type can still fit a literal; no borrowed Origin is taken from this survey.
    private BoundType? CallResultEvidence(BoundType? result, BindingScope scope, bool readConversions)
        => result is not { CarriesOrigin: true } ? result : readConversions ? this.ReadTypeReferent(result, scope) : null;

    private void FindResultEvidence(Koto target, BindingScope scope, ResultContext context)
    {
        switch (target)
        {
            case FunctionKoto function:
                // SPEC 14.9.1: a single-item body's value and every self-targeted return operand; an indented body supplies no value.
                var body = (function.Body ?? function.ExpressionBody)!;
                this.BodyEvidence(body, scope, context);
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
            // SPEC 12.3: a survey for a Literal fits each literal source to the surveyed Type; the first one's default is the default.
            context.HasLiteral = true;
            context.LiteralDefault ??= this.LiteralDefault(expression);
            context.LiteralMisfit |= context.LiteralFit is { } fit && !this.FitsInputLiteral(expression, fit, scope);
        }
        else if (evidence is null)
        {
            context.PartialEvidence = true;
        }
        else
        {
            context.HasNever = true; // SPEC 14.9.2, 10.5.1: a Never source supplies no candidate and is no Literal.
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

    // A survey of result sources (SPEC 14.9.1): whether one is a literal, whether one is typed, whether one is Never or not known
    // without binding, whether every literal fits the surveyed Type, and the first literal's default. AllLiteral is SPEC 10.5.1's
    // Literal control flow, every source a literal.
    private readonly record struct ResultSurvey(bool Literal, bool Typed, bool Other, bool Fits, BoundType? Default)
    {
        internal bool AllLiteral => this.Literal && !this.Typed && !this.Other;
    }

    private sealed class ResultCollector(List<Koto> children) : KotoVisitor
    {
        public override void Visit(Koto node) => children.Add(node);
    }
}
