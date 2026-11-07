// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Syntax-level product membership; this does not discover or certify executable tests.</summary>
internal static class TestDefinition
{
    internal static bool IsIncluded(Koto node)
        => node.CodeContext.Compilation.IsTestBuild && ReferenceEquals(node.CodeContext.Kotonoha, node.CodeContext.Compilation.Kotonoha);

    internal static bool IsTestOnly(Koto node)
    {
        if (node.CodeContext.SourceDocument?.IsTestOnly == true)
        {
            return true;
        }

        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is FunctionKoto && Marker(current) is not null)
            {
                return true;
            }
        }

        return false;
    }

    internal static AttributeKoto? Marker(Koto node)
        => node.FindAttribute(AttributeKind.Test);

    internal static bool IsValidSyntax(FunctionKoto function)
    {
        if (function.IsGenerated || function.IsAnonymous || function.IsConstructor || function.IsDestructor || function.IsRequirement || function.IsSpecialization ||
            (function.Modifier & ModifierKind.Unsafe) != 0 || function.Name.Length == 0 || function.Parameters.Count != 0 ||
            function.GenericArguments.Count != 0 || function.Origins.Count != 0 || function.Captures is { Length: > 0 } ||
            (function.Body is null && function.ExpressionBody is null) || function.FindAttribute(AttributeKind.LibraryImport) is not null ||
            function.ReturnType is not (null or TupleTypeKoto { ElementNodes.Count: 0 }))
        {
            return false;
        }

        var count = 0;
        for (var attribute = function.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
        {
            if (attribute.Kind != AttributeKind.Test)
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
