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

    public override void WriteTo(StringWriter writer)
    {
        this.Method.WriteTo(writer);
        writer.Write(Constants.OpenParenthesisChar);
        for (var i = 0; i < this.Arguments.Count; i++)
        {
            this.Arguments[i].WriteTo(writer);
            if (i < (this.Arguments.Count - 1))
            {
                writer.Write(Constants.CommaChar);
                writer.Write(Constants.SpaceChar);
            }
        }

        writer.Write(Constants.CloseParenthesisChar);
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}", [this.Method, .. this.Arguments]);
    }
}
