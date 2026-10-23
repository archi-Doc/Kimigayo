// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Parses the one named target and optional negation of a runtime is test (SPEC 13.6.4).
// Input: the consumed is token, its left operand and the remaining tokens. Output: the same expression's syntax.
// The expression context selects this form before lookup; a target never consumes the following logical operator.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public static partial class Parser
{
    [ThreadStatic]
    private static WrittenOriginVisitor? writtenOriginVisitor;

    // SPEC 13.6.4 applies to written syntax, not normalized Types: an unannotated function Type has implicit Origins.
    // Binding uses the same query once on each resolved alias target; the reused visitor retains no source after a query.
    internal static Koto? WrittenOrigin(Koto syntax)
    {
        var visitor = writtenOriginVisitor ??= new();
        try
        {
            visitor.Visit(syntax);
            return visitor.Found;
        }
        finally
        {
            visitor.Found = null;
        }
    }

    private static IsKoto ParseRuntimeTypeTest(ref TokenReader reader, Token token, Koto left)
    {
        var negated = reader.TryConsume(TokenKind.Not);
        var right = ParseNamedCoreType(ref reader);

        var test = (IsKoto)KotoHelper.NewBinaryKoto(ref reader, token, left, right);
        test.IsRuntimeTest = true;
        test.IsNegated = negated;
        return test;
    }

    private static Koto ParseNamedCoreType(ref TokenReader reader)
    {
        var target = ParseConstraintSubject(ref reader);
        if (OpensAdjacentTypeArguments(ref reader, target.Span))
        {
            target = ParseGenericsPostfix(ref reader, target);
        }

        if (WrittenOrigin(target) is { } written)
        {
            RecoverItem(ref reader, target, written.Unexpected(SyntaxForm.RuntimeTypeTestOrigin));
        }

        return target;
    }

    private sealed class WrittenOriginVisitor : KotoVisitor
    {
        internal Koto? Found { get; set; }

        public override void Visit(Koto node)
        {
            if (this.Found is not null)
            {
                return;
            }

            if (node is TypeSemanticsKoto { HasOrigin: true } or OriginApplicationKoto)
            {
                this.Found = node;
            }
            else
            {
                node.VisitChildren(this);
            }
        }
    }
}
