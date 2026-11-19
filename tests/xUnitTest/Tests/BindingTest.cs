// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class BindingTest
{
    // PLAN G375: a kept Proven is a state on the symbol, so a warm Bind's reads of a signature allocate nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBindOfSignatureReadsAllocatesNothing()
    {
        var compilation = CompilationTestHelper.ParseSuccess(
            "contract C\n    func m(self: ref/Self) -> i32\nstruct S\n    Self is C\n    public init() => ()\n    public func m(self: ref/Self) -> i32 => 1\n" +
            "struct Box<T>\n    T is C\n    public init() => ()\nfunc take(b: ref/Box<S>) -> i32 => 1\nfunc a(b: ref/Box<S>) -> i32 => take(b)\nfunc c(b: ref/Box<S>) -> i32 => take(b)");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        Assert.Equal(0, AllocationMeasurement.Measure(() => compilation.Bind()));
    }

    // PLAN G724, B4: a member access's side is published beside its selection in a table cleared each pass, so a warm Bind of Type-side,
    // value-side, constructor and Case accesses allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBindOfMemberSidesAllocatesNothing()
    {
        var compilation = CompilationTestHelper.ParseSuccess(
            "enum Message\n    Quit\n    Write(i32)\nstruct S\n    public init() => ()\n    public func m(self: ref/Self) -> i32 => 1\n" +
            "    public func make() -> S => S.init()\nfunc f(s: ref/S) -> i32 => s.m()\nfunc g() -> Message => Message.Write(1)\nfunc h() -> S => S.make()");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        Assert.Equal(0, AllocationMeasurement.Measure(() => compilation.Bind()));
    }

    [Fact]
    public void PipelineBindsExistingNodesAndRetainsCallPlans()
    {
        var compilation = CompilationTestHelper.ParseSuccess("func add(x: i32, y: i32) -> i32 => x + y\nlet result = add(1, 2)");
        var nodes = KotoTree.Walk(compilation.Kotonoha.RootKoto).ToArray();
        var call = Assert.Single(nodes.OfType<InvocationKoto>());
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        Assert.Equal(nodes, KotoTree.Walk(compilation.Kotonoha.RootKoto));
        Assert.Equal("i32", call.TypeOf()!.Name);
        Assert.Equal("add", call.CallOf()!.Target.Name);
        Assert.Equal(new[] { 0, 1 }, call.CallOf()!.ArgumentToParameter.ToArray());
        var plan = call.CallOf();
        var symbol = call.SymbolOf();
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        Assert.Same(symbol, call.SymbolOf());
        Assert.Same(plan, call.CallOf());
        Assert.Empty(TestDiagnostics.Of(compilation));
    }

    [Fact]
    public void UnresolvedSyntaxSurvivesUntilGeneratedDeclarationsAreIntegrated()
    {
        var compilation = CompilationTestHelper.ParseSuccess("group Demo\n    func read() -> i32 => generated()");
        var call = Assert.Single(KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var group = Assert.Single(compilation.Kotonoha.RootKoto.NestedContainers);
        var provisional = compilation.Binding.Bind(BindingMode.Provisional);
        Assert.False(provisional.IsComplete);
        Assert.Equal(BindingState.Unresolved, call.StateOf());
        Assert.Empty(compilation.Binding.Issues);
        compilation.Kotonoha.CreateCodeContext().Parse(group, "func generated() -> i32 => 42");
        Assert.True(compilation.Binding.Bind(BindingMode.Final).IsComplete, Describe(compilation));
        Assert.Same(call, Assert.Single(KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>()));
        Assert.Equal("generated", call.CallOf()!.Target.Name);
    }

    [Fact]
    public void AddedOverloadInvalidatesPreviouslySuccessfulSelection()
    {
        var compilation = CompilationTestHelper.ParseSuccess("group Demo\n    func f(x: i32) -> i32 => x\n    func read() -> i32 => f(1)");
        var call = Assert.Single(KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>());
        compilation.Binding.Bind(BindingMode.Provisional);
        Assert.Equal(BindingState.Resolved, call.StateOf());
        compilation.Kotonoha.CreateCodeContext().Parse(Assert.Single(compilation.Kotonoha.RootKoto.NestedContainers), "func f(x: i64) -> i32 => 1");
        Assert.False(compilation.Binding.Bind(BindingMode.Final).IsComplete);
        Assert.Null(call.CallOf());
        Assert.Contains(compilation.Binding.Issues, x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    [Theory]
    [InlineData("let result = absent", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("let x: i32 = true", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let x = 1\nlet x = 2", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("func f(x: i32) => x\nfunc f(y: i32) -> i64 => 1", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("func f<T>(x: T) => x\nfunc f<U>(x: U) => x", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("func f() -> i32 => 1\nfunc g()\n    let f = 1\n    f()", DiagnosticCode.NotCallable_Kd)]
    [InlineData("func f() => return 1", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let x: i8 = 128", DiagnosticCode.InvalidNumericLiteral_Kd)]
    [InlineData("let x: f32 = 1e100", DiagnosticCode.InvalidNumericLiteral_Kd)]
    [InlineData("let x = 1\nfunc f() => x", DiagnosticCode.InvalidCaptureBinding_Kd)]
    [InlineData("let x: u32 = 1\nlet y = -x", DiagnosticCode.OperatorNotDefined_Kd)]
    [InlineData("var x: f64 = 1.0\nx++", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("var x: f64 = 1.0\nx %= 2.0", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("var x: f64 = 1.0\nx <<= 1", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("let x: i32 = 1\nlet y = x << 1.0", DiagnosticCode.InvalidShiftCount_Kd)]
    [InlineData("let x = true < false", DiagnosticCode.OperatorNotDefined_Kd)]
    [InlineData("let x = true & false", DiagnosticCode.NonNumericOperand_Kd)]
    [InlineData("let x = 'a' + 'b'", DiagnosticCode.NonNumericOperand_Kd)]
    [InlineData("let x = \"a\" - \"b\"", DiagnosticCode.NonNumericOperand_Kd)]
    [InlineData("let x = \"a\" + \"b\"", DiagnosticCode.NonNumericOperand_Kd)]
    [InlineData("var s = \"a\"\ns += \"b\"", DiagnosticCode.NonNumericOperand_Kd)]
    [InlineData("let x: i32 = -1.5", DiagnosticCode.TypeMismatch_Kd)]
    public void FinalBindingRejectsInvalidProgramsWithoutDuplicatingIssues(string source, DiagnosticCode code)
    {
        var compilation = CompilationTestHelper.ParseSuccess(source);
        Assert.False(compilation.Bind().IsComplete);
        Assert.Contains(compilation.Binding.Issues, issue => issue.Code == code);
        var count = compilation.Binding.Issues.Count;
        Assert.False(compilation.Bind().IsComplete);
        Assert.Equal(count, compilation.Binding.Issues.Count);
    }

    // SPEC 11.6, 23.1.2 (DECISIONS BU-105): a cycle of inferred Properties is recorded once, at the declaration whose header started
    // first; each other declaration on it, and a dependent beside it, rests on that record. The query state of a header lives one pass,
    // so binding again gives the same records.
    [Theory]
    [InlineData("group Values\n    var a = b\n    var b = a", "CyclicBinding_Kd var a = b|PrerequisiteUnavailable_Kd var b = a")]
    [InlineData("group Values\n    var a = Values.b\n    var b = a", "CyclicBinding_Kd var a = Values.b|PrerequisiteUnavailable_Kd var b = a")]
    [InlineData("group Values\n    var a = b\n    var b = c\n    var c = a", "CyclicBinding_Kd var a = b|PrerequisiteUnavailable_Kd var b = c|PrerequisiteUnavailable_Kd var c = a")]
    [InlineData("group Values\n    var a = b\n    var b = a\n    var c = a + 1", "CyclicBinding_Kd var a = b|PrerequisiteUnavailable_Kd var b = a|PrerequisiteUnavailable_Kd var c = a + 1")]
    [InlineData("group Values\n    var a = b\n    var b = a\n    var c: i32 = 1", "CyclicBinding_Kd var a = b|PrerequisiteUnavailable_Kd var b = a")]
    [InlineData("group Values\n    var a: i32 = b\n    var b = a", "")]
    [InlineData("group Values\n    var a: i32 = b\n    var b = c\n    var c = a", "")]
    public void APropertyCycleIsRecordedOnceAtItsFirstHeaderInEveryPass(string source, string records)
    {
        var compilation = CompilationTestHelper.ParseSuccess(source);
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.Equal(records.Length == 0, compilation.Bind().IsComplete);
            Assert.Equal(records, string.Join('|', CompilationTestHelper.BindingErrors(compilation).OrderBy(static x => x.Span.Start).Select(static x => $"{x.Code} {x.Text}")));
        }
    }

    // DECISIONS BU-105: an edit between passes binds every header again, so a declaration added beside a chain sees the chain's Types.
    [Fact]
    public void AnEditBindsTheHeadersAgain()
    {
        var compilation = CompilationTestHelper.ParseSuccess("group Values\n    var a = 1\n    var b = a");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        compilation.Kotonoha.CreateCodeContext().Parse(Assert.Single(compilation.Kotonoha.RootKoto.NestedContainers), "var c = b\nvar d = d");
        Assert.False(compilation.Bind().IsComplete);
        Assert.Equal([DiagnosticCode.CyclicBinding_Kd], compilation.Binding.Issues.Select(static x => x.Code));
        var c = KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<PropertyKoto>().Single(static x => x.NameKoto.IdentifierName == "c");
        Assert.Equal("i32", c.TypeOf()!.Name);
    }

    // DECISIONS BU-105, SPEC 11.6: no query state crosses a pass, so annotating the declaration that carries a cycle's record binds the
    // corrected program in the next pass.
    [Fact]
    public void AnEditThatAnnotatesACycleBindsTheCorrectedProgram()
    {
        const string Cycle = "group Values\n    var a = b\n    var b = a";
        var compilation = CompilationTestHelper.ParseSuccess(Cycle);
        Assert.False(compilation.Bind().IsComplete);
        Assert.Contains(compilation.Binding.Issues, static x => x.Code == DiagnosticCode.CyclicBinding_Kd);
        var donor = Named(CompilationTestHelper.ParseSuccess(Cycle.Replace("var a = b", "var a: i32 = b", StringComparison.Ordinal)), "a");
        ParseTestHelper.Detach(donor, new CodeBlockKoto(donor.CodeContext));
        Assert.True(KotoHelper.Replace(Assert.Single(compilation.Kotonoha.RootKoto.NestedContainers), Named(compilation, "a"), donor));
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        Assert.Equal("i32", Named(compilation, "b").TypeOf()!.Name);

        static PropertyKoto Named(Compilation c, string name)
            => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<PropertyKoto>().Single(x => x.NameKoto.IdentifierName == name);
    }

    // DECISIONS BU-105: the declaration query state is inline on the symbol and stamped by the pass, so a warm Bind of Property headers
    // and a function header allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBindOfHeadersAllocatesNothing()
    {
        var compilation = CompilationTestHelper.ParseSuccess("group Values\n    var a = 1\n    var b = a\n    var c = b\n    func f(x: i32) -> i32 => x + c");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        Assert.Equal(0, AllocationMeasurement.Measure(() => compilation.Bind()));
    }

    [Theory]
    [InlineData("let x: u64 = 1\nlet count: u8 = 3\nlet y = x << count", "u64")]
    [InlineData("let x: i64 = 1\nlet n: u8 = 3\nlet y = if true => x << n else => x >> n", "i64")]
    [InlineData("let x: i64 = 1\nlet n: u8 = 3\nlet y = if true => x << n else => 0", "i64")]
    [InlineData("let x: i32 = 1\nlet y = x << -1", "i32")]
    [InlineData("let n: u8 = 3\nlet y = if true => 1 << n else => 0", "i32")]
    [InlineData("var x: i16 = 1\nlet count: u32 = 3\nx >>= count\nlet y = x", "i16")]
    [InlineData("let y = \"\\(\"a\")\\(\"b\")\"", "string")]
    [InlineData("var s = \"a\"\ns = \"\\(s)b\"\nlet y = s@move", "string")]
    [InlineData("let y = 'a' < 'b'", "bool")]
    [InlineData("let y = \"a\" >= \"b\"", "bool")]
    [InlineData("let y = () == ()", "bool")]
    [InlineData("var x: i8 = 1\nlet y = x++", "i8")]
    [InlineData("let x: f32 = 1.0\nlet y = -x", "f32")]
    public void BuiltInOperatorsFollowTheirOperandCategories(string source, string type)
    {
        var compilation = CompilationTestHelper.ParseSuccess(source);
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var result = KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<FieldKoto>().Single(x => x.NameKoto.IdentifierName == "y");
        Assert.Equal(type, result.TypeOf()!.Name);
    }

    [Theory]
    [InlineData("i8", "3")]
    [InlineData("i16", "3")]
    [InlineData("i32", "-1")]
    [InlineData("i64", "3")]
    [InlineData("u32", "3")]
    public void UntypedShiftCountsAreTypedIndependently(string type, string count)
    {
        // SPEC 12.3: a shift count is typed independently of the shifted operand, so an untyped count defaults to i32.
        var compilation = CompilationTestHelper.ParseSuccess($"let x: {type} = 1\nlet y = x << {count}");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var shift = Assert.Single(KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<BinaryKoto>(), static x => x.Akind == KotoKind.LessThanLessThan);
        Assert.Equal("i32", shift.Right.TypeOf()!.Name);
        Assert.Same(shift.Left.TypeOf(), shift.TypeOf());
    }

    [Fact]
    public void LocalsUseLexicalVisibilityAndInitializersSeeOuterBindings()
    {
        var compilation = CompilationTestHelper.ParseSuccess("func f(x: i32) -> i32\n    if true\n        let x = x + 1\n    return x");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var function = Assert.Single(KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => !x.IsGenerated);
        var local = Assert.Single(KotoTree.Walk(function).OfType<FieldKoto>());
        var operand = Assert.IsType<IdentifierNameKoto>(ParseTestHelper.Binary(KotoKind.Plus, local.InitializerKoto).Left);
        Assert.Equal(BindingSymbolKind.Parameter, operand.SymbolOf()!.Kind);
        Assert.NotSame(local.SymbolOf(), operand.SymbolOf());
    }

    [Fact]
    public void SourceLocalFunctionsAreNotImportedFromAnotherDocument()
    {
        var compilation = CompilationTestHelper.ParseSuccess("func privateToSource() -> i32 => 1");
        compilation.Kotonoha.CreateCodeContext().Parse(compilation.Kotonoha.RootKoto, new SourceDocument("second.kimi", "let result = privateToSource()"));
        Assert.False(compilation.Bind().IsComplete);
        Assert.Contains(compilation.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
    }

    [Fact]
    public void GenericCallsRetainInferredTypesAndCanonicalTupleTypes()
    {
        var compilation = CompilationTestHelper.ParseSuccess("func identity<T>(value: T) -> T => value@move\nlet a = identity(1)\nlet b = identity<i64>(2)\nlet pair: (i32, i32) = (1, 2)");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var calls = KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>().ToArray();
        Assert.Equal("i32", calls[0].CallOf()!.TypeArguments[0]!.Name);
        Assert.Equal("i64", calls[1].CallOf()!.TypeArguments[0]!.Name);
        var pair = KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<FieldKoto>().Single(x => x.NameKoto.IdentifierName == "pair");
        Assert.Same(pair.TypeKoto!.TypeOf(), pair.InitializerKoto!.TypeOf());
    }

    [Fact]
    public void UntypedArgumentsAreFittedOnlyAfterUniqueCandidateSelection()
    {
        var compilation = CompilationTestHelper.ParseSuccess("func f(x: i8) -> i8 => x\nfunc f(x: i64) -> i64 => x\nlet result = f(128)");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var call = Assert.Single(KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Equal("i64", call.ArgumentNodes[0].TypeOf()!.Name);
    }

    [Fact]
    public void AccessAndWrongRoleDoNotStopOuterLookup()
    {
        var compilation = CompilationTestHelper.ParseSuccess("group Outer\n    struct T\n    group Inner\n        group T\n        func f(x: T) -> T => x@move");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var function = Assert.Single(KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<FunctionKoto>());
        Assert.IsType<StructKoto>(function.Parameters[0].Type.SymbolOf()!.Declaration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualArgumentTypesPreferNongenericThenFewerDefaults(bool reverse)
    {
        var declarations = new[]
        {
            "func f<T>(x: T) -> T => x@move",
            "func f(x: i32, extra: i32 = 0) -> i32 => x",
            "func f(x: i32) -> i32 => x",
        };
        if (reverse)
        {
            Array.Reverse(declarations);
        }

        var compilation = CompilationTestHelper.ParseSuccess(string.Join("\n", declarations) + "\nlet result = f(1@i32)");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var call = Assert.Single(KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var selected = Assert.IsType<FunctionKoto>(call.SymbolOf()!.Declaration);
        Assert.Empty(selected.GenericArguments);
        Assert.Single(selected.Parameters);
    }

    [Fact]
    public void ExpectedResultFillsUnresolvedGenericTypeBeforeLiteralDefaulting()
    {
        var compilation = CompilationTestHelper.ParseSuccess("func identity<T>(value: T) -> T => value@move\nlet result: i64 = identity(1)");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var call = Assert.Single(KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Equal("i64", call.CallOf()!.TypeArguments[0]!.Name);
    }

    [Fact]
    public void NamedArgumentsPreserveSourceOrderInCommittedMapping()
    {
        var compilation = CompilationTestHelper.ParseSuccess("func f(a: i32, b: i32) -> i32 => a + b\nlet result = f(b: 2, a: 1)");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var call = Assert.Single(KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Equal(new[] { 1, 0 }, call.CallOf()!.ArgumentToParameter.ToArray());
        Assert.Equal("2", Assert.IsType<NumberLiteralKoto>(call.ArgumentNodes[0]).Literal);
    }

    [Fact]
    public void DirectBorrowOriginsResolveBeforeBoundCheck()
    {
        var compilation = CompilationTestHelper.ParseSuccess("func borrow(value: ref/i32) -> ref/i32 => value");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        Assert.Empty(compilation.Binding.Issues);
        var count = compilation.Binding.Issues.Count;
        Assert.True(compilation.Binding.CheckBound().IsComplete);
        Assert.Equal(count, compilation.Binding.Issues.Count);
    }

    [Fact]
    public void DirectTraversalCoversTheSameChildrenAsPublicSyntaxEnumeration()
    {
        var compilation = CompilationTestHelper.ParseSuccess("group Demo\n    struct Pair<T>\n        var value: T\n        func get(self: ref/Self) -> T => value\nfunc f(x: i32)\n    if x == 0\n        return\n    let values = [1, 2]\n    let map = [1: 2]\n    let text = \"value: \\(x)\"");
        var visitor = new CollectVisitor();
        visitor.Visit(compilation.Kotonoha.RootKoto);
        Assert.Equal(KotoTree.Walk(compilation.Kotonoha.RootKoto), visitor.Nodes);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RebindingReusesScratchAndDoesNotAllocatePerNode()
    {
        var source = "func identity<T>(x: T) -> T => x@move\n" + string.Join("\n", Enumerable.Range(0, 256).Select(i => $"let v{i} = identity({i})"));
        var compilation = CompilationTestHelper.ParseSuccess(source);
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        Assert.Equal(0, AllocationMeasurement.Measure(() => compilation.Binding.Bind(BindingMode.Final)));
    }

    // Closure receiver classification (12, 14, 35: a reusable visitor, the capture list without a boxed enumerator) and
    // specialization Origin inheritance (22: scratch binders) once allocated on every warm rebind.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(22)]
    [InlineData(35)]
    public void RebindingAMilestoneProgramDoesNotAllocate(int number)
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare(WindowsProfile.Target));
        compilation.Kotonoha.AddSource(new SourceDocument($"Milestone{number}.kimi", DiagnosticCorpus.Milestone(number)));
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        Assert.Equal(0, AllocationMeasurement.Measure(() => compilation.Binding.Bind(BindingMode.Final)));
    }

    // Scratch returns in nested order: a call through a function value, a reference at a Function Type, a Function Item argument and the
    // record of a rejected reference each rent the callee's Origins and its input Origins. Returned in renting order, 17 inputs were
    // cleared over the other list's first 16-slot buffer, and these programs faulted (CheckFaulted_Kd).
    [Theory]
    [InlineData("func use() -> i32\n    let n = 7\n    let h = pick\n    return h(n@ref{0})\n", null)]
    [InlineData("func use() -> i32\n    let n = 7\n    let h: (ref/i32{1}) -> i32 = pick\n    return h(n@ref{0})\n", null)]
    [InlineData("func apply(h: (ref/i32{1}) -> i32) -> i32 => h(7{0})\nfunc use() -> i32 => apply(pick)\n", null)]
    [InlineData("func use() -> i32\n    let h: (ref/i32{1}) -> ref/i32 during static = view\n    return 0\n", nameof(DiagnosticCode.UnprovenOriginContract_Kd))]
    public void ScratchReturnsInNestedOrder(string body, string? code)
    {
        var inputs = string.Concat(Enumerable.Range(1, 16).Select(static i => $", a{i}: i32"));
        var calls = string.Format(body, string.Concat(Enumerable.Repeat(", 0", 16)), string.Concat(Enumerable.Repeat(", i32", 16)));
        var source = $"func pick(a0: ref/i32 during source{inputs}) -> i32 => a0@follow\n" +
            $"func view(a0: ref/i32 during source{inputs}) -> ref/i32 during source => a0\n" + calls + "public func main() => ()\n";
        string[] expected = code is null ? [] : [code];
        Assert.Equal(expected, DiagnosticCorpus.Check(source).Diagnostics.Select(static x => x.Code));
    }

    // The nested-order contract holds in every configuration: a return out of order is a fault, never a silent clear over another
    // buffer's length. No program reaches the pool directly, so the test drives it.
    [Fact]
    public void ScratchReturnedOutOfOrderIsAContractBreak()
    {
        var type = typeof(Binding).GetNestedType("ScratchBuffers`1", BindingFlags.NonPublic)!.MakeGenericType(typeof(int));
        var pool = Activator.CreateInstance(type, nonPublic: true)!;
        var rent = type.GetMethod("Rent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var back = type.GetMethod("Return", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var outer = rent.Invoke(pool, [1]);
        _ = rent.Invoke(pool, [1]);
        var fault = Assert.Throws<TargetInvocationException>(() => back.Invoke(pool, [outer, false]));
        Assert.IsType<InvalidOperationException>(fault.InnerException);
    }

    [Fact]
    public void ParenthesizedArrayLengthsBindEverySyntaxNode()
    {
        var compilation = CompilationTestHelper.ParseSuccess("func f(value: [(2) of i32]) -> [2 of i32] => value");
        Assert.True(compilation.Bind().IsComplete, Describe(compilation));
        var types = KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<FixedArrayTypeKoto>().ToArray();
        Assert.Equal(2, types.Length);
        Assert.Same(types[0].TypeOf(), types[1].TypeOf());
        Assert.Equal(2, types[0].TypeOf()!.Length);
    }

    [Fact]
    public void IndexedReplacementPreservesOwnershipAndSourceWithoutCopyingStorage()
    {
        var compilation = CompilationTestHelper.ParseSuccess("func f(x: i32) => x\nlet value = f(1)");
        var call = Assert.Single(KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var arguments = call.ArgumentNodes;
        var old = arguments[0];
        var replacement = new TestReplacement(old.CodeContext);
        call.ReplaceArgument(0, replacement);
        Assert.Same(arguments, call.ArgumentNodes);
        Assert.Same(replacement, call.ArgumentNodes[0]);
        Assert.Null(old.Parent);
        Assert.Same(call, replacement.Parent);
        Assert.Same(old.CodeContext, replacement.CodeContext);
        Assert.Equal(old.Span, replacement.Span);
        Assert.Throws<InvalidOperationException>(() => call.ReplaceArgument(0, call.Method));
    }

    // PLAN G949 S1-r1a (s1r1-design 2.1, v8): a reader outside a pass reads each published list's composed view, the program part's
    // entries, then the library layer's (LibraryLayer.Part, empty until S1-r1b), never the list a pass writes. Every node, issue, derived
    // issue and obligation moved after a Bind from Binding's own part to the layer's is still listed, reported with its related
    // locations (a Callable selection's candidate clauses, found among the nodes: review-r1a B1), counted and read by the Origin rows as
    // in a twin compilation; a reader of the written list loses it.
    [Fact]
    public void AnEntryOfTheLayersPartReachesEveryReaderOutsideAPass()
    {
        // Missing is an issue; the header resting on the parser's error in the length slot (SPEC 4.4) a derived one; no Callable
        // signature of both's is better than the other for action(1) (SPEC 8.10, 10.4: AmbiguousBinding_Kd, each candidate at its clause).
        const string source = "func f(x: Missing) -> i32 => 1\nstruct Cursor<length N>\n    public init() => ()\n" +
            "func both<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i32>\n    return action(1)\n";
        var twin = CompilationTestHelper.Parse(source);
        twin.Bind();
        twin.Binding.ReportDiagnostics();
        var c = CompilationTestHelper.Parse(source);
        c.Bind();
        var (issues, derived, obligations) = (c.Binding.Issues.ToArray(), c.Binding.DerivedIssues.ToArray(), c.Binding.Obligations.ToArray());
        Assert.NotEmpty(issues);
        Assert.NotEmpty(derived);
        Assert.Contains(obligations, static x => x.Body.IsBody);
        Assert.Contains(Records(twin), static x => x.StartsWith("AmbiguousBinding_Kd", StringComparison.Ordinal) && x.Contains("[candidate", StringComparison.Ordinal));
        var layer = c.Binding.Library.Layer.Part;
        Move(c, "nodes", layer.Nodes);
        Move(c, "issues", layer.Issues);
        Move(c, "derivedIssues", layer.DerivedIssues);
        Move(c, "obligations", layer.Obligations);
        try
        {
            Assert.Equal(issues, c.Binding.Issues);
            Assert.Equal(derived, c.Binding.DerivedIssues);
            Assert.Equal(obligations, c.Binding.Obligations);
            c.Binding.ReportDiagnostics();
            Assert.Equal(Records(twin), Records(c));
            Assert.Equal(twin.Binding.TableCounts, c.Binding.TableCounts);
            Assert.Equal(OriginRows(twin), OriginRows(c));
        }
        finally
        {
            layer.Nodes.Clear();
            layer.Issues.Clear();
            layer.DerivedIssues.Clear();
            layer.Obligations.Clear();
        }

        // Each published record with its related locations.
        static string[] Records(Compilation c) => c.Diagnostics.Finalize().Diagnostics
            .Select(static x => $"{x.Code} {x.Span} {x.Message} [{string.Join(", ", (x.Related ?? []).Select(static r => $"{r.Role} {r.Span}"))}]").ToArray();

        // The Origin rows of each body an obligation names, in the obligations' order.
        static int[] OriginRows(Compilation c) => c.Binding.Obligations.Where(static x => x.Body.IsBody).Select(x => c.Types.OriginRows(x.Body).Rows.Count).ToArray();
    }

    // PLAN G949 S1-r1b (s1r1-design 3.3, RC2's recorder): a pass binds the modules of its set, the library's table when it holds the
    // library module and a source module's when it holds the sources; a node of another module that it fails is a foreign failure,
    // listed with its earlier state. No pass fails another module's node today (the census: no program-pass write to a library node),
    // so both parts' lists stay empty after a Bind that fails nodes.
    [Fact]
    public void APassBindsTheModulesOfItsSet()
    {
        var c = CompilationTestHelper.Parse("let x: Missing = 1\n");
        Assert.False(c.Bind().IsComplete);
        var (library, source, part) = (c.Library.Kotonoha, c.SourceModules[0], c.Library.Layer.Part);
        var libraryPass = new Binding.BindingPass([], library, part);
        var programPass = new Binding.BindingPass([source], null, part);
        Assert.True(libraryPass.Binds(library.Syntax) && !libraryPass.Binds(source.Syntax) && libraryPass.Binds(null));
        Assert.True(programPass.Binds(source.Syntax) && !programPass.Binds(library.Syntax));
        Assert.True(new Binding.BindingPass([source], library, part).Binds(library.Syntax));
        Assert.Empty(part.ForeignFailures);
        var own = (Binding.PassPart)typeof(Binding).GetField("programPart", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        Assert.Empty(own.ForeignFailures);
    }

    // PLAN G949 S1-1 (k1; s11-design 2.1, DECISIONS BU-105 as S1-1 amends it): the pass driver's counters skip the Frozen image of the
    // stamps that copy them, so no run's value reads as a frozen stamp: the run counter's uint image (BindingSymbol's query stamp copies
    // the low 32 bits) skips uint.MaxValue, and the requirement counter (each pass's call, its part's RequirementCall) skips it too. The
    // counters start two values before their images; a Bind of Hello advances each past it, and every Bind's Result is the first's.
    // From S1-1's freeze (B4) the layer's RequirementCall is Frozen32 (s10) and no later pass calls in it, so the counter itself and the
    // program part's call are read.
    [Fact]
    public void TheRunCountersSkipTheirFrozenImages()
    {
        var c = CompilationTestHelper.Parse(Repository.ReadText("docs/examples/Hello/Hello.kimi".Split('/')));
        var first = c.Bind();
        Assert.True(first.IsComplete);
        var run = typeof(Binding).GetField("storageVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var call = typeof(Binding).GetField("originRequirementPass", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var own = (Binding.PassPart)typeof(Binding).GetField("programPart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        run.SetValue(c.Binding, 0x1_FFFF_FFFEUL);
        call.SetValue(c.Binding, uint.MaxValue - 1);
        for (var bind = 0; bind < 2; bind++)
        {
            Assert.Equal(first, c.Bind());
            Assert.NotEqual(uint.MaxValue, (uint)(ulong)run.GetValue(c.Binding)!);
            Assert.NotEqual(uint.MaxValue, (uint)call.GetValue(c.Binding)!);
            Assert.NotEqual(uint.MaxValue, own.RequirementCall);
            Assert.Equal(Binding.Frozen32, c.Library.Layer.Part.RequirementCall);
        }
    }

    // PLAN G949 S1-r1b (v5; s1r1-design 3.3, RC2's recorder; review-s1r1b N2), as S1-1 completes it (s11-design 3.3): a library node the
    // program pass fails is the program part's entry, listed once with the state the library pass counted it in and the state and
    // failure the program pass recorded, and its columns keep the library's final state: the recorder writes no library column. No
    // program fails one today (the census: no program-pass write to a library node over the corpus), so the row fails one through the
    // private Fail after a Final Bind, as a check of the program pass would: CheckBound then records it after the program's issues, the
    // Bind's Result counts it once by its recorded state (Resolved - 1 and Invalid + 1 against the Bind's), ReportDiagnostics publishes
    // one record of it, a second CheckBound publishes the one record again, not twice, and a second failure of the node adds no listing,
    // also after a check cleared its failure (N2: the recorder reads the entry, so a node is listed once per pass).
    [Fact]
    public void ALibraryNodeTheProgramPassFailsIsRecordedOnce()
    {
        var c = CompilationTestHelper.Parse("let x: Missing = 1\n");
        var bound = c.Bind();
        Assert.False(bound.IsComplete);
        var program = c.Binding.Issues.Select(static x => x.Node).ToArray();
        Assert.NotEmpty(program);
        Assert.DoesNotContain(c.Diagnostics.Finalize().Diagnostics, static x => x.Code == nameof(DiagnosticCode.TypeMismatch_Kd));
        Koto node = KotoTree.Walk(c.Library.Kotonoha.RootKoto).OfType<NumberLiteralKoto>().First(static x => x.StateOf() == BindingState.Resolved);
        var fail = typeof(Binding).GetMethod("Fail", BindingFlags.Instance | BindingFlags.NonPublic)!;
        fail.Invoke(c.Binding, [node, BindingFailure.TypeMismatch, false]);
        Assert.Equal((BindingState.Resolved, BindingFailure.None), (node.StateOf(), node.FailureOf()));
        fail.Invoke(c.Binding, [node, BindingFailure.TypeMismatch, false]);
        c.Binding.ClearFailure(node); // A check's retry (ClearFailure), then the node failed again in the same pass.
        Assert.Equal((BindingState.Resolved, BindingFailure.None), (node.StateOf(), node.FailureOf()));
        fail.Invoke(c.Binding, [node, BindingFailure.TypeMismatch, false]);
        var own = (Binding.PassPart)typeof(Binding).GetField("programPart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        Assert.Equal((node, BindingState.Resolved, BindingState.Invalid, BindingFailure.TypeMismatch), Assert.Single(own.ForeignFailures));
        Assert.Empty(c.Library.Layer.Part.ForeignFailures);
        for (var check = 0; check < 2; check++)
        {
            Assert.Equal(bound with { ResolvedCount = bound.ResolvedCount - 1, InvalidCount = bound.InvalidCount + 1 }, c.Binding.CheckBound());
            Assert.Equal(program.Append(node), c.Binding.Issues.Select(static x => x.Node));
            c.Binding.ReportDiagnostics();
            Assert.Single(c.Diagnostics.Finalize().Diagnostics, static x => x.Code == nameof(DiagnosticCode.TypeMismatch_Kd));
            Assert.Equal((BindingState.Resolved, BindingFailure.None), (node.StateOf(), node.FailureOf()));
        }
    }

    // PLAN G949 S1-r1b (v4; s1r1-design 1.3): CheckBound reruns the program pass's Check only, on Binding's own part, and composes the
    // Result again: after a Final Bind it equals the Bind's Result and issues, and the library layer's counts stay the library pass's.
    [Fact]
    public void CheckBoundKeepsTheLibraryLayersCounts()
    {
        var c = CompilationTestHelper.Parse("let x: Missing = 1\n");
        var bound = c.Bind();
        var (issues, layer) = (c.Binding.Issues.ToArray(), c.Library.Layer.Part.Counts);
        Assert.NotEqual(0, layer.ResolvedCount);
        Assert.Equal(bound, c.Binding.CheckBound());
        Assert.Equal(issues, c.Binding.Issues);
        Assert.Equal(layer, c.Library.Layer.Part.Counts);
    }

    // PLAN G949 S1-r1b (c15 per review-s1r1 Re-check 3 RC-d: composition identities, not totals): the Bind's Result is the library
    // layer's counts plus the program part's (ComposeResult); the layer's counts, imports and obligations are the empty program's (the
    // library pass binds the library alone); each composed list is the program part's entries, then the layer's (R1.1's order), so after
    // a Provisional Bind too the obligations list the program's rows first and the library's declaration rows after them (M2's order).
    // The corpus compare of the aggregates (base against head) is an evidence run (O6).
    [Theory]
    [InlineData(BindingMode.Final)]
    [InlineData(BindingMode.Provisional)]
    public void TheBindsAggregatesAreTheProgramPartThenTheLibraryLayers(BindingMode mode)
    {
        var empty = CompilationTestHelper.Parse(string.Empty);
        empty.Binding.Bind(mode);
        // A missing Type (an issue) and a symbolic length (a program TypeFormation obligation; SPEC 4.4): invalid by the first.
        var c = CompilationTestHelper.Parse("let x: Missing = 1\ngroup Dimensions\n    private let Width: isize = 4\n" +
            "    public func f<length N>(row: [(N + Width) of u8]) => ()\n");
        var result = c.Binding.Bind(mode);
        var (layer, own) = (c.Library.Layer.Part, (Binding.PassPart)typeof(Binding).GetField("programPart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!);
        Assert.Equal(
            (mode, layer.Counts.ResolvedCount + own.Counts.ResolvedCount, layer.Counts.UnresolvedCount + own.Counts.UnresolvedCount, layer.Counts.InvalidCount + own.Counts.InvalidCount),
            (result.Mode, result.ResolvedCount, result.UnresolvedCount, result.InvalidCount));
        Assert.Equal(empty.Library.Layer.Part.Counts, layer.Counts);
        Assert.Equal(empty.Library.Layer.Part.Imports.Select(static x => (x.Decl.Name, x.Library, x.Symbol, x.Kind)), layer.Imports.Select(static x => (x.Decl.Name, x.Library, x.Symbol, x.Kind)));
        Assert.Equal(empty.Library.Layer.Part.Obligations.Select(static x => x.ToString()), layer.Obligations.Select(static x => x.ToString()));
        Assert.NotEmpty(own.Obligations);
        Assert.NotEmpty(layer.Obligations);
        Assert.Equal(own.Obligations.Concat(layer.Obligations), c.Binding.Obligations);
        Assert.Equal(own.Imports.Concat(layer.Imports), c.Binding.LibraryImports);
        Assert.Equal(own.Issues.Concat(layer.Issues), c.Binding.Issues);
    }

    // PLAN G949 (s1r1-design 8.9; n55-design 2.3 rule 2): a keyed PassPart collection's one find reads the layer's map first, so a key
    // both parts hold reads the layer's row, and the running pass's map only for a key the layer's lacks; null when neither holds it.
    [Fact]
    public void AKeyedPartCollectionIsFoundInTheLayersPartFirst()
    {
        var layer = new Dictionary<int, string> { [1] = "layer" };
        var running = new Dictionary<int, string> { [1] = "program", [2] = "program" };
        Assert.Same(layer, Binding.PassPart.Find(Binding.PassPart.Parts(layer, running), 1, out var value));
        Assert.Equal("layer", value);
        Assert.Same(running, Binding.PassPart.Find(Binding.PassPart.Parts(layer, running), 2, out value));
        Assert.Equal("program", value);
        Assert.Null(Binding.PassPart.Find(Binding.PassPart.Parts(layer, running), 3, out _));
    }

    // PLAN G949, the localRegions batch (n55-design 2.3 rules 1-3; DECISIONS BU-220's D-9 line): the local-region graph is a PassPart
    // collection. A Bind records its rows in the running pass's part, the library pass's in the layer's and the program pass's in
    // Binding's own (S1-r1b), so the layer's part holds no program row; the program's rows moved after the Bind into the layer's part
    // are found there, and each region's sources, walked again through them, equal the program part's. `held`'s region reaches b's
    // borrow only through `view`'s row, so the walk reads a moved row past its first find too.
    [Fact]
    public void ALocalRegionInTheLayersPartIsFoundThere()
    {
        // A var local's region takes the sources of every value stored in it, `held`'s through `view`'s region (SPEC 15.3.6, 15.4.4,
        // 15.6.5): valid.
        var c = CompilationTestHelper.Parse("let a = 1\nlet b = 2\nvar view = a@ref\nview = b@ref\nvar held = view\nheld = a@ref\n" +
            "require view@follow == 2 and held@follow == 1 else => $abort(\"view\")\n");
        c.Bind();
        Assert.True(c.Binding.Result.IsComplete);
        var layer = c.Binding.Library.Layer.Part.Regions;
        var own = (Dictionary<BoundOrigin, Binding.LocalRegion>)typeof(Binding).GetField("localRegions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        Assert.DoesNotContain(own.Keys, layer.ContainsKey);
        var sources = own.Keys.Select(x => (Origin: x, Sources: c.Binding.LocalRegionSources(x).ToArray())).ToArray();
        Assert.Contains(sources, static x => x.Origin.Binder is VariableKoto { NameKoto: var name } && name.ToString() == "held" && x.Sources.Length >= 2);
        try
        {
            foreach (var (origin, region) in own)
            {
                region.Version = -1; // Walked again, through the rows' new part.
                layer.Add(origin, region);
            }

            own.Clear();
            Assert.All(sources, x => Assert.Equal(x.Sources, c.Binding.LocalRegionSources(x.Origin).ToArray()));
        }
        finally
        {
            foreach (var (origin, _) in sources)
            {
                layer.Remove(origin);
            }
        }
    }

    // Moves a list Binding's pass wrote, its own part's, into the library layer's part.
    private static void Move<T>(Compilation c, string field, List<T> layer)
    {
        var own = (List<T>)typeof(Binding).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        layer.AddRange(own);
        own.Clear();
    }

    private sealed class TestReplacement(CodeContext context) : ExpressionKoto(context, default)
    {
        public override KotoKind Akind => KotoKind.NumberLiteral;
    }

    private static string Describe(Compilation compilation)
        => string.Join(Environment.NewLine, compilation.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")) + $" ({compilation.Binding.Result})";

    private sealed class CollectVisitor : KotoVisitor
    {
        internal List<Koto> Nodes { get; } = new();

        public override void Visit(Koto node)
        {
            this.Nodes.Add(node);
            base.Visit(node);
        }
    }
}
