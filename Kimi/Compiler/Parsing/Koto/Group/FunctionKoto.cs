// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

public sealed record FunctionParameterKoto(string ExternalName, string InternalName, bool IsOptional, Koto Type, Koto? DefaultValue);

[TinyhandObject]
public partial class FunctionKoto : IdentifiableKoto, ITokenParser
{
    public override KotoKind Akind => KotoKind.Function;

    [Key(2)]
    public ModifierKind Modifier { get; private set; }

    [Key(3)]
    public string Name { get; private set; }

    [IgnoreMember]
    public IReadOnlyList<GenericTypeSemanticsPair> GenericArguments { get; }

    [IgnoreMember]
    public IReadOnlyList<FunctionParameterKoto> Parameters { get; }

    [IgnoreMember]
    public Koto? ReturnType { get; }

    [IgnoreMember]
    public bool IsExcluded { get; }

    public FunctionKoto(ref TokenReader reader, TokenContext context, SourceRange range, string name, List<GenericTypeSemanticsPair>? genericArguments, List<FunctionParameterKoto> parameters, Koto? returnType)
        : base(ref reader, range)
    {
        this.AttributeChain = context.AttributeKoto;
        this.Modifier = context.ModifierKind;
        this.IsExcluded = context.IsExcluded;
        this.Name = name;
        this.GenericArguments = genericArguments ?? [];
        this.Parameters = parameters;
        this.ReturnType = returnType;

        foreach (var argument in this.GenericArguments)
        {
            argument.typeKoto.Parent = this;
            if (argument.semanticsKoto is not null)
            {
                argument.semanticsKoto.Parent = this;
            }
        }

        foreach (var parameter in parameters)
        {
            parameter.Type.Parent = this;
            if (parameter.DefaultValue is not null)
            {
                parameter.DefaultValue.Parent = this;
            }
        }

        if (returnType is not null)
        {
            returnType.Parent = this;
        }
    }

    public override ReadOnlySpan<char> GetIdentifier()
        => this.Name;

    public void Parse(ref TokenReader reader)
    {
        var depth = 1;
        while (reader.CanRead)
        {
            var kind = reader.CurrentTokenKind;
            reader.Advance();
            if (kind == TokenKind.StartBlock)
            {
                depth++;
            }
            else if (kind == TokenKind.EndBlock && --depth == 0)
            {
                return;
            }
        }
    }

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            KotoParser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendLineFeed);
        }

        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append("func ");
        builder.Append(this.Name);

        if (this.GenericArguments.Count > 0)
        {
            builder.Append('<');
            for (var i = 0; i < this.GenericArguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                var argument = this.GenericArguments[i];
                if (argument.semanticsKoto is not null)
                {
                    argument.semanticsKoto.WriteTo(ref builder);
                    builder.Append(' ');
                }

                argument.typeKoto.WriteTo(ref builder);
            }

            builder.Append('>');
        }

        builder.Append('(');
        for (var i = 0; i < this.Parameters.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendCommaAndSpace();
            }

            var parameter = this.Parameters[i];
            builder.Append(parameter.ExternalName);
            if (parameter.IsOptional)
            {
                builder.Append('?');
            }

            if (!parameter.ExternalName.Equals(parameter.InternalName, StringComparison.Ordinal))
            {
                builder.Append(" => ");
                builder.Append(parameter.InternalName);
            }

            builder.Append(": ");
            parameter.Type.WriteTo(ref builder);
            if (parameter.DefaultValue is not null)
            {
                builder.Append(" = ");
                parameter.DefaultValue.WriteTo(ref builder);
            }
        }

        builder.Append(')');
        if (this.ReturnType is not null)
        {
            builder.Append(" -> ");
            this.ReturnType.WriteTo(ref builder);
        }
    }
}
