// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    private bool ValidAddressOfI64(BindingSymbol symbol)
    {
        if (symbol.CompilerFunction != CompilerFunctionKind.StorageAddressOfI64 ||
            symbol.Declaration is not FunctionKoto { Name: "addressOfI64", Modifier: ModifierKind.Internal | ModifierKind.Unsafe, Body: null, ExpressionBody: null, AttributeChain: null, IsGenerated: false, IsRequirement: false, IsSpecialization: false, GenericArguments.Count: 0, Origins.Count: 0, TypeConstraints.Count: 0, Parameters: [var parameter] } function ||
            !ReferenceEquals(function.Parent, this.StorageScope.Owner) ||
            parameter is not { InternalName: "value", ExternalName: "value", DefaultValue: null, AttributeChain: null })
        {
            return false;
        }

        return parameter.Type is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Uniq, OriginName: null, OriginExpression: null } input &&
            function.ReturnType is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Raw, OriginName: null, OriginExpression: null } result &&
            BareName(input.Type, "i64") && BareName(result.Type, "i64");
    }
}
