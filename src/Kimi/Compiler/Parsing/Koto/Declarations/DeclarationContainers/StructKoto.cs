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

    /// <summary>Gets the synthesized constructor once it exists: at once without a base, and for a structure with a base only
    /// after its omitted base clause selects a base constructor (SPEC 6.2.3.6).</summary>
    internal FunctionKoto? ImplicitConstructor => this.ImplicitConstructorPending ? null : this.SynthesizedConstructor;

    /// <summary>Gets the synthesized constructor the declarations admit, available or still pending; the indexer visits it.</summary>
    internal FunctionKoto? SynthesizedConstructor { get; private set; }

    /// <summary>Gets a value indicating whether <see cref="SynthesizedConstructor"/> waits for its omitted base selection.</summary>
    internal bool ImplicitConstructorPending { get; private set; }

    internal ConstructorAvailability ConstructorAvailability { get; private set; }

    /// <summary>Gets the first own Field without an initializer when <see cref="ConstructorAvailability"/> is
    /// <see cref="ConstructorAvailability.MissingInitializer"/>.</summary>
    internal PropertyKoto? UninitializedField { get; private set; }

    // SPEC 6.2.3.6: decided from the merged, selected members every pass. A structure with a base keeps its synthesized constructor
    // pending until Binding completes the omitted base selection (CompleteImplicitConstructor).
    internal void PrepareImplicitConstructor(bool compilerManaged)
    {
        this.SynthesizedConstructor = null;
        this.ImplicitConstructorPending = false;
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
        if (availability == ConstructorAvailability.Eligible)
        {
            this.SynthesizedConstructor = this.implicitConstructor ??= new(this);
            this.ImplicitConstructorPending = this.Bases.Count != 0;
        }
    }

    /// <summary>Ends the pending state: the constructor exists when the omitted base clause selected a base constructor, and is
    /// withdrawn otherwise.</summary>
    /// <param name="exists">Whether the omitted base clause selected a base constructor.</param>
    internal void CompleteImplicitConstructor(bool exists)
    {
        this.ImplicitConstructorPending = false;
        if (!exists)
        {
            this.SynthesizedConstructor = null;
        }
    }

    protected override void ForEachChildSlot(ref ChildSlots slots)
    {
        base.ForEachChildSlot(ref slots);
        slots.Fixed(this.SynthesizedConstructor);
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
