// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Describes a type for signature comparison.
/// </summary>
/// <param name="Semantics">The type semantics.</param>
/// <param name="Name">The type name.</param>
/// <param name="GenericParameterCount">The number of generic parameters.</param>
[TinyhandObject]
public readonly partial record struct TypeSignature(
    [property: Key(0)] SemanticsKind Semantics,
    [property: Key(1)] string Name,
    [property: Key(2)] int GenericParameterCount);

/// <summary>
/// Describes a function for signature comparison.
/// </summary>
/// <param name="Name">The function name.</param>
/// <param name="GenericParameterCount">The number of generic parameters.</param>
/// <param name="Parameters">The function parameters.</param>
[TinyhandObject]
public readonly partial record struct FunctionSignature(
    [property: Key(0)] string Name,
    [property: Key(1)] int GenericParameterCount,
    [property: Key(2)] ImmutableArray<ParameterSignature> Parameters);

/// <summary>
/// Describes a function parameter for signature comparison.
/// </summary>
/// <param name="TypeKoto">The parameter type.</param>
[TinyhandObject]
public readonly partial record struct ParameterSignature(
    [property: Key(0)] TypeKoto TypeKoto);

/// <summary>
/// Describes a field for signature comparison.
/// </summary>
/// <param name="Name">The field name.</param>
[TinyhandObject]
public readonly partial record struct FieldSignature(
    [property: Key(0)] string Name);

/// <summary>
/// Describes a property for signature comparison.
/// </summary>
/// <param name="Name">The property name.</param>
[TinyhandObject]
public readonly partial record struct PropertySignature(
    [property: Key(0)] string Name);
