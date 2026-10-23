// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Documentation;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Kotonoha
{
    // The recipient replaces previous associations of the moved declarations with prepared, independently owned snapshots.
    // Retire empty fragments so repeated round trips neither duplicate comments nor grow the registry.
    internal void ReplaceDocumentation(IEnumerable<DocumentationSource> replacements, Func<Koto, bool> contains)
    {
        if (this.documentationSources is { } sources)
        {
            foreach (var source in sources)
            {
                source.RemoveDeclarations(contains);
            }

            sources.RemoveAll(static x => x.Comments.Count == 0);
        }

        foreach (var replacement in replacements)
        {
            this.RecordDocumentation(replacement);
        }
    }
}
