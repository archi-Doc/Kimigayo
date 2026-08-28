// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public readonly partial record struct TypeSignature(
    [property: Key(0)] SemanticsKind Semantics,
    [property: Key(1)] string Name,
    [property: Key(2)] int GenericParameterCount);

[TinyhandObject]
public readonly partial record struct FunctionSignature(
    [property: Key(0)] string Name,
    [property: Key(1)] int GenericParameterCount,
    [property: Key(2)] ImmutableArray<ParameterSignature> Parameters);

[TinyhandObject]
public readonly partial record struct ParameterSignature(
    [property: Key(0)] TypeKoto TypeKoto);

[TinyhandObject]
public readonly partial record struct FieldSignature(
    [property: Key(0)] string Name);

[TinyhandObject]
public readonly partial record struct PropertySignature(
    [property: Key(0)] string Name);
