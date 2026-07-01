// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public partial class ErrorKoto : Koto
{
    public ErrorKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
    }

    public override string ToString()
        => $"Error";
}
