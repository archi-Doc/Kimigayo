// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Supplies retained Binding facts; never resolves names during flow analysis.</summary>
internal sealed class BindingControlFlowTypes(Binding binding) : ControlFlowTypeSystem
{
    public override bool IsBoundConstruction(Koto expression) => binding.TryGetEnumConstruction(expression, out _);

    public override bool IsBoundRuntimeTypeTest(Koto expression)
        => expression is IsKoto { BindingState: BindingState.Resolved, BoundRuntimeTest: not null };

    public override bool IsProvenCopy(Koto expression)
        => expression.BindingState == BindingState.Resolved && expression.BoundType is { } type && binding.ProveCopy(type, expression) == ConstraintProof.Proven;

    public override ControlFlowType? GetExpressionType(Koto expression)
        => expression.BindingState == BindingState.Resolved ? FlowType(expression.BoundType) : null;

    public override ControlFlowType? GetDeclaredType(Koto? syntax)
        => syntax?.BindingState == BindingState.Resolved ? FlowType(syntax.BoundType) : null;

    public override ControlFlowType? GetExpectedResultType(Koto boundary)
        => boundary is FunctionKoto { IsGenerated: true } ? ControlFlowType.Unit : boundary is PropertyAccessorKoto accessor ? FlowType(accessor.ReturnType?.BoundType ?? accessor.BoundType) : FlowType(boundary.BoundSymbol?.Type);

    public override FunctionKoto? GetReferencedFunction(Koto expression)
        => expression is ExpressionKoto and not InvocationKoto && expression.BindingState == BindingState.Resolved &&
            expression.BoundSymbol is { Kind: BindingSymbolKind.Function, Declaration: FunctionKoto function } ? function : null;

    public override bool TryGetCallReceiver(InvocationKoto call, out Koto? receiver)
    {
        if (this.IsBoundConstruction(call))
        {
            receiver = null;
            return true;
        }

        var bound = call.BoundCall;
        receiver = bound?.Receiver;
        return bound is not null;
    }

    public override ControlFlowType? GetDefaultGetterResultType(PropertyKoto property)
        => property.BindingState == BindingState.Resolved ? FlowType(property.BoundType) : null;

    public override bool? RequiresUnsafeContext(Koto expression)
    {
        if (expression.BindingState != BindingState.Resolved)
        {
            return null;
        }

        return expression is DereferenceKoto || (expression is ExpressionKoto &&
            expression.BoundSymbol is { Kind: BindingSymbolKind.Function, Declaration: FunctionKoto f } && (f.Modifier & ModifierKind.Unsafe) != 0);
    }

    public override bool? IsCompatible(ControlFlowResultSource source, ControlFlowType target)
    {
        var actual = SemanticType(source.Type);
        var expected = SemanticType(target);
        if (actual is null || expected is null)
        {
            return null;
        }

        return Binding.FitsType(actual, expected);
    }

    public override ControlFlowType? InferResultType(IReadOnlyList<ControlFlowResultSource> sources)
    {
        // Choose the supplied Type accepting every other source, independently of source order (SPEC 14.9.1).
        for (var i = 0; i < sources.Count; i++)
        {
            if (SemanticType(sources[i].Type) is not { } candidate)
            {
                return null;
            }

            if (ReferenceEquals(candidate, BoundType.Never))
            {
                continue;
            }

            var fitsAll = true;
            for (var j = 0; j < sources.Count && fitsAll; j++)
            {
                if (SemanticType(sources[j].Type) is not { } other)
                {
                    return null;
                }

                fitsAll = Binding.FitsType(other, candidate);
            }

            if (fitsAll)
            {
                return FlowType(candidate);
            }
        }

        return null;
    }

    public override bool? IsExhaustive(MatchKoto match) => this.GetMatchCoverage(match, null).IsExhaustive;

    public override MatchCoverage GetMatchCoverage(MatchKoto match, ControlFlowType? subject)
        => binding.TryGetMatch(match, out var plan) ? plan!.Coverage : default;

    private static ControlFlowType? FlowType(BoundType? type)
        => ReferenceEquals(type, BoundType.Unit) ? ControlFlowType.Unit : ReferenceEquals(type, BoundType.Never) ? ControlFlowType.Never : ReferenceEquals(type, BoundType.Boolean) ? ControlFlowType.Boolean : type;

    private static BoundType? SemanticType(ControlFlowType? type)
        => type as BoundType ?? (type?.GetType() == typeof(ControlFlowType) ? BoundType.Primitives.GetValueOrDefault(type.Name) : null);
}
