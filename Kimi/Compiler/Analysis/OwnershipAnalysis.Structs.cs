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

        if (this.compilation.Binding.StoredBase(type) is { } parent)
        {
            var source = (Koto?)function.BaseInitializer ?? function;
            var place = this.Place(source, parent, OwnershipPlaceKind.Local, false);
            this.body.ReceiverBase = place;
            this.Emit(function.IsConstructor ? OwnershipOperationKind.Declare : OwnershipOperationKind.InitializeReceiverField, source, place);
            if (function.IsConstructor)
            {
                if (function.BaseInitializer is not { } initializer)
                {
                    this.Unsupported(function);
                }
                else
                {
                    var value = this.Expression(initializer);
                    if (value >= 0)
                    {
                        this.Emit(OwnershipOperationKind.Write, source, place, value);
                    }
                }
            }
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

        if (this.body.ReceiverBase >= 0)
        {
            this.Emit(OwnershipOperationKind.CheckReceiverField, source, this.body.ReceiverBase);
        }
    }
}
