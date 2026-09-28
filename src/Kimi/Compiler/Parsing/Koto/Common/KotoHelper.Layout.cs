// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public static partial class KotoHelper
{
    [ThreadStatic]
    private static BodyVisitor? bodyVisitor;

    internal static bool ContainsBody(Koto node)
    {
        var visitor = bodyVisitor ??= new();
        visitor.Found = false;
        visitor.Visit(node);
        return visitor.Found;
    }

    // Reuse the iterator-free traversal without retaining any source node.
    private sealed class BodyVisitor : KotoVisitor
    {
        internal bool Found { get; set; }

        public override void Visit(Koto node)
        {
            if (node is CodeBlockKoto or FunctionKoto or MatchKoto or CompileTimeSwitchKoto)
            {
                this.Found = true;
            }
            else if (!this.Found)
            {
                node.VisitChildren(this);
            }
        }
    }
}
