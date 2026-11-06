// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Value Context (SPEC 14.2.2, 14.5.2, 14.7): whether a position uses its value, decided from its syntax and Binding's facts.
// - Responsibility: Value Context (IsValueContext); a function or accessor body its Unit result discards (DiscardsFunctionBody); a
//   selection whose result is required (IsResultRequiringSelection); the construct a transfer goes to (TransferTarget, DECISIONS BU-101).
// - Inputs: the attached syntax tree, through Syntax's own rules (KotoHelper.ResolveTransferTarget, IsSelectionBody,
//   IsParameterDefault), and Binding's facts: a written result Type resolved to Unit, a function's bound result Type, and whether an
//   anonymous function's body infers it (BoundClosure.InfersResult, DECISIONS BU-109).
// - Outputs: the four answers, read by Binding only: the jump binding, result sources and Structural Completion, bare acquisition and
//   discarded Places, the Adjust rule, the HIR publication and the lints.
// - Invariants: expected Unit alone does not discard a value. A body is discarded only by its declaration's form (a generated
//   function, a constructor, a destructor, a named function other than a static initializer without a written result, a setter) or by
//   a Unit result fixed before the body (SPEC 10.5.4): the written `()`, a written or bound result Type identical to Unit, never one
//   the body's inference decided, or a getter without a written result in a property of such a Type. An invalid transfer goes to no
//   construct.
// - Prohibitions: no reader outside Binding; Syntax keeps only the syntactic rules these read.
public sealed partial class Binding
{
    /// <summary>Gets the construct a transfer goes to (SPEC 14.5.2; DECISIONS BU-101): the target
    /// <see cref="KotoHelper.ResolveTransferTarget"/> resolves, when the transfer may go there, which needs that an unlabeled yield's
    /// selection uses its result (SPEC 14.7). An invalid transfer goes to none, so it is no construct's result source and no path
    /// completes one through it (SPEC 14.9.1, 14.9.3).</summary>
    /// <param name="jump">The attached transfer expression.</param>
    /// <returns>The target, or null when the transfer is invalid.</returns>
    internal static Koto? TransferTarget(JumpKoto jump)
        => KotoHelper.ResolveTransferTarget(jump) is { } target &&
            !(jump is YieldKoto { Label: null } && target is IfKoto or MatchKoto && !IsValueContext(target)) ? target : null;

    /// <summary>Determines whether a selection's result is used, without consulting its bodies.</summary>
    /// <param name="selection">The attached selection.</param>
    /// <returns>Whether the selection is in Value Context.</returns>
    internal static bool IsResultRequiringSelection(Koto selection)
        => selection is TryKoto || (selection is IfKoto or MatchKoto && IsValueContext(selection));

    /// <summary>Determines whether a function's fixed return type discards a single-item value.</summary>
    /// <param name="boundary">The function or accessor.</param>
    /// <returns>Whether the return type is already Unit.</returns>
    internal static bool DiscardsFunctionBody(Koto boundary) => boundary switch
    {
        FunctionKoto f => f.IsGenerated || f.IsConstructor || f.IsDestructor ||
            (f.ReturnType is null && !f.IsAnonymous && f.StaticInitializer is null) || IsUnitType(f.ReturnType) ||
            (f.ClosureStorage is not { InfersResult: true } && ReferenceEquals(f.BoundSymbol?.Type, BoundType.Unit)),
        PropertyAccessorKoto a => a.AccessorKind == PropertyAccessorKind.Set || IsUnitType(a.ReturnType) ||
            (a.ReturnType is null && a.Parent is PropertyKoto property && IsUnitType(property.TypeKoto)),
        _ => false,
    };

    /// <summary>Determines whether a position uses its value; expected Unit alone does not discard it.</summary>
    /// <param name="expression">The expression in its attached syntax tree.</param>
    /// <returns>Whether the expression is in Value Context.</returns>
    internal static bool IsValueContext(Koto expression)
    {
        switch (expression.Parent)
        {
            case null:
                return false;
            case CodeBlockKoto block:
                return block.TrailingExpression == expression && IsValueContext(block);
            case FunctionKoto function:
                return (function.ExpressionBody == expression && !DiscardsFunctionBody(function)) || KotoHelper.IsParameterDefault(function, expression);
            case PropertyAccessorKoto accessor:
                return accessor.Body == expression && expression is not CodeBlockKoto && !DiscardsFunctionBody(accessor);
            case LabeledKoto labeled:
                return IsValueContext(labeled);
            case ParenthesizedKoto parentheses:
                return IsValueContext(parentheses);
            case IfKoto conditional:
                return !KotoHelper.IsSelectionBody(conditional, expression) || IsValueContext(conditional);
            case TryKoto:
                return true;
            case MatchKoto match:
                return !KotoHelper.IsSelectionBody(match, expression) || IsValueContext(match);
            case DoKoto scoped:
                return IsValueContext(scoped);
            case RequireKoto require:
                return require.Condition == expression;
            case ForKoto loop:
                return loop.Iterable == expression;
            case WhileKoto loop:
                return loop.Condition == expression;
            case LoopKoto:
            case BlockStatementKoto:
                return false;
            default:
                return true;
        }
    }

    private static bool IsUnitType(Koto? type)
        => type is TupleTypeKoto { ElementNodes.Count: 0 } || ReferenceEquals(type?.BoundType, BoundType.Unit) ||
            (type is ParenthesizedTypeKoto p && IsUnitType(p.Type));
}
