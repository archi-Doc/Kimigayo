// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>Declarations of the embedded Kimi library sources, and the symbols the library registers for them.</summary>
internal static class LibrarySource
{
    /// <summary>Gets the one library declaration a path names, for a declaration without a lang item: its containers from the root,
    /// then its own name, a nested declaration's or a member function's (none of a conditional conformance block's).</summary>
    /// <param name="library">The compilation's library.</param>
    /// <param name="path">The dotted path, such as <c>Storage.indexBounds</c>.</param>
    /// <returns>The declaration.</returns>
    internal static Kimi.Compiler.Parsing.Koto Declaration(KimiLibrary library, string path)
    {
        var names = path.Split('.');
        Kimi.Compiler.Parsing.DeclarationContainerKoto container = library.Kotonoha.RootKoto;
        for (var i = 0; i < names.Length - 1; i++)
        {
            container = Assert.Single(container.NestedContainers, x => x.Name == names[i]);
        }

        return Assert.Single(
            container.NestedContainers.Where(x => x.Name == names[^1]).Cast<Kimi.Compiler.Parsing.Koto>()
                .Concat(container.Members.OfType<Kimi.Compiler.Parsing.FunctionKoto>().Where(x => x.Name == names[^1])));
    }

    /// <summary>Gets the symbol the library registers for the declaration a path names (<see cref="Declaration"/>).</summary>
    /// <param name="library">The compilation's library.</param>
    /// <param name="path">The dotted path.</param>
    /// <returns>The registered symbol.</returns>
    internal static BindingSymbol Symbol(KimiLibrary library, string path)
    {
        var declaration = Declaration(library, path);
        return Assert.Single(library.RegisteredSymbols.ToArray(), x => ReferenceEquals(x.Declaration, declaration));
    }
}
