// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Documentation;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Verification;
using Xunit;

namespace XunitTest;

public sealed class HoverStateTest
{
    [Theory]
    [InlineData("short")]
    [InlineData("long-comment")]
    [InlineData("maximum-comment")]
    [InlineData("long-effects")]
    [InlineData("identity-dag")]
    [InlineData("wide-identity")]
    [Trait("Purpose", "Allocation")]
    public void MeasuredWorkloadsReuseTheCompleteResponseWithinBounds(string name)
    {
        var participants = HoverWorkloads.Create(name);
        var state = new HoverState(participants, true);
        using var text = new TextDocument(HoverWorkloads.Text);
        var first = state.Find(text, new(0, 8));
        Assert.NotNull(first.Body);
        Assert.InRange(first.Body.Length, 1, HoverLimits.Output);
        if (name == "maximum-comment")
        {
            Assert.Contains("Documentation truncated", first.Body);
        }

        for (var warm = 0; warm < 32; warm++)
        {
            state.Find(text, new(0, 8));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            state.Find(text, new(0, 8));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(1, state.CachedCount);
        Assert.Same(first.Body, state.Find(text, new(0, 8)).Body);
    }

    [Fact]
    public void TransientDocumentationFailuresAreRetriedButComparisonLimitsAreCached()
    {
        var entry = Entry();
        var declaration = entry.Info.Declarations[0];
        var bad = declaration.Documentation[0] with { Span = new(0, 100) };
        var state = State(entry with { Info = entry.Info with { Declarations = [declaration with { Documentation = [bad] }] } });
        using var text = new TextDocument("struct Sample\n");
        for (var i = 0; i < 2; i++)
        {
            var result = state.Find(text, new(0, 8));
            Assert.Contains("struct Sample", result.Body);
            Assert.Contains("Documentation unavailable: internal failure", result.Body);
            Assert.NotNull(result.Reason);
            Assert.Equal(0, state.CachedCount);
        }

        var left = entry with { Info = entry.Info with { TypeIdentity = new(new string('x', HoverLimits.Work), []) } };
        var right = entry with { Info = entry.Info with { TypeIdentity = new(new string('x', HoverLimits.Work), []) } };
        var limited = State(left, right);
        Assert.NotNull(limited.Find(text, new(0, 8)).Reason);
        Assert.Null(limited.Find(text, new(0, 8)).Body);
        Assert.Null(limited.Find(text, new(0, 8)).Reason);
        Assert.Equal(1, limited.CachedCount);
    }

    [Fact]
    public void UnseenHistoricalPositionsUseTheOldSnapshotWithUtf16AndCrLfRanges()
    {
        const string Text = "//😀\r\nstruct Sample\r\n";
        using var text = new TextDocument(Text);
        var original = new HoverDocument(new("main.kimi", Text), [Entry() with { Span = new(13, 6) }]);
        var state = new HoverState([new(new(SourceIdentity.FromPath("main.kimi"), UnitKind.Product, "test"), 1, original)], false);
        Assert.True(state.Edited(0, 0, 2));
        Assert.True(text.TryApply(new(0, 0), new(0, 0), "X\n"));
        var result = state.Find(text, new(2, 8));
        Assert.StartsWith(HoverRenderer.PreviousNotice, result.Body);
        Assert.Equal(new SourceRange(new(2, 7), new(2, 13)), result.Range);
        Assert.Null(state.Find(text, new(2, 100)).Body);
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void SessionDisposalReleasesIndexesEvenIfTheSessionObjectRemainsReferenced()
    {
        var (session, weak) = Disposed();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.TryGetTarget(out _));
        Assert.Empty(session.Units);
        Assert.Empty(session.Store.RevalidationTargets());
        GC.KeepAlive(session);
    }

    [Fact]
    public void IndependentIdentitiesAndAllDocumentationInputsMustAgree()
    {
        var a = Entry();
        var b = Entry();
        Assert.True(new HoverAgreement().Equal(a, b));
        Assert.False(new HoverAgreement().Equal(a, b with { Span = new(8, 6) }));
        Assert.False(new HoverAgreement().Equal(a, b with { Info = b.Info with { Copy = ConstraintProof.Refuted } }));
        Assert.False(new HoverAgreement().Equal(a, b with { Info = b.Info with { TypeIdentity = new("other binding", []) } }));
        var declaration = b.Info.Declarations[0];
        var documentation = declaration.Documentation[0];
        var variants = new[]
        {
            documentation with { Source = new("doc.kimi", "/// Differ!") },
            documentation with { Parameters = [new("U")] },
            documentation with { Placement = new(null, []) },
            documentation with { LogicalName = "other.kimi" },
            documentation with { DeclarationSpan = new(1, 2) },
        };
        foreach (var variant in variants)
        {
            var changed = b with { Info = b.Info with { Declarations = [declaration with { Documentation = [variant] }] } };
            Assert.False(new HoverAgreement().Equal(a, changed));
        }

        Assert.False(new HoverAgreement().Equal(a, b with { Info = b.Info with { Declarations = [declaration with { DocumentationNotice = "deferred" }] } }));
    }

    [Fact]
    public void SharedIdentitySubgraphsAreComparedWithoutExponentialExpansion()
    {
        var left = new HoverKey("leaf", []);
        var right = new HoverKey("leaf", []);
        for (var i = 0; i < 40; i++)
        {
            left = new("pair", [left, left]);
            right = new("pair", [right, right]);
        }

        Assert.True(new HoverAgreement().Equal(new(default, new([], TypeIdentity: left)), new(default, new([], TypeIdentity: right))));
    }

    [Fact]
    public void HistoricalMappingShiftsUntouchedTokensAndUsesOneCoherentBody()
    {
        using var text = new TextDocument("struct Sample\n");
        var state = State(Entry());
        var current = state.Find(text, new(0, 8));
        Assert.NotNull(current.Body);
        Assert.True(state.Edited(0, 0, 3));
        Assert.True(text.TryApply(new(0, 0), new(0, 0), "//\n"));
        var previous = state.Find(text, new(1, 8));
        Assert.Equal(HoverRenderer.PreviousNotice + "\n\n" + current.Body, previous.Body);
        Assert.Equal(new SourceRange(new(1, 7), new(1, 13)), previous.Range);
        Assert.Same(previous.Body, state.Find(text, new(1, 9)).Body);
        Assert.Null(state.Find(text, new(0, 0)).Body);
        Assert.Null(state.Find(text, new(1, 13)).Body);
    }

    [Theory]
    [InlineData(7, 7, "")]
    [InlineData(13, 13, "")]
    [InlineData(8, 9, "a")]
    [InlineData(6, 8, " S")]
    public void TouchingEitherBoundaryOrReplacingIdenticalTextForbidsHistory(int from, int to, string replacement)
    {
        using var text = new TextDocument("struct Sample\n");
        var state = State(Entry());
        Assert.True(state.Edited(from, to, replacement.Length));
        Assert.True(text.TryApply(new(0, from), new(0, to), replacement));
        Assert.Null(state.Find(text, new(0, 10)).Body);
        Assert.True(state.Find(text, new(0, 10)).Reason is null);
    }

    [Fact]
    public void DisagreementAbsenceAndResourceLimitsAreCachedWithoutHistoricalFallback()
    {
        using var text = new TextDocument("struct Sample\n");
        var left = Entry();
        var state = State(left, left with { Info = left.Info with { Copy = ConstraintProof.Unknown } });
        Assert.Null(state.Find(text, new(0, 8)).Body);
        Assert.Null(state.Find(text, new(0, 8)).Body);
        Assert.Equal(1, state.CachedCount);
        Assert.Null(state.Find(text, new(0, 0)).Body);
        Assert.Equal(2, state.CachedCount);
        var limited = State(left with { Info = left.Info with { Effects = new string('x', HoverLimits.Output) } });
        Assert.NotNull(limited.Find(text, new(0, 8)).Reason);
        Assert.Null(limited.Find(text, new(0, 8)).Reason);
        Assert.Equal(1, limited.CachedCount);
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void RepeatedCurrentAndHistoricalLookupAllocatesNothing()
    {
        using var text = new TextDocument("struct Sample\n");
        var state = State(Entry(), Entry());
        Assert.NotNull(state.Find(text, new(0, 8)).Body);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            state.Find(text, new(0, 8));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(state.Edited(0, 0, 1));
        Assert.True(text.TryApply(new(0, 0), new(0, 0), " "));
        Assert.NotNull(state.Find(text, new(0, 9)).Body);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            state.Find(text, new(0, 9));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void CacheAndHistoryStorageHaveFixedBounds()
    {
        var source = new SourceDocument("main.kimi", new string('x', 30_000));
        var entries = new HoverEntry[1200];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = new(new(i * 2, 1), new([new("Type", "P", new string('a', 2000), [], [])]));
        }

        using var text = new TextDocument(source.SourceText);
        var state = new HoverState([new(new(SourceIdentity.FromPath("main.kimi"), UnitKind.Product, "test"), 1, new(source, entries))], false);
        for (var i = 0; i < entries.Length; i++)
        {
            Assert.NotNull(state.Find(text, new(0, i * 2)).Body);
            Assert.InRange(state.CachedCount, 1, HoverLimits.CacheEntries);
            Assert.InRange(state.CachedCharacters, 1, HoverLimits.CacheCharacters);
        }

        for (var i = 0; i < HoverLimits.Edits; i++)
        {
            Assert.True(state.Edited(29_999, 29_999, 0));
        }

        Assert.False(state.Edited(29_999, 29_999, 0));
    }

    private static HoverEntry Entry()
    {
        var documentation = new HoverDocumentation(new("doc.kimi", "/// Comment"), new(0, 11), 0, "Project", "doc.kimi", null, 0, default, [new DocumentationMarkdownParameter("T")], new("C:/source", []));
        var declaration = new HoverDeclaration("Type", "Project", "struct Sample", [new("Project", "main.kimi", new(7, 6))], [documentation], Identity: new("Sample declaration", []));
        return new(new(7, 6), new([declaration], Copy: ConstraintProof.Proven, CopyType: "Sample", TypeIdentity: new("Sample", [new("binder", [])])));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (LspSession Session, WeakReference<HoverDocument> Index) Disposed()
    {
        var participants = HoverWorkloads.Create("long-comment");
        var workload = HoverWorkloads.Session(participants);
        workload.Session.Process(workload.Hover, 0);
        workload.Session.Process(workload.Edit, 0);
        workload.Dispose();
        return (workload.Session, new(participants[0].Document));
    }

    private static HoverState State(params HoverEntry[] entries)
    {
        var participants = new HoverParticipant[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            participants[i] = new(new(SourceIdentity.FromPath("main.kimi"), UnitKind.Product, i.ToString()), i + 1, new(new("main.kimi", "struct Sample\n"), [entries[i]]));
        }

        return new(participants, false);
    }
}
