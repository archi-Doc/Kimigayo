// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class InheritedObjectCallTest(ITestOutputHelper output)
{
    private const string Types = "open struct Base\n    public var value: i32 = 7\n    public func read(self: objref/Self) -> i32 => self.value\n    public func view(self: objref/Self) -> ref/i32 => self.value@ref\nstruct Leaf<T> : Base\n    public init() => ()\n";

    [Theory]
    [InlineData("Owner", "let owner = Leaf<i32>.init()@obj\nrequire owner.read() == 7 else => $abort(\"owner\")")]
    [InlineData("Rc", "let owner = Leaf<i32>.init()@rc\nrequire owner.read() == 7 else => $abort(\"rc\")")]
    [InlineData("Arc", "let owner = Leaf<i32>.init()@arc\nrequire owner.read() == 7 else => $abort(\"arc\")")]
    [InlineData("Ref", "let owner = Leaf<i32>.init()@obj\nlet r = owner@objref\nrequire r.read() == 7 else => $abort(\"ref\")")]
    [InlineData("Uniq", "var owner = Leaf<i32>.init()@obj\nlet r = owner@objuniq\nrequire r.read() == 7 else => $abort(\"uniq\")")]
    [InlineData("Result", "let owner = Leaf<i32>.init()@obj\nlet r = owner.view()\nrequire r == 7 else => $abort(\"result\")")]
    [InlineData("Erased", "let owner = Leaf<i32>.init()@obj\nlet r = owner@objref/Base\nrequire r.read() == 7 else => $abort(\"erased\")")]
    [InlineData("Tuple", "let owner = (Leaf<i32>.init()@obj, 1)\nrequire owner.0.read() == 7 else => $abort(\"tuple\")")]
    [InlineData("Stored", "func read(r: ref/(obj/Leaf<i32>)) -> i32 => r.read()\nlet owner = Leaf<i32>.init()@obj\nrequire read(owner@ref) == 7 else => $abort(\"stored\")")]
    [InlineData("StoredView", "func read(r: ref/(objref/Leaf<i32> during a)) -> i32 => r.read()\nlet owner = Leaf<i32>.init()@obj\nlet view = owner@objref\nrequire read(view@ref) == 7 else => $abort(\"stored view\")")]
    [InlineData("Generic", "func read<T>(r: objref/Leaf<T>) -> i32\n    T is Owned\n    return r.read()\nlet owner = Leaf<i32>.init()@obj\nrequire read(owner@objref) == 7 else => $abort(\"generic\")")]
    public void SharedObjectBaseCallsUseTheOriginalHeaderAndLoan(string name, string body)
        => ScalarEmissionTest.EmitFixture("InheritedObject" + name, Types + body, string.Empty);

    [Fact]
    public void DirectBaseCallsAndCapturesUseTheSameObjectProjection()
    {
        const string Source = "open struct Base\n    public var value: i32 = 7\n    public func read(self: objref/Self) -> i32 => self.value\nstruct Leaf : Base\n    public init() => ()\n    public func readAgain(self: objref/Self) -> i32\n        let f = func[self]() -> i32 => base.read() + 1\n        return f()\nlet owner = Leaf.init()@obj\nrequire owner.readAgain() == 8 else => $abort(\"base\")";
        NativeAllocationAudit.WriteFixture("InheritedObjectBaseCapture", Source, 1, 1, 20);
    }

    [Fact]
    public void UnboundCallsStillRequireAnExplicitBaseView()
    {
        var c = MinimalEmissionTest.Analyze(Types + "func read(r: objref/Leaf<i32>) -> i32 => Base.read(r)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(DiagnosticCode.NoApplicableOverload_Kd, Assert.Single(c.Binding.Issues).Code);
        ScalarEmissionTest.EmitFixture("InheritedObjectUnbound", Types + "func read(r: objref/Leaf<i32>) -> i32 => Base.read(r@objref/Base)\nlet owner = Leaf<i32>.init()@obj\nrequire read(owner@objref) == 7 else => $abort(\"unbound\")", string.Empty);
    }

    [Fact]
    public void TransitiveObjectBaseCallsKeepTheCompleteBoundPath()
        => ScalarEmissionTest.EmitFixture("InheritedObjectTransitive", Types.Replace("struct Leaf<T> : Base", "open struct Middle : Base\n    protected init() => ()\nstruct Leaf<T> : Middle", StringComparison.Ordinal) + "let owner = Leaf<i32>.init()@obj\nrequire owner.read() == 7 else => $abort(\"transitive\")", string.Empty);

    [Theory]
    [InlineData("func bad<T>(r: objref/Leaf<T>) -> i32 => r.read()", "UnprovenConstraint_Kd")]
    [InlineData("func bad(n: ref/i32, r: objref/Leaf<ref/i32 during n>) -> i32 => r.read()", "UnprovenConstraint_Kd")]
    public void ErasureRequiresOwnedAfterSelectingTheMember(string source, string code)
    {
        var path = Path.GetFullPath("object-base-erasure.kimi");
        var c = MinimalEmissionTest.Analyze(Types + source, path);
        c.Binding.ReportDiagnostics();
        var shown = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(code, shown.Code);
        Assert.Equal("r.read()", shown.Text);
        Assert.Contains("Owned", shown.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("NoApplicable", shown.Code, StringComparison.Ordinal);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Proof, record.Category);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("Owned", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains("Owned", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }

        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void ExactViewTargetsNeedNoOwnedErasure()
    {
        var source = "struct Box<T>\n    public let item: T\n    public init(item: T) => self.item = item@move\n    public func read(self: objref/Self) -> i32 => 7\nfunc read<T>(r: objref/Box<T>) -> i32 => r.read()\nlet n: i32 = 1\nlet owner = Box<ref/i32>.init(n@ref)@obj\nrequire read(owner@objref) == 7 else => $abort(\"exact\")";
        ScalarEmissionTest.EmitFixture("InheritedObjectExact", source, string.Empty);
    }

    [Fact]
    public void EditingTheOwnedPremiseRevokesAndRebuildsErasureEvidence()
    {
        const string Operation = "func read<T>(r: objref/Leaf<T>) -> i32\n    T is Owned\n    return r.read()";
        var c = MinimalEmissionTest.Analyze(Types + Operation);
        var clause = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<IsKoto>());
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.Method is MemberAccessKoto);
        var original = clause.Right;
        var donor = MinimalEmissionTest.Analyze(Types + Operation.Replace("T is Owned", "T is Copy", StringComparison.Ordinal));
        var changed = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<IsKoto>()).Right;
        Assert.True(KotoHelper.Replace(clause, original, changed));
        Assert.Null(call.CallOf());
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(DiagnosticCode.UnprovenConstraint_Kd, Assert.Single(c.Binding.Issues).Code);
        Assert.True(KotoHelper.Replace(clause, changed, original));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.NotNull(call.CallOf());
    }

    [Fact]
    public void ALocalPayloadBorrowCannotBeHiddenByErasure()
    {
        var source = Types.Replace("public init() => ()", "public let item: T\n    public init(item: T) => self.item = item@move", StringComparison.Ordinal) + "let n: i32 = 1\nlet owner = Leaf<ref/i32>.init(n@ref)@obj\n_ = owner.read()";
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var shown = Assert.Single(TestDiagnostics.Of(c));
        // The inferred local Origin has no static proof at Binding's erasure deadline.
        Assert.Equal("UnprovenConstraint_Kd", shown.Code);
        Assert.Equal("owner.read()", shown.Text);
        Assert.Contains("Owned", shown.Explanation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("owner = Leaf<i32>.init()@obj")]
    [InlineData("owner.value = 9")]
    public void ReturnedLoansStillProtectTheCompletePayload(string update)
    {
        var records = DiagnosticCorpus.Check(Types + "var owner = Leaf<i32>.init()@obj\nlet r = owner.view()\n" + update + "\n_ = r").Diagnostics;
        Assert.Equal("ComparisonLoanConflict_Kd", Assert.Single(records).Code);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmObjectBaseCallsReuseAllPlans()
    {
        var c = MinimalEmissionTest.Analyze(Types + "let owner = Leaf<i32>.init()@obj\nlet r = owner.view()\n_ = r\n_ = owner.read()");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }
}
