// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal static class FormattingTypes
{
    internal static bool IsBuiltin(BoundType type) => type.Semantics == SemanticsKind.Owner &&
        (ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String) || IsUtf8Slice(type));

    internal static bool IsUtf8Slice(BoundType? type) => type?.Symbol?.LibraryDeclaration == KimiDeclarationId.Utf8Slice;

    internal static bool IsSliceBorrow(BoundType? type) => ReferenceTypes.IsStruct(type) && IsUtf8Slice(type!.Components[0]);
}
