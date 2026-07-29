// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class InvocationKoto : Koto
{
    [Key(1)]
    public Koto Method { get; private set; }

    [Key(2)]
    public List<Koto> Arguments { get; private set; }

    public InvocationKoto(ref TokenReader reader, Koto method, List<Koto> arguments)
        : base(ref reader, default)
    {
        this.Method = method;
        this.Arguments = arguments;

        if (arguments.Count == 0)
        {
            this.Range = method.Range;
        }
        else
        {
            this.Range = new(method.Range.Start, arguments[^1].Range.End);
        }

        method.Parent = this;
        foreach (var x in arguments)
        {
            x.Parent = this;
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.Append(this.Method.ToString());
        sb.Append(Constants.OpenParenthesisChar);
        for (var i = 0; i < this.Arguments.Count; i++)
        {
            sb.Append(this.Arguments[i].ToString());
            if (i < (this.Arguments.Count - 1))
            {
                sb.Append(Constants.CommaChar);
                sb.Append(Constants.SpaceChar);
            }
        }

        sb.Append(Constants.CloseParenthesisChar);

        return sb.ToString();
    }

    public override void WriteTo(ref IndentWriter writer)
    {
        this.Method.WriteTo(writer);
        writer.Append(Constants.OpenParenthesisChar);
        for (var i = 0; i < this.Arguments.Count; i++)
        {
            this.Arguments[i].WriteTo(writer);
            if (i < (this.Arguments.Count - 1))
            {
                writer.Append(Constants.CommaChar);
                writer.Append(Constants.SpaceChar);
            }
        }

        writer.Append(Constants.CloseParenthesisChar);
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}", [this.Method, .. this.Arguments]);
    }
}
