// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

internal interface ITokenParser
{
    void Parse(ref TokenReader reader);
}
