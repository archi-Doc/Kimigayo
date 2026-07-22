// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

[TinyhandObject]
public sealed partial class AliasKoto : Koto
{
    [Key(1)]
    public List<string> QualifiedName { get; private set; }

    public AliasKoto(ref TokenReader reader, List<string> alias)
        : base(ref reader, default)
    {
        this.QualifiedName = alias;
    }

    public override string ToString()
        => $"alias {string.Join(Constants.DotChar, this.QualifiedName)}";

    public override void WriteTo(StringWriter writer)
    {
        writer.Write("alias ");
        for (var i = 0; i < this.QualifiedName.Count; i++)
        {
            writer.Write(this.QualifiedName[i]);
            if (i < (this.QualifiedName.Count - 1))
            {
                writer.Write(Constants.DotChar);
            }
        }
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}({string.Join(Constants.DotChar, this.QualifiedName)})", default);
    }
}
