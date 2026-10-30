// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ObjectPayloadReadTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Obj", "makeObj")]
    [InlineData("Rc", "makeRc")]
    public void CopyReadsSharePayloadProjection(string name, string factory)
    {
        var source = $$"""
            let owner = Kimi.Intrinsics.{{factory}}(7)
            let snapshot = owner@follow
            require snapshot == 7 and owner@follow == 7 else => $abort("direct")
            let stored = (owner@move, 3)
            require stored.0@follow == 7 else => $abort("stored")
            require Kimi.Intrinsics.{{factory}}(9)@follow == 9 else => $abort("temporary")
            """;
        NativeAllocationAudit.WriteFixture("ObjectPayloadRead" + name, source, 2, 2, 40);
    }

    [Fact]
    public void SnapshotSurvivesOwnerDestruction()
    {
        const string Source = """
            func read() -> (i32, bool)
                let owner = Kimi.Intrinsics.makeObj((7, true))
                return owner@follow
            let snapshot = read()
            require snapshot.0 == 7 and snapshot.1 else => $abort("snapshot")
            """;
        NativeAllocationAudit.WriteFixture("ObjectPayloadReadSnapshot", Source, 1, 1, 24);
    }

    [Fact]
    public void GenericBorrowedViewsReadTheSameCompletePayload()
    {
        const string Source = """
            func read<T>(value: objref/T) -> T
                T is ObjectPayload and Sealed and Copy
                return value@follow
            var owner = Kimi.Intrinsics.makeObj(7)
            require read(owner@objref) == 7 else => $abort("generic")
            let exclusive = owner@objuniq
            require exclusive@follow == 7 else => $abort("exclusive")
            let shared = exclusive@objref
            require shared@follow == 7 else => $abort("shared")
            """;
        NativeAllocationAudit.WriteFixture("ObjectPayloadReadViews", Source, 1, 1, 20);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonCopyPayloadNeedsABorrowWithoutATransferRepair(bool borrow)
    {
        var path = Path.GetFullPath("object-payload-read.kimi");
        var source = "let owner = Kimi.Intrinsics.makeObj(\"text\")\nlet value = owner@follow" + (borrow ? "@ref" : string.Empty) + "\nConsole.writeLine(value)";
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        c.ReportAnalysis();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        if (borrow)
        {
            Assert.Empty(result.Diagnostics);
            Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
            return;
        }

        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("TransferRequired_Kd", error.Code);
        Assert.Equal("owner@follow", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Null(error.Repairs);
        Assert.False(c.Generate(TextWriter.Null));
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    // SPEC 15.1.5, 13.5.5: an object payload never offers Take, also a part selected through the handle; the handle itself and a
    // handle stored in an owned Field do.
    [Theory]
    [InlineData("func g(h: obj/P)\n    let y = h.inner@move", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("func g()\n    var h = P.init(1)@obj\n    let y = h.inner@move", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("func g(h: obj/P)\n    let y = h@follow.inner@move", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("func g()\n    var h = P.init(1)@obj\n    let y = h.v@move", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("func g()\n    var h = P.init(1)@obj\n    let y = h.inner.s@move", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("func g()\n    var h = P.init(1)@obj\n    consume(h.inner@move)", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("func g(h: obj/P)\n    let y = h@follow@move", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("func g(h: obj/P)\n    let y = h@move", null)]
    [InlineData("func g()\n    var h = P.init(1)@obj\n    let n = h.v", null)]
    [InlineData("func g(x: Box<obj/P>)\n    let y = x.item@move", null)]
    public void PayloadPartsOfferNoTake(string function, DiagnosticCode? code)
    {
        var c = MinimalEmissionTest.Analyze(
            "struct Q\n    public var w: i32\n    public var s: string\n    public init(w: i32)\n        self.w = w\n        self.s = \"a\"\n" +
            "struct P\n    public var v: i32\n    public var inner: Q\n    public init(v: i32)\n        self.v = v\n        self.inner = Q.init(v)\n" +
            "struct Box<E>\n    public var item: E\n    public init(item: E) => self.item = item@move\nfunc consume(q: Q) => ()\n" + function);
        Assert.Equal(code is { } expected ? [expected] : Array.Empty<DiagnosticCode>(), c.Binding.Issues.Select(x => x.Code));
    }

    [Fact]
    public void ReadingDoesNotBypassExclusiveLoans()
    {
        var c = MinimalEmissionTest.Analyze("var owner = Kimi.Intrinsics.makeObj(7)\nlet view = owner@objuniq\nlet snapshot = owner@follow\n_ = view@move");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, static x => x.Code.IsLoanConflict());
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Generate(TextWriter.Null));
    }

    // SPEC 13.5.5: exclusive payload access needs exclusive authority along the whole path, also to an objuniq view.
    [Fact]
    public void AnObjuniqViewReachedThroughASharedPathLendsNoExclusiveReceiver()
    {
        var source = "struct Counter\n    public var n: i32 = 0\n    public func bump(self: uniq/Self) => self.n += 1\nvar h = Kimi.Intrinsics.makeObj(Counter.init())\nlet u = h@objuniq\nlet r = u@ref\nr@follow.bump()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Generate(TextWriter.Null));
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.NoApplicableOverload_Kd), "r@follow.bump()"), (error.Code, source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmPayloadReadPlansAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("let owner = Kimi.Intrinsics.makeRc(7)\nrequire owner@follow == 7 else => $abort(\"read\")");
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
    }
}
