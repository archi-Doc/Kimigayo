// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Verification;
using Xunit;

namespace XunitTest;

// SPEC 15.3.1, 15.6.3: fitting an acquired reference to another Origin preserves its actual parent.
public class FixedOriginRetentionTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FixedCallResultsKeepTheAcquisitionUntilTheirLastUse(bool named, bool live)
    {
        var source = FixedCall(named, live);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(!live, c.Ownership.Result.IsVerified);
        if (live)
        {
            Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        }
        else
        {
            source += "\nvar z: i32 = 41\nlet x: i32 = 7\nrequire use(z@uniq, x@ref) == 42 else => $abort(\"recovery\")";
            ScalarEmissionTest.EmitFixture("FixedOriginRetentionCall" + named, source, string.Empty);
        }
    }

    [Theory]
    [InlineData("named", false)]
    [InlineData("named", true)]
    [InlineData("value", false)]
    [InlineData("value", true)]
    [InlineData("consuming", false)]
    [InlineData("consuming", true)]
    public void FixedCallStoresRetainTheActualArgument(string form, bool conflict)
    {
        var source = "func use(z: uniq/i32, x: ref/i32) -> i32\n    origin z outlives x\n    var holder: ref/i32 during x = x\n" +
            (form == "named" ? "    func store(h: uniq/(ref/i32 during x), b: ref/i32 during x) -> i32\n        h@follow = b\n        return 0\n    store(holder@uniq, z@follow@ref)\n"
                : "    var store = func [holder@uniq] (b: ref/i32 during x) -> i32\n        holder@follow = b\n        return 0\n    store" + (form == "consuming" ? "@move" : string.Empty) + "(z@follow@ref)\n") +
            (conflict ? "    z@follow = 42\n" : string.Empty) + "    return holder@follow\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified == !conflict, MinimalEmissionTest.Describe(c, null));
        if (conflict)
        {
            Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        }
        else
        {
            source += "\nvar z: i32 = 41\nlet x: i32 = 7\nrequire use(z@uniq, x@ref) == 41 else => $abort(\"store\")";
            ScalarEmissionTest.EmitFixture("FixedOriginRetentionStore" + form, source, string.Empty);
        }
    }

    [Theory]
    [InlineData(false, "origin z == x", false)]
    [InlineData(true, "origin z == x", true)]
    [InlineData(false, "origin z outlives x\n    origin x outlives z", true)]
    [InlineData(true, "origin z outlives x\n    origin x outlives z", false)]
    [InlineData(false, "origin z outlives x", false)]
    [InlineData(true, "origin z outlives x", true)]
    public void EquivalentFitsKeepTheSameLoans(bool named, string relation, bool live)
    {
        var direct = FixedCall(named, live).Replace("origin z outlives x", relation, StringComparison.Ordinal);
        foreach (var source in new[] { direct, direct.Replace("let r = pass(z)", "let adapted: uniq/i32 during x = z\n    let r = pass(adapted)", StringComparison.Ordinal) })
        {
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
            Assert.True(c.Ownership.Result.IsVerified == !live, MinimalEmissionTest.Describe(c, null));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpaqueFunctionResultsKeepTheActualLoan(bool live)
    {
        var source = "func pass(b: uniq/i32) -> uniq/i32 => b\n" +
            "func use(z: uniq/i32, x: ref/i32, f: (uniq/i32 during x) -> uniq/i32 during x) -> i32\n    origin z outlives x\n    let r = f(z)\n" +
            (live ? "    z@follow = 42\n    return r@follow\n" : "    r@follow = 42\n    return z@follow\n");
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified == !live, MinimalEmissionTest.Describe(c, null));
        if (!live)
        {
            source += "var z: i32 = 41\nlet x: i32 = 7\nrequire use(z@uniq, x@ref, pass) == 42 else => $abort(\"opaque\")";
            ScalarEmissionTest.EmitFixture("FixedOriginRetentionOpaque", source, string.Empty);
        }
    }

    [Theory]
    [InlineData("holder = x\n    z@follow = 42\n    return holder@follow", true)]
    [InlineData("let saved = holder\n    holder = x\n    z@follow = 42\n    return saved@follow", false)]
    [InlineData("let seen = holder@follow\n    z@follow = 42\n    return seen", true)]
    public void StoredLoansFollowReplacementAndCopies(string tail, bool valid)
    {
        var source = "func use(z: uniq/i32, x: ref/i32) -> i32\n    origin z outlives x\n" +
            "    func store(h: uniq/(ref/i32 during x), b: ref/i32 during x) -> ()\n        h@follow = b\n" +
            "    var holder: ref/i32 during x = x\n    store(holder@uniq, z@follow@ref)\n    " + tail;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified == valid, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStoredClosureValueReachesLaterResults(bool conflict)
    {
        var source = "func use(z: uniq/i32, x: ref/i32) -> i32\n    origin z outlives x\n" +
            "    var state: ref/i32 during x = x\n    var exchange = func [var state] (value: ref/i32 during x) -> ref/i32 during x\n" +
            "        let old = state\n        state = value\n        return old\n" +
            "    _ = exchange(z@follow@ref)\n    let result = exchange(x)\n" +
            (conflict ? "    z@follow = 42\n    return result@follow\n" : "    return result@follow\n");
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified == !conflict, MinimalEmissionTest.Describe(c, null));
        if (!conflict)
        {
            source += "var z: i32 = 41\nlet x: i32 = 7\nrequire use(z@uniq, x@ref) == 41 else => $abort(\"reload\")";
            ScalarEmissionTest.EmitFixture("FixedOriginRetentionReload", source, string.Empty);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteTypePositionsKeepIndependentSources(bool conflict)
    {
        var source = "func use(z: uniq/i32, w: uniq/i32, x: ref/i32, y: ref/i32) -> i32\n    origin z outlives x\n    origin w outlives y\n" +
            "    func first(pair: (ref/i32 during x, ref/i32 during y)) -> ref/i32 during x => pair.0\n" +
            "    let r = first((z@follow@ref, w@follow@ref))\n    " + (conflict ? "z" : "w") + "@follow = 42\n    return r@follow\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified == !conflict, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("if flag\n        store(holder@uniq, z@follow@ref)\n    else\n        z@follow = 42\n    return holder@follow", true)]
    [InlineData("if flag\n        store(holder@uniq, z@follow@ref)\n    z@follow = 42\n    return holder@follow", false)]
    [InlineData("var i = 0\n    while i < 2\n        store(holder@uniq, z@follow@ref)\n        i += 1\n    z@follow = 42\n    return holder@follow", false)]
    [InlineData("store(holder@uniq, z@follow@ref)\n    let result = load(holder@uniq)\n    z@follow = 42\n    return result@follow", false)]
    [InlineData("let earlier = holder\n    store(holder@uniq, z@follow@ref)\n    z@follow = 42\n    return earlier@follow", true)]
    public void RetentionFollowsExecutedStoresAndSnapshots(string tail, bool valid)
    {
        var source = "func use(z: uniq/i32, x: ref/i32, flag: bool) -> i32\n    origin z outlives x\n" +
            "    func store(h: uniq/(ref/i32 during x), b: ref/i32 during x) -> ()\n        h@follow = b\n" +
            "    func load(h: uniq/(ref/i32 during x)) -> ref/i32 during x => h@follow\n" +
            "    var holder: ref/i32 during x = x\n    " + tail;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified == valid, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASharedResultDoesNotDowngradeAnExclusiveAcquisition(bool named)
    {
        var source = FixedCall(named, true).Replace("-> uniq/i32 during x => b", "-> ref/i32 during x => b@follow@ref", StringComparison.Ordinal)
            .Replace("z@follow = 42", "let snapshot = z@follow", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    [Theory]
    [InlineData("uniq", "let other = r\n    let seen = r@follow + other@follow\n    z@follow = 42\n    return seen", true)]
    [InlineData("ref", "let seen = z@follow\n    return r@follow + seen", true)]
    [InlineData("uniq", "defer => _ = r@follow\n    z@follow = 42\n    return 0", false)]
    [InlineData("uniq", "func read(b: ref/i32 during x) -> ref/i32 during x => b\n    let child = read(r)\n    return r@follow + child@follow", true)]
    public void RetainedAcquisitionsPermitOnlyCompatibleUses(string mode, string tail, bool valid)
    {
        var source = "func use(z: uniq/i32, x: ref/i32) -> i32\n    origin z outlives x\n" +
            "    func pass(b: " + mode + "/i32 during x) -> ref/i32 during x => b@follow@ref\n" +
            "    let r = pass(" + (mode == "ref" ? "z@follow@ref" : "z") + ")\n    " + tail;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified == valid, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ASecondCallStillSuspendsTheReturnedParent()
    {
        var source = FixedCall(true, false).Replace("    r@follow = 42\n    return z@follow", "    func read(b: uniq/i32 during x) -> ref/i32 during x => b@follow@ref\n    let child = read(r)\n    let seen = r@follow\n    return child@follow + seen", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict && x.Source.ToString() == "r");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GenericAndDefaultCallsUseTheirInstantiatedContracts(bool generic, bool live)
    {
        var source = FixedCall(true, live);
        source = generic
            ? source.Replace("pass(b: uniq/i32 during x) -> uniq/i32 during x", "pass<T>(b: uniq/T during x) -> uniq/T during x", StringComparison.Ordinal)
            : source.Replace("b: uniq/i32 during x)", "b: uniq/i32 during x, unused: i32 = 1)", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified == !live, MinimalEmissionTest.Describe(c, null));
        if (!live)
        {
            source += "var z: i32 = 41\nlet x: i32 = 7\nrequire use(z@uniq, x@ref) == 42 else => $abort(\"instantiated\")";
            ScalarEmissionTest.EmitFixture("FixedOriginRetentionInstantiated" + generic, source, string.Empty);
        }
    }

    [Theory]
    [InlineData("Tuple", 0, true)]
    [InlineData("Tuple", 1, false)]
    [InlineData("Array", 0, true)]
    [InlineData("Array", 1, false)]
    [InlineData("Struct", 0, true)]
    [InlineData("Struct", 1, false)]
    public void PartialReplacementPreservesTheOtherParts(string form, int replaced, bool valid)
    {
        var prefix = form == "Struct" ? "struct Pair {source}\n    public var first: ref/i32 during source\n    public var second: ref/i32 during source\n    public init(value: ref/i32 during source)\n        self.first = value\n        self.second = value\n" : string.Empty;
        var declaration = form == "Tuple" ? "var holder = (x, x)" : form == "Array" ? "var holder: [2 of ref/i32 during x] = [x, x]" : "var holder = Pair.init(x)";
        var first = form == "Tuple" ? "holder.0" : form == "Array" ? "holder[0]" : "holder.first";
        var target = replaced == 0 ? first : form == "Tuple" ? "holder.1" : form == "Array" ? "holder[1]" : "holder.second";
        var source = prefix + "func use(z: uniq/i32, x: ref/i32) -> i32\n    origin z outlives x\n" +
            "    func store(h: uniq/(ref/i32 during x), b: ref/i32 during x) -> ()\n        h@follow = b\n" +
            "    " + declaration + "\n    store(" + first + "@uniq, z@follow@ref)\n    " + target +
            " = x\n    z@follow = 42\n    return " + first + "@follow\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified == valid, MinimalEmissionTest.Describe(c, null));
        if (valid)
        {
            source += "var z: i32 = 41\nlet x: i32 = 7\nrequire use(z@uniq, x@ref) == 7 else => $abort(\"replacement\")";
            ScalarEmissionTest.EmitFixture("FixedOriginRetentionPart" + form, source, string.Empty);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DestructionIsAUseOfTheRetainedValue(bool conflict)
    {
        var source = "struct View {source}\n    public let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\n    drop => _ = self.value@follow\n" +
            "func use(z: uniq/i32, x: ref/i32) -> i32\n    origin z outlives x\n" +
            "    func pass(b: ref/i32 during x) -> ref/i32 during x => b\n    do\n        let view = View.init(pass(z@follow@ref))\n" +
            (conflict ? "        z@follow = 42\n" : string.Empty) + "    z@follow = 42\n    return z@follow\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified == !conflict, MinimalEmissionTest.Describe(c, null));
        if (!conflict)
        {
            source += "var z: i32 = 41\nlet x: i32 = 7\nrequire use(z@uniq, x@ref) == 42 else => $abort(\"destruction\")";
            ScalarEmissionTest.EmitFixture("FixedOriginRetentionDestruction", source, string.Empty);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneOriginDoesNotMergeMultipleActualLoans(bool first)
    {
        var source = "func use(z: uniq/i32, w: uniq/i32, x: ref/i32) -> i32\n    origin z outlives x\n    origin w outlives x\n" +
            "    func choose(a: ref/i32 during x, b: ref/i32 during x) -> ref/i32 during x => a\n" +
            "    let r = choose(z@follow@ref, w@follow@ref)\n    " + (first ? "z" : "w") + "@follow = 42\n    return r@follow\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    [Fact]
    public void EditingAFixedCallRevokesItsRetainedArgument()
    {
        var source = "func use(z: uniq/i32, x: ref/i32) -> i32\n    origin z outlives x\n    let pass = func (b: ref/i32 during x) -> ref/i32 during x => b\n" +
            "    let r = pass(z@follow@ref)\n    z@follow = 42\n    return r@follow\npublic func main() => ()";
        var c = MinimalEmissionTest.Analyze(source);
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(static x => x.Method is IdentifierNameKoto { IdentifierName: "pass" });
        foreach (var keep in new[] { false, true, false })
        {
            var donor = MinimalEmissionTest.Analyze(keep ? source : source.Replace("pass(z@follow@ref)", "pass(x)", StringComparison.Ordinal));
            var input = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(static x => x.Method is IdentifierNameKoto { IdentifierName: "pass" }).ArgumentNodes[0];
            Assert.True(KotoHelper.Replace(call, call.ArgumentNodes[0], input));
            Assert.True(c.Bind().IsComplete);
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.Equal(!keep, c.Ownership.Analyze().IsVerified);
            Assert.Equal(!keep, c.Emission.WriteIr(TextWriter.Null, out _));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmFixedCallsAllocateNothing(bool named)
    {
        var source = FixedCall(named, false) + "\npublic func main() => ()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    [Theory]
    [InlineData(false, "ref")]
    [InlineData(true, "ref")]
    [InlineData(false, "uniq")]
    [InlineData(true, "uniq")]
    public void AnAnnotatedChildStillSuspendsItsParent(bool annotation, string mode)
    {
        var source = LocalChild(annotation, true, mode);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData(false, "ref")]
    [InlineData(true, "ref")]
    [InlineData(false, "uniq")]
    [InlineData(true, "uniq")]
    public void AnEndedChildRestoresItsParent(bool annotation, string mode)
    {
        var source = LocalChild(annotation, false, mode) + "\nvar value: i32 = 41\nlet independent: i32 = 7\n" +
            "require use(value@uniq, independent@ref) == 41 else => $abort(\"parent\")";
        ScalarEmissionTest.EmitFixture("FixedOriginRetentionEnded" + mode + annotation, source, string.Empty);
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("uniq")]
    public void AnAnnotationDoesNotBorrowTheIndependentInput(string mode)
    {
        var source = "func use(z: uniq/i32, x: uniq/i32) -> i32\n    origin z outlives x\n    let y: " + mode +
            "/i32 during x = " + (mode == "ref" ? "z@follow@ref" : "z") +
            "\n    x@follow = 9\n    return y@follow\nvar z: i32 = 41\nvar x: i32 = 7\n" +
            "require use(z@uniq, x@uniq) == 41 else => $abort(\"source\")\nrequire x == 9 else => $abort(\"independent\")";
        ScalarEmissionTest.EmitFixture("FixedOriginRetentionIndependent" + mode, source, string.Empty);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("named")]
    [InlineData("value")]
    public void TheConflictIdentifiesTheActualParentAndRetainedChild(string form)
    {
        var source = form == "local" ? LocalChild(true, true, "uniq") : FixedCall(form == "named", true);
        var path = Path.GetFullPath("fixed-origin-retention.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete);
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("z", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal("This operation conflicts with an active loan", error.Message);
        var retained = Assert.Single(error.Related!);
        Assert.Equal("loan", retained.Role);
        Assert.Contains(form == "local" ? "y" : "r", source.Substring(retained.Span!.Value.Start, retained.Span.Value.Length), StringComparison.Ordinal);
        var json = System.Text.Json.JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("\"code\":\"ComparisonLoanConflict_Kd\"", json, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"loan\"", json, StringComparison.Ordinal);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("value retaining the conflicting loan", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            if (related)
            {
                Assert.Equal(retained.Range, Assert.Single(sent.RelatedInformation!).Location.Range);
            }
            else
            {
                Assert.Contains("value retaining the conflicting loan", sent.Message, StringComparison.Ordinal);
            }
        }
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmLocalTransfersAllocateNothing(bool live)
    {
        var c = MinimalEmissionTest.Analyze(LocalChild(true, live, "uniq") + "\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified == !live, iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    [Fact]
    public void EditingTheAcquisitionRevokesItsPreviousLoan()
    {
        var source = LocalChild(true, true, "ref") + "\npublic func main() => ()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Ownership.Result.IsVerified);
        var local = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "y");
        foreach (var parent in new[] { false, true })
        {
            var donor = MinimalEmissionTest.Analyze(parent ? source : source.Replace("= z@follow@ref", "= x", StringComparison.Ordinal));
            var initializer = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "y").InitializerKoto!;
            Assert.True(KotoHelper.Replace(local, local.InitializerKoto!, initializer));
            Assert.True(c.Bind().IsComplete);
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.Equal(!parent, c.Ownership.Analyze().IsVerified);
            Assert.Equal(!parent, c.Emission.WriteIr(TextWriter.Null, out _));
        }
    }

    private static string FixedCall(bool named, bool live)
        => "func use(z: uniq/i32, x: ref/i32) -> i32\n    origin z outlives x\n" +
            (named ? "    func pass(b: uniq/i32 during x) -> uniq/i32 during x => b\n"
                : "    let pass = func (b: uniq/i32 during x) -> uniq/i32 during x => b\n") +
            "    let r = pass(z)\n" + (live ? "    z@follow = 42\n    return r@follow\n"
                : "    r@follow = 42\n    return z@follow\n");

    private static string LocalChild(bool annotation, bool live, string mode)
        => "func use(z: uniq/i32, x: ref/i32) -> i32\n    origin z outlives x\n    let y" +
            (annotation ? ": " + mode + "/i32 during x" : string.Empty) + " = " + (mode == "ref" ? "z@follow@ref" : "z") + "\n" +
            (live ? "    z@follow = 41\n    return y@follow" : "    require y@follow == 41 else => $abort(\"child\")\n    z@follow = 41\n    return z@follow");
}
