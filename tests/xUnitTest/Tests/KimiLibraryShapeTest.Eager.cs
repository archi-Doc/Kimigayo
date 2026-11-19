// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections;
using System.Reflection;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// PLAN G949 Stage 2 U2b-1 (design u2-design 2.7; rows e1 and e2; labels back\notes\l1-s2\u2b-impl\labels.md): the library's remaining lazy
// facts are made when the library is made, so a later pass or reader writes none on a library object (the eager remainder beside S1-1's
// B1 steps, k4).
public partial class KimiLibraryShapeTest
{
    // e1 (D-4; O-U2-4, the Front's C-380 16:55; C-380 F3 (b)): after the empty program's Bind every non-Not constraint of the layer's level
    // (PLAN G949 Stage 2 U4), the library pass's, has its negation (a Not constraint's is its operand, never stored). Before U2b a negation
    // was formed only where a pass used it.
    [Fact]
    public void EveryLibraryConstraintHasItsNegation()
    {
        var c = Bound(string.Empty);
        var constraints = c.Library.Layer.Constraints.Values.ToArray();
        Assert.NotEmpty(constraints);
        Assert.Empty(constraints.Where(static x => x.Kind != ConstraintKind.Not && x.Negation is null).Select(static x => x.ToString()));
    }

    // e2 (D-C1): every library document's logical name is in the library module's memo once the library is made, so no reader of a library
    // node's source name writes it. Before U2b only a compilation that collected documentation formed them all.
    [Fact]
    public void EveryLibraryDocumentHasItsLogicalName()
    {
        var c = Bound(string.Empty);
        var names = (IDictionary)typeof(Kotonoha).GetField("names", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Library.Kotonoha)!;
        var documents = c.Library.Kotonoha.Syntax.Nodes.ToArray().OfType<Koto>().Select(static x => x.CodeContext?.SourceDocument).OfType<SourceDocument>()
            .Distinct<SourceDocument>(ReferenceEqualityComparer.Instance).ToArray();
        Assert.NotEmpty(documents);
        Assert.All(documents, x => Assert.True(names.Contains(x), $"{x.Path} has no logical name in the memo"));
    }
}
