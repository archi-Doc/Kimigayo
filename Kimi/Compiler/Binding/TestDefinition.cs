// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Syntax-level product membership; this does not discover or certify executable tests.</summary>
internal static class TestDefinition
{
    internal static AttributeKoto? Marker(Koto node)
    {
        for (var attribute = node.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
        {
            if (attribute.IdentifierKoto is IdentifierNameKoto { IdentifierName: "Test" })
            {
                return attribute;
            }
        }

        return null;
    }

    internal static bool IsValidSyntax(FunctionKoto function)
    {
        if (function.IsGenerated || function.IsAnonymous || function.IsConstructor || function.IsDestructor || function.IsRequirement || function.IsSpecialization ||
            (function.Modifier & ModifierKind.Unsafe) != 0 || function.Name.Length == 0 || function.Parameters.Count != 0 ||
            function.GenericArguments.Count != 0 || function.Origins.Count != 0 || function.Captures is { Length: > 0 } ||
            (function.Body is null && function.ExpressionBody is null) || Parser.HasLibraryImport(function.AttributeChain) ||
            function.ReturnType is not (null or TupleTypeKoto { ElementNodes.Count: 0 }))
        {
            return false;
        }

        var count = 0;
        for (var attribute = function.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
        {
            if (attribute.IdentifierKoto is not IdentifierNameKoto { IdentifierName: "Test" })
            {
                continue;
            }

            if (++count > 1 || attribute.Operand is InvocationKoto { ArgumentNodes.Count: > 0 })
            {
                return false;
            }
        }

        for (var parent = function.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is FunctionKoto { IsGenerated: false } ||
                (parent is DeclarationContainerKoto container && (container.GenericParameterNodes.Count != 0 || container.OriginNames.Count != 0)))
            {
                return false;
            }
        }

        return count == 1;
    }
}
