// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static bool HasUngroupedBorrowSuffix(Koto syntax)
    {
        while (syntax is OptionalTypeKoto optional)
        {
            syntax = optional.Type;
        }

        return syntax is TypeSemanticsKoto { Type: not null, HasOrigin: true, IsTransparentWrapper: false };
    }

    private static bool ContainsOrigin(BoundType type, BoundOrigin origin)
    {
        if (ReferenceEquals(type.Origin, origin))
        {
            return true;
        }

        foreach (var component in type.Components)
        {
            if (ContainsOrigin(component, origin))
            {
                return true;
            }
        }

        return false;
    }

    // Run only while publishing failures. These checks neither bind new names nor
    // change a syntax decision, and require no state on successful uses.
    private string? BorrowOriginHint(Koto node)
    {
        if (node is TypeSemanticsKoto { IsLegacyBorrowCandidate: true } legacy &&
            (legacy.BoundSymbol?.Kind == BindingSymbolKind.SemanticsParameter ||
            (legacy.BoundSymbol is null && legacy.BindingFailure == BindingFailure.MissingType && CompilerHelper.TryParse(legacy.Identifier, out _))))
        {
            return "This may be a removed brace borrow annotation. Use 's/T during a' in a Type; an adaptation's outer Origin must be inferred with 'x@s/T'.";
        }

        if (node.BindingFailure == BindingFailure.InvalidOrigin && node is IdentifierNameKoto
            { BoundSymbol: { Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture, Type: { } valueType } } &&
            !IsBorrow(valueType.Semantics))
        {
            return "This value's name does not denote an outer borrow Origin. Use a declared schema slot such as 'x.slot', or borrow local storage with 'let r = x@ref' and infer its Origin. Borrowing does not extend its lifetime.";
        }

        if (node.Parent is AndKoto conjunction && ReferenceEquals(conjunction.Right, node) &&
            HasUngroupedBorrowSuffix(conjunction.Left) && this.IsExistingOriginForHint(node))
        {
            return "An Origin intersection requires parentheses: 'during (a and b)'. Here 'and' separates constraint requirements.";
        }

        if (node.BindingFailure == BindingFailure.TypeMismatch)
        {
            var returned = node as ReturnKoto ?? node.Parent as ReturnKoto;
            var function = returned is not null ? KotoHelper.ResolveTransferTarget(returned) as FunctionKoto : node.Parent as FunctionKoto;
            var syntax = function?.ReturnType;
            while (syntax is ParenthesizedTypeKoto group)
            {
                syntax = group.Type;
            }

            if (syntax is TypeSemanticsKoto { HasOrigin: false } && function?.BoundSymbol?.Type is
                { Origin.Kind: OriginKind.Static, Components.Count: 1 } expected)
            {
                if (IsExcludedInputOrigin(returned?.Expression?.BoundType ?? node.BoundType) ||
                    (this.resultContexts.TryGetValue(node, out var context) && context.Sources.Exists(IsExcludedInputOrigin)))
                {
                    return "The omitted result Origin is static; Origins inside Option inputs are not elision candidates. Write an explicit result 'during' annotation using the required named input Origins, then recheck lifetime and Type fitting.";
                }

                bool IsExcludedInputOrigin(BoundType? actual)
                {
                    if (actual is not { Origin.Kind: not OriginKind.Static, Components.Count: 1 } ||
                        actual.Semantics != expected.Semantics || !IsBorrow(actual.Semantics) ||
                        !ReferenceEquals(actual.Components[0], expected.Components[0]))
                    {
                        return false;
                    }

                    foreach (var parameter in function.Parameters)
                    {
                        if (parameter.Type.BoundType is { } input && input.Symbol == this.Library.Option && ContainsOrigin(input, actual.Origin))
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }
        }

        return null;
    }

    private bool IsExistingOriginForHint(Koto node)
    {
        var name = node is IdentifierNameKoto identifier ? identifier.IdentifierName :
            node is MemberAccessKoto { Left: IdentifierNameKoto left, Right: IdentifierNameKoto } ? left.IdentifierName : null;
        if (name is null)
        {
            return false;
        }

        for (var scope = this.ConstraintScope(node); scope is not null; scope = scope.Parent)
        {
            BoundType? carrier = null;
            var found = false;
            if (scope.Origins?.ContainsKey(name) == true)
            {
                return node is IdentifierNameKoto;
            }

            if (scope.Values.TryGetValue(name, out var value))
            {
                carrier = value.Type;
                found = true;
            }
            else if (this.originDeclarations.GetValueOrDefault(scope.Owner)?.Sets.TryGetValue(name, out var set) == true)
            {
                carrier = set.BoundType;
                found = true;
            }
            else if (scope.OriginSets?.TryGetValue(name, out var localSet) == true)
            {
                carrier = localSet.BoundType;
                found = true;
            }

            if (!found)
            {
                continue;
            }

            if (node is IdentifierNameKoto)
            {
                return carrier?.Origin is not null && IsBorrow(carrier.Semantics);
            }

            while (carrier is { Kind: BoundTypeKind.Semantics } && IsBorrow(carrier.Semantics))
            {
                carrier = carrier.Components[0];
            }

            if (carrier?.Symbol?.Schema is { } schema && node is MemberAccessKoto { Right: IdentifierNameKoto slot })
            {
                for (var i = 0; i < schema.Origins.Count; i++)
                {
                    if (schema.Origins[i].Name == slot.IdentifierName)
                    {
                        return carrier.Kind == BoundTypeKind.Slice ? carrier.Origin is not null : i < carrier.OriginArguments.Count;
                    }
                }
            }

            return false;
        }

        return false;
    }
}
