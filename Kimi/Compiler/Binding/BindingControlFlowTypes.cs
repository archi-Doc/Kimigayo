// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Supplies retained Binding facts; never resolves names during flow analysis.</summary>
internal sealed class BindingControlFlowTypes : ControlFlowTypeSystem
{
    public override ControlFlowType? GetExpressionType(Koto expression)
        => expression.BindingState == BindingState.Resolved ? FlowType(expression.BoundType) : null;

    public override ControlFlowType? GetDeclaredType(Koto? syntax)
        => syntax?.BindingState == BindingState.Resolved ? FlowType(syntax.BoundType) : null;

    public override ControlFlowType? GetExpectedResultType(Koto boundary)
        => boundary is FunctionKoto { IsGenerated: true } ? ControlFlowType.Unit : FlowType(boundary.BoundSymbol?.Type);

    public override FunctionKoto? GetReferencedFunction(Koto expression)
        => expression.BindingState == BindingState.Resolved ? expression.BoundSymbol?.Declaration as FunctionKoto : null;

    public override ControlFlowType? GetDefaultGetterResultType(PropertyKoto property)
        => property.BindingState == BindingState.Resolved ? FlowType(property.BoundType) : null;

    public override bool? RequiresUnsafeContext(Koto expression)
    {
        if (expression.BindingState != BindingState.Resolved)
        {
            return null;
        }

        return expression is DereferenceKoto || (expression.BoundSymbol?.Declaration is FunctionKoto f && (f.Modifier & ModifierKind.Unsafe) != 0);
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
        BoundType? common = null;
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            if (!source.IsReachable)
            {
                continue;
            }

            if (SemanticType(source.Type) is not { } type)
            {
                return null;
            }

            if (ReferenceEquals(type, BoundType.Never))
            {
                continue;
            }

            if (common is not null && !ReferenceEquals(common, type))
            {
                return null;
            }

            common = type;
        }

        return FlowType(common ?? BoundType.Never);
    }

    public override bool? IsExhaustive(MatchKoto match) => null;

    private static ControlFlowType? FlowType(BoundType? type)
        => ReferenceEquals(type, BoundType.Unit) ? ControlFlowType.Unit : ReferenceEquals(type, BoundType.Never) ? ControlFlowType.Never : ReferenceEquals(type, BoundType.Boolean) ? ControlFlowType.Boolean : type;

    private static BoundType? SemanticType(ControlFlowType? type)
        => type as BoundType ?? (type?.GetType() == typeof(ControlFlowType) ? BoundType.Primitives.GetValueOrDefault(type.Name) : null);
}
