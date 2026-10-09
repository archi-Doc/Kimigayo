// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a structure declaration.
/// </summary>
public sealed class StructKoto : DeclarationContainerKoto
{
    private FunctionKoto? implicitConstructor;

    internal FunctionKoto? ImplicitConstructor { get; private set; }

    internal ConstructorAvailability ConstructorAvailability { get; private set; }

    /// <summary>Gets the first own Field without an initializer when <see cref="ConstructorAvailability"/> is
    /// <see cref="ConstructorAvailability.MissingInitializer"/>.</summary>
    internal PropertyKoto? UninitializedField { get; private set; }

    // SPEC 6.2.3.6: decided from the merged, selected members every pass. Base construction requires its own verified invocation
    // plan, so a structure with a base receives no synthesized constructor yet.
    internal void PrepareImplicitConstructor(bool compilerManaged)
    {
        this.ImplicitConstructor = null;
        this.UninitializedField = null;
        var availability = compilerManaged ? ConstructorAvailability.CompilerManaged : ConstructorAvailability.Eligible;
        for (var i = 0; i < this.Members.Count; i++)
        {
            if (this.Members[i] is FunctionKoto { IsConstructor: true })
            {
                this.ConstructorAvailability = ConstructorAvailability.Explicit;
                this.UninitializedField = null;
                return;
            }

            if (availability == ConstructorAvailability.Eligible &&
                this.Members[i] is PropertyKoto { DeclarationKind: PropertyDeclarationKind.Let or PropertyDeclarationKind.Var, InitializerKoto: null } field)
            {
                availability = ConstructorAvailability.MissingInitializer;
                this.UninitializedField = field;
            }
        }

        this.ConstructorAvailability = availability;
        if (availability == ConstructorAvailability.Eligible && this.Bases.Count == 0)
        {
            this.ImplicitConstructor = this.implicitConstructor ??= new(this);
        }
    }

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        base.VisitChildrenCore(visitor);
        if (this.ImplicitConstructor is { } constructor)
        {
            visitor.Visit(constructor);
        }
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Struct;

    /// <inheritdoc/>
    public override TokenKind TokenKind => TokenKind.Struct;

    /// <inheritdoc/>
    public override bool IsInstantiable => true;

    /// <inheritdoc/>
    public override bool SupportsGenerics => true;

    /// <inheritdoc/>
    public override bool SupportsOrigins => true;

    /// <inheritdoc/>
    public override bool SupportsTypeConstraints => true;

    /// <summary>Initializes a new instance of the <see cref="StructKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The declaration source span.</param>
    public StructKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    internal StructKoto(CodeContext codeContext, TokenContext state, SourceSpan range)
        : base(codeContext, state, range)
    {
    }

    /// <inheritdoc/>
    public override void Parse(ref TokenReader reader)
        => this.ParseMembers(ref reader, parseTypeConstraints: true, parseDeclarationContainers: true);
}
