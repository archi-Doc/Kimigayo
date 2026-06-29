// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class ConditionAndKoto : Koto
{
    [Key(1)]
    public Koto Left { get; private set; }

    [Key(2)]
    public Koto Right { get; private set; }

    public ConditionAndKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range)
    {
        this.Left = left;
        this.Right = right;
        this.Left.Parent = this;
        this.Right.Parent = this;
    }

    public override string ToString()
        => $"({this.Left} and {this.Right})";

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
        this.Left.Parent = this;
        this.Right.Parent = this;
    }
}
