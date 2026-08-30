// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a function type.
/// </summary>
[TinyhandObject]
public sealed partial class FunctionTypeKoto : TypeKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.FunctionType;

    /// <summary>Gets the parameter type expression.</summary>
    [Key(1)]
    public Koto Parameters { get; private set; }

    /// <summary>Gets the return type.</summary>
    [Key(2)]
    public Koto ReturnType { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="FunctionTypeKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="parameters">The parameter type expression.</param>
    /// <param name="returnType">The return type.</param>
    public FunctionTypeKoto(ref TokenReader reader, SourceSpan range, Koto parameters, Koto returnType)
        : base(ref reader, range)
    {
        this.Parameters = parameters;
        this.ReturnType = returnType;
        parameters.Parent = this;
        returnType.Parent = this;
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
    {
        this.Parameters.Bind(compilation);
        this.ReturnType.Bind(compilation);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Parameters.WriteTo(ref builder);
        builder.Append(" -> ");
        this.ReturnType.WriteTo(ref builder);
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.Parameters;
        yield return this.ReturnType;
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Parameters == oldKoto)
        {
            this.Parameters = newKoto;
        }
        else if (this.ReturnType == oldKoto)
        {
            this.ReturnType = newKoto;
        }
        else
        {
            return false;
        }

        return true;
    }
}
