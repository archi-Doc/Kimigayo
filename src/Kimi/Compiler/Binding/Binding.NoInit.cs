// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<Koto, NoInitProblem>? noInitFailures;

    private enum NoInitProblem : byte
    {
        LocalVar,
        Annotation,
        Holes,
        Element,
    }

    private static bool HasWrittenArrayHole(Koto syntax)
    {
        syntax = ArrayShapeSyntax(syntax);
        return IsArrayHole(syntax) || (syntax is FixedArrayTypeKoto array &&
            (IsArrayHole(array.Length) || HasWrittenArrayHole(array.ElementType)));
    }

    private bool CheckNoInitDeclaration(NoInitKoto directive, VariableKoto variable)
    {
        var problem = variable is not FieldKoto { VariableKind: VariableKind.Var } ? NoInitProblem.LocalVar
            : variable.TypeKoto is null ? NoInitProblem.Annotation
            : HasWrittenArrayHole(variable.TypeKoto) ? NoInitProblem.Holes : (NoInitProblem?)null;
        if (problem is null)
        {
            return true;
        }

        this.FailExplained(ref this.noInitFailures, directive, BindingFailure.NoInit, problem.Value);
        return false;
    }

    private BoundType? BindNoInit(NoInitKoto directive, BindingScope scope, BoundType? expected)
    {
        if (directive.Parent is not VariableKoto variable || !this.CheckNoInitDeclaration(directive, variable))
        {
            return null;
        }

        if (expected is null && variable.TypeKoto is { BindingState: not BindingState.Resolved } unavailable)
        {
            return this.CompleteDependent(directive, unavailable);
        }

        if (expected is not { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner })
        {
            return this.FailExplained(ref this.noInitFailures, directive, BindingFailure.NoInit, NoInitProblem.Annotation);
        }

        var element = expected.Components[0];
        if (element.Semantics != SemanticsKind.Owner ||
            !(ScalarTypes.Supports(element) || this.IsGenericInteger(element, scope) || this.IsGenericWrapping(element, scope)))
        {
            return this.FailExplained(ref this.noInitFailures, directive, BindingFailure.NoInit, NoInitProblem.Element);
        }

        return Complete(directive, expected);
    }
}
