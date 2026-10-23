// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Parses the one named target and optional negation of a runtime is test (SPEC 13.6.4).
// Input: the consumed is token, its left operand and the remaining tokens. Output: the same expression's syntax.
// The expression context selects this form before lookup; a target never consumes the following logical operator.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public static partial class Parser
{
    private static IsKoto ParseRuntimeTypeTest(ref TokenReader reader, Token token, Koto left)
    {
        var negated = reader.TryConsume(TokenKind.Not);
        var right = ParseConstraintSubject(ref reader);
        if (OpensAdjacentTypeArguments(ref reader, right.Span))
        {
            right = ParseGenericsPostfix(ref reader, right);
        }

        var test = (IsKoto)KotoHelper.NewBinaryKoto(ref reader, token, left, right);
        test.IsRuntimeTest = true;
        test.IsNegated = negated;
        return test;
    }
}
