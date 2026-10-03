// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ObjectRuntimeFieldReadTest(ITestOutputHelper output)
{
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void OwnedHandlesReadPayloadFieldsAndRetainTheirOwner()
    {
        const string Source = """
            struct Item
                public let id: i32 = 7
                public let pair: (i32, i32) = (11, 13)
                public let name: string = "item"
                drop => Console.writeLine("drop")
            func make() -> obj/Item
                Console.writeLine("make")
                return Kimi.Intrinsics.makeObj(Item.init())
            let first = make()
            require first.id == 7 else => $abort("id")
            let copy = first.pair
            require copy.0 == 11 and first.pair.1 == 13 else => $abort("pair")
            Console.writeLine(first.name)
            let view = first@objref
            Console.writeLine(view.name)
            require make().id == 7 else => $abort("temporary")
            """;
        // Each allocation has a 16-byte header and a 40-byte payload (i32, pair, alignment, 24-byte string).
        NativeAllocationAudit.WriteFixture("ObjectRuntimeFieldReadOwned", Source, 2, 2, 112, "make\nitem\nitem\nmake\ndrop\ndrop\n");
    }

    [Theory]
    [InlineData("owner.id = 9")]
    [InlineData("owner.id += 1")]
    public void ImmutableOwnersRejectDirectWrites(string operation)
    {
        var text = "struct Item\n    public var id: i32 = 7\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\n" + operation;
        var path = Path.GetFullPath("object-field-write.kimi");
        var c = MinimalEmissionTest.Analyze(text, path);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("InvalidAssignment_Kd", error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("owner.id", text.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DirectReadsRespectAnOutstandingExclusiveView()
    {
        var c = MinimalEmissionTest.Analyze("struct Item\n    public let id: i32 = 7\nvar owner = Kimi.Intrinsics.makeObj(Item.init())\nlet view = owner@objuniq\nrequire owner.id == 7 else => $abort(\"read\")\n_ = view@move");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmOwnedFieldAnalysisAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("struct Item\n    public let id: i32 = 7\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nrequire owner.id == 7 else => $abort(\"id\")");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(0, AllocationMeasurement.Measure(
            () =>
            {
                if (!c.Ownership.Analyze().IsVerified)
                {
                    throw new InvalidOperationException("Object field analysis failed.");
                }
            },
            iterations: 64,
            warmupIterations: 32));
    }

    [Fact]
    public void AFieldBorrowProtectsTheOwnerFromTransfer()
    {
        var c = MinimalEmissionTest.Analyze("struct Item\n    public let id: i32 = 7\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet field = owner.id@ref\nlet moved = owner@move\nrequire field == 7 else => $abort(\"field\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }
}
