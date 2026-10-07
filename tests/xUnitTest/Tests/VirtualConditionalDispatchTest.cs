// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class VirtualConditionalDispatchTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InapplicableSlotsKeepTheirIndexWithoutABodyDependency(bool block)
    {
        var source = Program(block);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.TryPrepare(out var module, out var failure), MinimalEmissionTest.Describe(c, failure));
        Assert.Equal(2, module.Objects.Count);
        Assert.All(module.Objects, item => Assert.Equal(3, item.VirtualSlots.Length));
        Assert.NotNull(module.Objects[0].VirtualSlots[1]);
        Assert.Null(module.Objects[1].VirtualSlots[1]);
        Assert.NotNull(module.Objects[1].VirtualSlots[2]);
        ScalarEmissionTest.EmitFixture("VirtualConditionalDispatch" + block, source, string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ItemsAndDirectBaseCallsKeepTheOriginalApplicability(bool block)
    {
        var source = Program(block).Replace("override func copy(self: objref/Self, value: ref/T) -> T => value@follow", "override func copy(self: objref/Self, value: ref/T) -> T => base.copy(value)", StringComparison.Ordinal);
        source += "\nlet f = Leaf<i32>.copy\nlet erased: (objref/Base<i32>, ref/i32) -> i32 = f\nrequire f(a@objref/Base<i32>, 8@ref) == 8 and erased(a@objref/Base<i32>, 9@ref) == 9 else => $abort(\"items\")";
        ScalarEmissionTest.EmitFixture("VirtualConditionalItems" + block, source, string.Empty);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AnInapplicableCallOrItemCannotReachAnEmptySlot(bool block, bool item)
    {
        var valid = Program(block);
        var c = MinimalEmissionTest.Analyze(valid + (item ? "\n_ = Leaf<NonCopy>.copy" : "\n_ = b.copy(NonCopy.init())"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.Span.Start >= valid.Length);
        using var ir = new StringWriter();
        Assert.False(c.Emission.WriteIr(ir, out _));
        Assert.Empty(ir.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryFunctionItemsUseTheSameClosedPremiseCheck(bool block)
    {
        var declarations = "contract Marker\nstruct NonCopy\n    drop => ()\nstruct Box<T>\n" + (block
            ? "    Self is Marker when T is Copy\n        public func copy(value: ref/T) -> T => value@follow\n"
            : "    public func copy(value: ref/T) -> T\n        T is Copy\n        return value@follow\n");
        ScalarEmissionTest.EmitFixture("VirtualConditionalOrdinaryItem" + block, declarations + "let f = Box<i32>.copy\nrequire f(7@ref) == 7 else => $abort(\"ordinary\")", string.Empty);
        var path = Path.GetFullPath("conditional-function-item.kimi");
        var source = declarations + "let f = Box<NonCopy>.copy";
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var shown = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnsatisfiedConstraint_Kd", shown.Code);
        Assert.Equal("Box<NonCopy>.copy", shown.Text);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Contains(record.Reason!, x => x.Name == "subject" && x.Value == "NonCopy");
        Assert.Contains(record.Reason!, x => x.Name == "constraint" && x.Value == "T is Copy");
        Assert.Equal("constraint", Assert.Single(record.Related!).Role);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(Assert.Single(result.Diagnostics).Display!.Range, sent.Range);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void InapplicableBodiesAddNoGrowingDescriptorDependencies()
    {
        var source = Program(false)
            .Replace("override func copy(self: objref/Self, value: ref/T) -> T => value@follow", "override func copy(self: objref/Self, value: ref/T) -> T\n        let child = Leaf<(T, T)>.init()@obj\n        return value@follow", StringComparison.Ordinal)
            .Replace("let a = Leaf<i32>.init()@obj\n", string.Empty, StringComparison.Ordinal)
            .Replace("a.copy(7) == 7 and a.last() == 30 and ", string.Empty, StringComparison.Ordinal);
        ScalarEmissionTest.EmitFixture("VirtualConditionalNoDependency", source, string.Empty);
    }

    [Fact]
    public void UnknownReferencePremisesRemainProofErrors()
    {
        const string Source = "struct Box<T>\n    public func copy(value: ref/T) -> T\n        T is Copy\n        return value@follow\nfunc use<T>()\n    _ = Box<T>.copy\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        c.Binding.ReportDiagnostics();
        var record = Assert.Single(c.Diagnostics.Finalize().Diagnostics);
        Assert.Equal("UnprovenConstraint_Kd", record.Code);
        Assert.Equal(DiagnosticCategory.Proof, record.Category);
        Assert.Contains(record.Reason!, x => x.Name == "subject" && x.Value == "T");
        Assert.Contains("cannot be proven", record.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public void EditingAPremiseRevokesAndRebuildsTheTable()
    {
        const string Source = "open struct Base<T>\n    T is Owned\n    public init() => ()\n    public virtual func read(self: objref/Self) -> i32\n        T is Copy\n        return 1\nstruct Leaf<T> : Base<T>\n    T is Owned\n    public init() => ()\n    override func read(self: objref/Self) -> i32 => 2\nlet a = Leaf<string>.init()@obj";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var before, out var failure), failure);
        var previousTable = Assert.Single(before.Objects).VirtualSlots;
        Assert.Null(previousTable[0]);
        var donor = MinimalEmissionTest.Analyze(Source.Replace("T is Copy", "T is Owned", StringComparison.Ordinal));
        var original = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        var changed = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        Assert.True(KotoHelper.Replace(original, original.TypeConstraints[0], changed.TypeConstraints[0]));
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.TryPrepare(out var after, out failure), failure);
        Assert.NotNull(Assert.Single(after.Objects).VirtualSlots[0]);
        Assert.Null(previousTable[0]); // Previously published physical records are not mutated.
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RejectedReferencesReuseTheirFailureFacts()
    {
        var c = MinimalEmissionTest.Analyze(Program(false) + "\n_ = Leaf<NonCopy>.copy");
        Assert.False(c.Binding.Result.IsComplete);
        var rejected = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => rejected &= !c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(rejected);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmApplicabilityAndHolesReuseTheirStorage(bool block)
    {
        var c = MinimalEmissionTest.Analyze(Program(block));
        Assert.True(c.Emission.Validate(out var failure), MinimalEmissionTest.Describe(c, failure));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    private static string Program(bool block)
    {
        var conditional = block
            ? "    Self is Marker when T is Copy\n        public virtual func copy(self: objref/Self, value: ref/T) -> T => value@follow\n"
            : "    public virtual func copy(self: objref/Self, value: ref/T) -> T\n        T is Copy\n        return value@follow\n";
        return "contract Marker\nstruct NonCopy\n    public init() => ()\n    drop => ()\nopen struct Base<T>\n    T is Owned\n    public init() => ()\n    public virtual func first(self: objref/Self) -> i32 => 1\n" + conditional + "    public virtual func last(self: objref/Self) -> i32 => 3\nstruct Leaf<T> : Base<T>\n    T is Owned\n    public init() => ()\n    override func copy(self: objref/Self, value: ref/T) -> T => value@follow\n    override func last(self: objref/Self) -> i32 => 30\nlet a = Leaf<i32>.init()@obj\nlet b = Leaf<NonCopy>.init()@obj\nrequire a.copy(7) == 7 and a.last() == 30 and b.last() == 30 and b.first() == 1 else => $abort(\"conditional\")";
    }
}
