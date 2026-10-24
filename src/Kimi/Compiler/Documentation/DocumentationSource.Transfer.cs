// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Documentation;

public sealed partial class DocumentationSource
{
    internal void RemoveDeclarations(Func<Koto, bool> contains)
        => this.comments.RemoveAll(x => x.Declaration is { } declaration && contains(declaration));

    // Syntax owns moved-node membership. Copy only those associations, retaining source locations and link bases;
    // mutable comments and optional-failure ownership belong to the recipient. The donor stays unchanged.
    internal DocumentationSource? CopyDeclarations(Kotonoha destination, Func<Koto, bool> contains)
    {
        DocumentationSource? result = null;
        foreach (var comment in this.comments)
        {
            if (comment.Declaration is not { } declaration || !contains(declaration))
            {
                continue;
            }

            result ??= new(this.Source, destination.Compilation.CollectHover ? destination.Compilation : null)
            {
                LogicalName = this.LogicalName,
                ModId = this.ModId,
                AdditionOrder = this.AdditionOrder,
                hasParseErrors = this.hasParseErrors,
                failed = this.failed,
            };
            result.comments.Add(new(result, comment.Span, comment.Indent, comment.IsRecognized)
            {
                Declaration = declaration,
                DeclarationSpan = comment.DeclarationSpan,
                IsSelected = comment.IsSelected,
                IsSuppressed = comment.IsSuppressed,
            });
        }

        return result;
    }
}
