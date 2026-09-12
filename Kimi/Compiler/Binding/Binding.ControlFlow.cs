// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<Koto, ResultContext> resultContexts = new(ReferenceEqualityComparer.Instance);

    private readonly List<ResultContext> resultPool = new();
    private readonly List<Koto> resultChildren = new();
    private ResultCollector? resultCollector;
    private StructuralCompletion? resultStructure;

    private ResultContext BeginResult(Koto target, BindingScope scope, BoundType? expected)
    {
        if (!KotoHelper.IsValueContext(target) || target is WhileKoto or ForKoto)
        {
            expected = BoundType.Unit;
        }

        if (this.resultContexts.Count == this.resultPool.Count)
        {
            this.resultPool.Add(new());
        }

        var context = this.resultPool[this.resultContexts.Count];
        context.Expected = expected;
        context.Invalid = context.Pending = false;
        context.Sources.Clear();
        this.resultContexts[target] = context;
        if (expected is null)
        {
            this.FindResultEvidence(target, scope, context);
        }

        return context;
    }

    private BoundType? ResultEvidence(Koto source, BindingScope scope)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source.BoundType is { } known)
        {
            return known;
        }

        switch (source)
        {
            case BoolLiteralKoto:
                return BoundType.Boolean;
            case StringLiteralKoto:
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
            case BinaryKoto binary when binary.Akind is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent or KotoKind.Ampersand or KotoKind.Bar or KotoKind.Caret:
                return this.ResultEvidence(binary.Left, scope) ?? this.ResultEvidence(binary.Right, scope);
            case ConversionKoto conversion:
                return this.BindType(conversion.Right, this.NodeScope(source, scope));
            case IdentifierNameKoto name:
                var symbol = this.Lookup(name.IdentifierName, this.NodeScope(name, scope), name, false);
                if (symbol?.Type is { } type)
                {
                    return type;
                }

                return symbol?.Declaration is VariableKoto { TypeKoto: { } declared }
                    ? this.BindType(declared, symbol.Scope) : null;
            case InvocationKoto { Method: IdentifierNameKoto callee }:
                var function = this.Lookup(callee.IdentifierName, this.NodeScope(callee, scope), callee, false);
                return function is { Next: null, Declaration: FunctionKoto { GenericArguments.Count: 0 } } ? function.Type : null;
            default:
                return null;
        }
    }

    private void FindResultEvidence(Koto target, BindingScope scope, ResultContext context)
    {
        switch (target)
        {
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
                    this.BodyEvidence(match.Arms[i].Body, scope, context);
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
        var expression = body is CodeBlockKoto { IsExpressionBody: true } block ? block.Items[0] : body;
        if (expression is not CodeBlockKoto && KotoHelper.IsValueContext(expression))
        {
            this.SourceEvidence(expression, scope, context);
        }
    }

    private void SourceEvidence(Koto expression, BindingScope scope, ResultContext context)
    {
        expression = KotoHelper.UnwrapParentheses(expression);
        if (expression is LabeledKoto label)
        {
            expression = label.Target;
        }

        if (expression is IfKoto or MatchKoto or LoopKoto or DoKoto)
        {
            this.FindResultEvidence(expression, scope, context);
            return;
        }

        var evidence = this.ResultEvidence(expression, scope);
        if (evidence is not null && !ReferenceEquals(evidence, BoundType.Never))
        {
            context.Invalid |= context.Expected is not null && !ReferenceEquals(context.Expected, evidence);
            context.Expected ??= evidence;
        }
    }

    private void TransferEvidence(Koto node, Koto target, BindingScope scope, ResultContext context)
    {
        if (node is JumpKoto { Expression: { } expression } jump && jump is not ContinueKoto && KotoHelper.ResolveTransferTarget(jump) == target)
        {
            this.SourceEvidence(expression, scope, context);
        }

        if (node is FunctionKoto or PropertyAccessorKoto or DeferredBlockKoto or CompileTimeMatchKoto)
        {
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
            context.Sources.Add(item.BoundType);
        }
        else if (structural.CanComplete(body))
        {
            context.Sources.Add(BoundType.Unit);
        }
    }

    private BoundType? FinishResult(Koto node, ResultContext context)
    {
        var structural = this.resultStructure ??= new(item => ReferenceEquals(item.BoundType, BoundType.Never));
        structural.Clear();
        switch (node)
        {
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

        var common = context.Expected;
        var suppliedValue = false;
        foreach (var source in context.Sources)
        {
            if (source is null)
            {
                context.Pending = true;
                continue;
            }

            if (ReferenceEquals(source, BoundType.Never))
            {
                continue;
            }

            suppliedValue = true;
            if (common is null)
            {
                common = source;
            }
            else if (!FitsType(source, common))
            {
                context.Invalid = true;
            }
        }

        if (context.Invalid)
        {
            return Fail(node, BindingFailure.TypeMismatch);
        }

        if (context.Pending)
        {
            return Complete(node, null);
        }

        return Complete(node, !suppliedValue && !structural.CanComplete(node) ? BoundType.Never : common);
    }

    private sealed class ResultCollector(List<Koto> children) : KotoVisitor
    {
        public override void Visit(Koto node) => children.Add(node);
    }

    private sealed class ResultContext
    {
        internal BoundType? Expected { get; set; }

        internal List<BoundType?> Sources { get; } = new();

        internal bool Pending { get; set; }

        internal bool Invalid { get; set; }
    }
}
