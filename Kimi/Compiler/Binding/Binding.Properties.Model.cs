// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Property contracts share their identity vocabulary.
#pragma warning disable CS1591 // Metadata members are named for their semantic roles.

/// <summary>Retained Property semantics; standard access is a Place permission, not a function.</summary>
public sealed class BoundProperty
{
    internal BoundProperty(BindingSymbol symbol)
    {
        this.Symbol = symbol;
        this.Getter = new(this, PropertyAccessorKind.Get);
        this.Setter = new(this, PropertyAccessorKind.Set);
    }

    public BindingSymbol Symbol { get; }

    public PropertyKoto Declaration => (PropertyKoto)this.Symbol.Declaration;

    public BoundType? Type => this.Symbol.Type;

    public bool IsStored => this.Declaration.DeclarationKind is PropertyDeclarationKind.Let or PropertyDeclarationKind.Var;

    public BoundAccessor Getter { get; }

    public BoundAccessor Setter { get; }

    public bool IsVerified { get; internal set; }
}

/// <summary>A reusable operation signature, including implicit inputs without synthetic syntax.</summary>
public sealed class BoundAccessor
{
    internal BoundAccessor(BoundProperty property, PropertyAccessorKind kind)
    {
        this.Property = property;
        this.Kind = kind;
    }

    public BoundProperty Property { get; }

    public PropertyAccessorKind Kind { get; }

    public PropertyAccessorKoto? Declaration { get; internal set; }

    public bool IsPresent { get; internal set; }

    public bool IsStandard => this.IsPresent && this.Property.IsStored && this.Declaration?.Body is null;

    public ModifierKind Access { get; internal set; }

    public BoundType? Receiver { get; internal set; }

    public BoundType? Input { get; internal set; }

    public BoundType? Result { get; internal set; }

    internal Koto Binder => (Koto?)this.Declaration ?? this.Property.Declaration;

    internal BindingSymbol? SelfSymbol { get; set; }

    internal BindingSymbol? ValueSymbol { get; set; }

    internal BindingSymbol? StorageSymbol { get; set; }

    internal BindingSymbol? SignatureSymbol { get; set; }
}

/// <summary>The limited implementations of a required Property operation.</summary>
public enum PropertyWitnessKind : byte
{
    AccessorCall,
    StorageCopy,
    StorageBorrow,
    StorageSet,
}

/// <summary>A verified operation mapping. Calls retain the requirement's function boundary.</summary>
public readonly record struct BoundPropertyWitness(
    BoundAccessor Requirement,
    BoundAccessor Implementation,
    PropertyWitnessKind Kind,
    BoundType ReceiverType,
    BoundType? InputType,
    BoundType ResultType,
    BoundType ImplementationType,
    IReadOnlyList<BoundOrigin?> InputOrigins,
    BoundMemberPath? BasePath);

/// <summary>An interned base projection; traversal never acquires an intermediate base.</summary>
public sealed class BoundMemberPath
{
    internal BoundMemberPath(BoundMemberPath? parent, Koto declaration, BoundType type)
    {
        this.Parent = parent;
        this.Declaration = declaration;
        this.Type = type;
    }

    public BoundMemberPath? Parent { get; }

    public Koto Declaration { get; }

    public BoundType Type { get; }
}
