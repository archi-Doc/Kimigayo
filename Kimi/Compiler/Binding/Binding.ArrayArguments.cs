// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static bool IsArrayArgument(Koto source)
        => KotoHelper.UnwrapParentheses(source) is ArrayLiteralKoto;

    private bool PrepareArrayArgument(Koto source, BindingScope scope)
    {
        source = KotoHelper.UnwrapParentheses(source);
        var elements = source switch
        {
            ArrayLiteralKoto array => array.Elements,
            TupleLiteralKoto tuple => tuple.Elements,
            _ => null,
        };
        if (elements is null)
        {
            return NeedsEnumContext(source) ? this.PrepareContextualEnumInputs(source, scope) : IsUnfittedLiteral(source) || this.BindNode(source, scope) is not null;
        }

        var valid = true;
        for (var i = 0; i < elements.Count; i++)
        {
            valid &= this.PrepareArrayArgument(elements[i], scope);
        }

        return valid;
    }

    // Read-only candidate probing: bind only the winning literal's aggregate shape.
    // General element expressions already have independent Types and cannot be retried.
    private CandidateApplicability ProbeArrayArgument(Koto source, BoundType expected, BindingScope scope)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source is ArrayLiteralKoto array)
        {
            if (expected is not { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner } || expected.Length != array.Elements.Count)
            {
                return CandidateApplicability.Inapplicable;
            }

            for (var i = 0; i < array.Elements.Count; i++)
            {
                var result = this.ProbeArrayArgument(array.Elements[i], expected.Components[0], scope);
                if (result != CandidateApplicability.Applicable)
                {
                    return result;
                }
            }

            return CandidateApplicability.Applicable;
        }

        if (source is TupleLiteralKoto tuple)
        {
            if (expected.Kind != BoundTypeKind.Tuple || expected.Components.Count != tuple.Elements.Count)
            {
                return CandidateApplicability.Inapplicable;
            }

            for (var i = 0; i < tuple.Elements.Count; i++)
            {
                var result = this.ProbeArrayArgument(tuple.Elements[i], expected.Components[i], scope);
                if (result != CandidateApplicability.Applicable)
                {
                    return result;
                }
            }

            return CandidateApplicability.Applicable;
        }

        if (NeedsEnumContext(source))
        {
            return this.ProbeContextualEnum(source, expected, scope);
        }

        if (IsUnfittedLiteral(source))
        {
            return this.FitsInputLiteral(source, expected) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
        }

        return source.BoundType is not { } actual ? CandidateApplicability.Pending :
            FitsType(actual, expected) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
    }
}
