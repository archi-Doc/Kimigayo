// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private bool SpecialField(Koto node)
        => node is MemberAccessKoto { Left: IdentifierNameKoto { BoundSymbol: { Name: "self" } symbol } } &&
            ReferenceEquals(symbol, this.compilation.Binding.SpecialReceiver(this.body.Function));

    private void PrepareReceiverFields(FunctionKoto function)
    {
        if (StructStorage.ReceiverType(function) is not { } type)
        {
            return;
        }

        for (var i = 0; i < StructStorage.Count(type); i++)
        {
            var field = StructStorage.Field(type, i);
            var place = this.LocalPlace(field.BoundSymbol, field, field.BoundType, field.VariableKind == VariableKind.Var);
            this.Emit(function.IsConstructor ? OwnershipOperationKind.Declare : OwnershipOperationKind.InitializeReceiverField, field, place);
            if (function.IsConstructor && field.InitializerKoto is { } initializer)
            {
                var value = this.Expression(initializer);
                if (value >= 0)
                {
                    this.Emit(OwnershipOperationKind.Write, field, place, value);
                }
            }
        }
    }

    private void CheckConstruction(Koto source)
    {
        if (!this.body.Function.IsConstructor || StructStorage.ReceiverType(this.body.Function) is not { } type)
        {
            return;
        }

        for (var i = 0; i < StructStorage.Count(type); i++)
        {
            var field = StructStorage.Field(type, i);
            this.Emit(OwnershipOperationKind.CheckReceiverField, source, this.body.SymbolPlaces[field.BoundSymbol!]);
        }
    }
}
