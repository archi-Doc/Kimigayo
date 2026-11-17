// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// G949 S1-r2's rule (c) (the S1-r2 note §4; BU-219's readiness bullet with PLAN G949 S1-r1b): in a program pass, some readers read a
// library declaration's fact before its gate (a DirectBase query, a header, a Type), which keeps today's timing only while no library
// subject takes the gated branch. These Facts state that after a Bind of the empty program, so a library change that would take it fails
// here instead of answering final in a program pass.
public partial class KimiLibraryShapeTest
{
    // F1 (the note's readers 1-3): no library struct declares a base clause.
    [Fact]
    public void NoLibraryStructDeclaresABaseClause()
        => Assert.Empty(KotoTree.Walk(Bound(string.Empty).Library.Kotonoha.RootKoto).OfType<StructKoto>().Where(static x => x.BaseClause is not null).Select(static x => Line(x)));

    // F2 (the note's readers 13 and 14; review-r2note C1): no projection node of the library's Type syntax is pending, read on the
    // projection itself (the node's Right, written before NormalizedProjection gives the outer node its normalized Type) through the
    // readers' own predicate (PendingAssociatedType). An `associate` clause's name is not Type syntax (the note §4) and is left out: the
    // clause's head, its Left or the head inside its Origin application or transparent Type wrappers, which the associate binder completes
    // with the projection (BindAssociatedName); every other node under the Left stays checked.
    [Fact]
    public void NoLibraryTypeProjectionIsPending()
    {
        var c = Bound(string.Empty);
        var root = c.Library.Kotonoha.RootKoto;
        var names = KotoTree.Walk(root).OfType<IsKoto>().Where(static x => x.IsAssociatedConstraint).Select(static x => Head(x.Left)).ToHashSet();
        Assert.Empty(KotoTree.Walk(root).OfType<MemberAccessKoto>()
            .Where(x => !names.Contains(x) && c.Binding.Semantics.TypeOf(x.Right) is { Kind: BoundTypeKind.AssociatedProjection } projection && c.Binding.PendingAssociatedType(projection))
            .Select(static x => Line(x)));

        static Koto Head(Koto node) => node switch
        {
            OriginApplicationKoto applied => Head(applied.Type),
            TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner } => Head(inner),
            _ => node,
        };
    }
}
