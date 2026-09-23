// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private BindingSymbol? builtinFormat;

    private BindingSymbol FormatTarget(BindingSymbol selected, BoundType? self)
    {
        if (self is null || !FormattingTypes.IsBuiltin(self) ||
            selected.Scope.Owner.BoundSymbol?.LibraryDeclaration != KimiDeclarationId.Utf8Format)
        {
            return selected;
        }

        this.builtinFormat ??= new(selected.Name, selected.Kind, selected.Declaration, selected.Scope)
        {
            CompilerFunction = CompilerFunctionKind.BuiltinFormat,
        };
        this.builtinFormat.Type = selected.Type;
        this.builtinFormat.ReceiverIndex = selected.ReceiverIndex;
        return this.builtinFormat;
    }
}
