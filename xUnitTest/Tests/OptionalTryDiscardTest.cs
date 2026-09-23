// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class OptionalTryDiscardTest
{
    [Theory]
    [InlineData("let value: i32? = .None")]
    [InlineData("let value: i32?? = .Some(.None)")]
    [InlineData("let value: ((i32) -> i32)? = .None")]
    [InlineData("_ = 3")]
    [InlineData("_ = if true => 1 else => 2")]
    [InlineData("let items: [2 of i32] = [1, 2]\nfor _ in items => ()")]
    public void AcceptsNewForms(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.True(c.Ownership.Result.IsVerified, Describe(c));
    }

    [Theory]
    [InlineData("let _: i32 = 1")]
    [InlineData("func f(_: i32) => ()")]
    [InlineData("let f = func (_: i32) => ()")]
    [InlineData("let x = (_ = 1)")]
    [InlineData("_ += 1")]
    [InlineData("let x = 1@ref?")]
    [InlineData("let x: (i32)? -> i32")]
    public void RejectsSyntaxOutsideDedicatedPositions(string source)
    {
        var parsed = ParseTestHelper.Parse(source);
        Assert.NotEmpty(parsed.DiagnosticCollection.GetArray());
    }

    [Theory]
    [InlineData("Option", "func source() -> i32? => .Some(7)\nfunc run() -> i32? => .Some((try source()) + 1)\nmatch run()\n    .Some(let n) => require n == 8 else => $abort(\"value\")\n    .None => $abort(\"none\")")]
    [InlineData("None", "func source() -> i32? => .None\nfunc run() -> bool?\n    let n = try source()\n    $abort(\"continued\")\nmatch run()\n    .Some(_) => $abort(\"some\")\n    .None => ()")]
    [InlineData("Result", "func source() -> Result<i32, i32> => .Err(9)\nfunc run() -> Result<bool, i32>\n    let n = try source()\n    return .Ok(n == 1)\nmatch run()\n    .Ok(_) => $abort(\"ok\")\n    .Err(let n) => require n == 9 else => $abort(\"error\")")]
    [InlineData("Discard", "func source() -> Result<i32, i32> => .Ok(1)\nfunc run() -> Result<(), i32>\n    _ = try source()\n    return .Ok(())\n_ = run()\n_ = \"destroy\"")]
    public void ExecutesBothPaths(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, Describe(c));
        ScalarEmissionTest.EmitFixture("OptionalTry" + name, source + "\nConsole.writeLine(\"ok\")", "ok\n");
    }

    [Theory]
    [InlineData("func source() -> i32? => .None\nfunc run() -> i32 => try source()")]
    [InlineData("func source() -> i32? => .None\nlet x = try source()")]
    [InlineData("func run() -> i32? => .Some(try 1)")]
    [InlineData("func run() -> i32? => .Some(try .None)")]
    [InlineData("func source() -> Result<(), i32> => .Ok(())\nfunc run() -> Result<(), i32> => try source()")]
    [InlineData("func source() -> Result<i32, string> => .Err(\"bad\")\nfunc run() -> Result<i32, i32> => .Ok(try source())")]
    [InlineData("func source() -> i32? => .Some(1)\nfunc run() -> Result<i32, i32> => .Ok(try source())")]
    [InlineData("func source() -> i32? => .Some(1)\nfunc run() -> i32?\n    defer => _ = try source()\n    return .None")]
    public void RejectsInvalidPropagation(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    [Theory]
    [InlineData("try source()", true)]
    [InlineData("_ = try source()", false)]
    [InlineData("source()", true)]
    [InlineData("_ = source()", false)]
    public void WarnsOnlyForImplicitDiscard(string statement, bool warning)
    {
        var c = MinimalEmissionTest.Analyze("func source() -> Result<i32, i32> => .Ok(1)\nfunc run() -> Result<(), i32>\n    " + statement + "\n    return .Ok(())");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Equal(warning, flow.Warnings.Count != 0);
    }

    [Theory]
    [InlineData("ref{static}/i32?", "Option<ref{static}/i32>")]
    [InlineData("ref{static}/(i32?)", "ref{static}/Option<i32>")]
    [InlineData("i32??", "Option<Option<i32>>")]
    [InlineData("(i32) -> i32?", "(i32) -> Option<i32>")]
    [InlineData("((i32) -> i32)?", "Option<(i32) -> i32>")]
    [InlineData("[2 of i32?]?", "Option<[2 of Option<i32>]>")]
    public void OptionalSpellingHasIdenticalType(string abbreviated, string complete)
    {
        var c = MinimalEmissionTest.Analyze($"func first(value: {abbreviated}) => ()\nfunc second(value: {complete}) => ()");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        var functions = Descendants(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(x => !x.IsGenerated).ToArray();
        Assert.Same(functions[0].Parameters[0].Type.BoundType, functions[1].Parameters[0].Type.BoundType);
    }

    [Theory]
    [InlineData("Nested", "func source() -> i32?? => .Some(.Some(8))\nfunc run() -> i32? => .Some(try try source())\nmatch run()\n    .Some(8) => ()\n    _ => $abort(\"nested\")")]
    [InlineData("SingleLayer", "func source() -> i32?? => .Some(.None)\nfunc run() -> bool?\n    let inner = try source()\n    let isNone = match inner\n        .None => true\n        .Some(_) => false\n    return .Some(isNone)\nmatch run()\n    .Some(true) => ()\n    _ => $abort(\"flattened\")")]
    [InlineData("Borrow", "struct S\n    public var n: i32\n    public init(n: i32) => self.n = n\nfunc wrap(x: ref/S) -> ref{x}/S? => .Some(x)\nfunc run(x: ref/S) -> i32?\n    let r = try wrap(x)\n    return .Some(r.n)\nlet s = S.init(8)\nmatch run(s)\n    .Some(8) => ()\n    _ => $abort(\"borrow\")")]
    [InlineData("Lambda", "func source() -> i32? => .Some(8)\nlet f = func () -> i32? => .Some(try source())\nmatch f()\n    .Some(8) => ()\n    _ => $abort(\"lambda\")")]
    [InlineData("Generic", "func unwrap<T>(x: T?) -> T? => .Some(try x@move)\nmatch unwrap<i32>(.Some(8))\n    .Some(8) => ()\n    _ => $abort(\"generic\")\n_ = unwrap<string>(.Some(\"owned\"))")]
    public void ExecutesCombinations(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, Describe(c));
        ScalarEmissionTest.EmitFixture("OptionalTry" + name, source + "\nConsole.writeLine(\"ok\")", "ok\n");
    }

    [Theory]
    [InlineData("try source()", "()", 0)]
    [InlineData("source()", "()", 2)]
    [InlineData("(try source())", "i32", 3)]
    [InlineData("if true => try source()", "i32", 3)]
    [InlineData("try source()", "Result<(), i32>", 2)]
    [InlineData("let x = try source()", "i32", 0)]
    [InlineData("_ = do\n        source()", "i32", 1)]
    public void WarningPriorityIsLocalToDiscard(string statement, string payload, int priority)
    {
        var value = payload == "()" ? "()" : payload.StartsWith("Result", StringComparison.Ordinal) ? ".Ok(())" : "1";
        var c = MinimalEmissionTest.Analyze($"func source() -> Result<{payload}, i32> => .Ok({value})\nfunc run() -> Result<(), i32>\n    {statement}\n    return .Ok(())");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        var warnings = c.AnalyzeControlFlow(c.Binding.TypeSystem).Warnings;
        if (priority == 0)
        {
            Assert.Empty(warnings);
        }
        else
        {
            Assert.Equal(priority, Assert.Single(warnings).Priority);
        }
    }

    [Fact]
    public void ExplicitDiscardDestroysAtStatementEnd()
        => ScalarEmissionTest.EmitFixture("OptionalTryDiscardCleanup", "struct Token\n    deinit => Console.writeLine(\"destroy\")\n_ = Token.init()\nConsole.writeLine(\"after\")", "destroy\nafter\n");

    [Fact]
    public void FailureCleansAcquiredArgumentsBeforeFunctionLocals()
        => EmitChecked("OptionalTryFailureCleanup", "struct Token\n    let id: i32\n    public init(id: i32) => self.id = id\n    deinit\n        if self.id == 1 => Console.writeLine(\"local\")\n        if self.id == 2 => Console.writeLine(\"argument\")\n        if self.id == 3 => Console.writeLine(\"late\")\nfunc fail() -> Result<i32, string> => .Err(\"error\")\nfunc skipped(a: Token, b: i32, c: Token) => $abort(\"called\")\nfunc run() -> Result<(), string>\n    let local = Token.init(1)\n    defer => Console.writeLine(\"defer\")\n    skipped(Token.init(2), try fail(), Token.init(3))\n    return .Ok(())\nmatch run()\n    .Ok(_) => $abort(\"ok\")\n    .Err(let e) => Console.writeLine(e)", "argument\ndefer\nlocal\nerror\n");

    [Theory]
    [InlineData("Partial", "(key, _)", "sum += key", 4)]
    [InlineData("Repeated", "(_, _)", "sum += 1", 2)]
    [InlineData("Whole", "_", "sum += 1", 2)]
    [InlineData("Named", "pair", "sum += pair.0", 4)]
    public void UnnamedIterationPreservesShape(string name, string binding, string body, int total)
        => EmitChecked("OptionalTryFor" + name, $"let pairs: [2 of (i32, i32)] = [(1, 2), (3, 4)]\nvar sum = 0\nfor {binding} in pairs@move => {body}\nrequire sum == {total} else => $abort(\"iteration\")", string.Empty);

    [Fact]
    public void RebindingRetainsCurrentTryArms()
    {
        var c = MinimalEmissionTest.Analyze("func run(x: i32?) -> i32? => .Some(try x)\n_ = run(.Some(1))");
        for (var i = 0; i < 3; i++)
        {
            Assert.True(c.Bind().IsComplete, Describe(c));
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
            Assert.True(c.Emission.Validate(out var error), error);
        }
    }

    [Theory]
    [InlineData("func run(x: i32??) -> i32? => .Some(try try x)")]
    [InlineData("let x: ref{static}/(i32?)? = .None")]
    [InlineData("let x: [1 of i32?] = [.None]\n_ = x")]
    [InlineData("for (key, _) in pairs => _ = key\nfor (_, _) in pairs => ()")]
    [InlineData("if true => _ = prepare() else => _ = prepare()")]
    [InlineData("let _value = 1_000\nfunc tryGet() -> i32 => _value")]
    [InlineData("#switch\n    #case _\n        let items: [2 of _] = [1, 2]\n        _ = items")]
    public void SyntaxSurvivesWritingAndSerialization(string source)
    {
        var tree = ParseTestHelper.Parse(source);
        ParseTestHelper.AssertValid(tree);
        var output = tree.RootKoto.ToString();
        ParseTestHelper.AssertValid(ParseTestHelper.Parse(output));
        Assert.DoesNotContain("$try.", output);
        var restored = TinyhandSerializer.Deserialize<Kotonoha>(TinyhandSerializer.Serialize(tree))!;
        restored.OnDeserialized(Compilation.CreateForTest());
        ParseTestHelper.AssertValid(restored);
        Assert.Equal(output, restored.RootKoto.ToString());
        Assert.All(Descendants(restored.RootKoto), n => Assert.All(n.ChildNodes, child => Assert.Same(n, child.Parent)));
    }

    [Theory]
    [InlineData("let _value: i32 = 3\n_ = _value", true)]
    [InlineData("func make<T>() -> T? => .None\nfunc run() -> i32?\n    let x: i32 = try make()\n    return .Some(x)", false)]
    [InlineData("func make<T>() -> T? => .None\nfunc run() -> i32?\n    let x: i32 = try make<i32>()\n    return .Some(x)", true)]
    [InlineData("func make() -> string? => .Some(\"owned\")\nfunc run() -> ()?\n    let saved = make()\n    _ = try saved@move\n    _ = saved@move\n    return .Some(())", false)]
    [InlineData("func run(x: ref/(i32?)) -> i32? => .Some(try x)", false)]
    [InlineData("func run<T>(x: T) -> i32? => .Some(try x)", false)]
    [InlineData("func run() -> i32?\n    return .Some(1)\n    _ = try 1", false)]
    [InlineData("func source() -> i32? => .Some(1)\nfunc run(x: i32 = try source()) -> i32? => .Some(x)", false)]
    [InlineData("let value: i32? = null", false)]
    [InlineData("let x = 1\n_ = x@i32?", false)]
    [InlineData("func run(x: i32?) -> i32? => .Some(try (x))", true)]
    [InlineData("enum Option<T>\n    Other\nlet x: i32? = .Some(1)", true)]
    [InlineData("enum Option<T>\n    Some(T)\n    None\nfunc run(x: Option<i32>) -> Option<i32> => .Some(try x)", false)]
    [InlineData("let pairs: [1 of (i32, i32)] = [(1, 2)]\nfor (x, x) in pairs => ()", false)]
    [InlineData("let pairs: [1 of (i32, i32)] = [(1, 2)]\nfor (_, _, _) in pairs => ()", false)]
    public void PreservesInferenceOwnershipAndBindingRules(string source, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified && !c.Kotonoha.DiagnosticCollection.HasErrors);
    }

    [Theory]
    [InlineData("func try() => ()")]
    [InlineData("let x = _")]
    [InlineData("let f = func [_]() => ()")]
    [InlineData("let x: i32?{a}")]
    [InlineData("let x = i32?.Some(1)")]
    [InlineData("let x = try\nlet y = 1")]
    [InlineData("_ =")]
    [InlineData("let x = value is Dog?")]
    [InlineData("let x = value@ref{a}/T")]
    [InlineData("let x = value@(ref{a}/T)")]
    [InlineData("let x = value@ref/(ref{a}/T)")]
    public void RejectsReservedAndIncompleteForms(string source)
        => Assert.NotEmpty(ParseTestHelper.Parse(source).DiagnosticCollection.GetArray());

    [Fact]
    public void GenericWarningsAreIssuedAtDefinitionOnce()
    {
        var c = MinimalEmissionTest.Analyze("func process<T>(x: Result<T, i32>) -> Result<(), i32>\n    try x@move\n    return .Ok(())\n_ = process<()>(.Ok(()))\n_ = process<i32>(.Ok(1))");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, Describe(c));
        var warning = Assert.Single(c.AnalyzeControlFlow(c.Binding.TypeSystem).Warnings);
        Assert.Equal(3, warning.Priority);
        Assert.IsType<TryKoto>(warning.Node);
    }

    [Fact]
    public void NeverSuccessStillChecksFailureAndHasNoDiscardWarning()
    {
        var c = MinimalEmissionTest.Analyze("func source() -> Result<Never, i32> => .Err(1)\nfunc run() -> Result<(), i32>\n    try source()");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Warnings);
        Assert.Empty(flow.Issues);
    }

    [Fact]
    public void TryDiagnosticExplainsTheNormalPayload()
    {
        var c = MinimalEmissionTest.Analyze("func save() -> Result<(), i32> => .Ok(())\nfunc run() -> Result<(), i32> => try save()");
        c.Binding.CheckBound();
        c.Binding.ReportDiagnostics();
        c.AnalyzeControlFlow(c.Binding.TypeSystem).ReportDiagnostics();
        Assert.Contains(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray(), d => d.Message.Contains("payload Type", StringComparison.Ordinal) && d.Message.Contains("without try", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitWholeResultDiscardCleansError()
        => EmitChecked("OptionalTryIgnoreError", "struct Error\n    deinit => Console.writeLine(\"error destroyed\")\nfunc source() -> Result<(), Error> => .Err(Error.init())\n_ = source()\nConsole.writeLine(\"after\")", "error destroyed\nafter\n");

    [Fact]
    public void SuccessMoveDestroysOnlyTheSecuredValue()
        => EmitChecked("OptionalTryMoveCleanup", "struct Token\n    deinit => Console.writeLine(\"destroy\")\nfunc run(x: Token?) -> Token? => .Some(try x@move)\n_ = run(.Some(Token.init()))\nConsole.writeLine(\"after\")", "destroy\nafter\n");

    [Theory]
    [InlineData("try source()")]
    [InlineData("_ = try source()")]
    public void BothDiscardFormsDestroySuccessAtStatementEnd(string statement)
        => EmitChecked(statement[0] == '_' ? "OptionalTryExplicitPayloadCleanup" : "OptionalTryImplicitPayloadCleanup", $"struct Token\n    deinit => Console.writeLine(\"destroy\")\nfunc source() -> Token? => .Some(Token.init())\nfunc run() -> ()?\n    {statement}\n    Console.writeLine(\"after\")\n    return .Some(())\n_ = run()", "destroy\nafter\n");

    [Fact]
    public void WarmTryBindingAndFlowReuseStorage()
    {
        var c = MinimalEmissionTest.Analyze("func run(x: i32?) -> i32? => .Some(try x)");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Bind();
            flow.Reanalyze(c.Kotonoha.RootKoto);
        }));
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
    }

    [Fact]
    public void DiscardedTryKeepsItsSuccessType()
    {
        var c = MinimalEmissionTest.Analyze("func run(x: i32?) -> ()?\n    try x\n    return .Some(())");
        var propagation = Assert.Single(Descendants(c.Kotonoha.RootKoto).OfType<TryKoto>());
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        Assert.Same(BoundType.I32, flow.Nodes[propagation].ExpressionType);
    }

    [Fact]
    public void ExecutesDocumentedExample()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../examples/OptionalTryDiscard/OptionalTryDiscard.kimi"));
        EmitChecked("OptionalTryExample", File.ReadAllText(path), "ready\nnot ready\none layer\n");
    }

    [Theory]
    [InlineData("Optional", "ref{a}/S?")]
    [InlineData("Expanded", "Option<ref{a}/S>")]
    public void OptionalAdaptationRetainsExistingPayloadOrigins(string name, string target)
        => EmitChecked("OptionalTryAdaptation" + name, $"struct S\n    public var n: i32 = 8\nfunc identity(x: ref{{a}}/S?) -> ref{{a}}/S? => x@{target}\nfunc wrap(x: ref/S) -> ref{{x}}/S? => .Some(x)\nlet s = S.init()\nmatch identity(wrap(s))\n    .Some(let r) => require r.n == 8 else => $abort(\"payload\")\n    .None => $abort(\"none\")", string.Empty);

    [Fact]
    public void OptionalIdentityAcquisitionMovesOwnedPayload()
    {
        // SPEC 13.5.3: Identity Acquisition never transfers a Non-Copy Place; the transferred temporary is acquired instead.
        const string Declaration = "struct Token\n    deinit => Console.writeLine(\"destroy\")\nlet x: Token? = .Some(Token.init())\n";
        var rejected = MinimalEmissionTest.Analyze(Declaration + "_ = x@Token?");
        Assert.False(rejected.Binding.Result.IsComplete);
        Assert.Contains(rejected.Binding.Issues, x => x.Code == DiagnosticCode.TransferRequired_Kd);
        EmitChecked("OptionalTryIdentityMove", Declaration + "_ = x@move@Token?", "destroy\n");
    }

    private static void EmitChecked(string name, string source, string expected)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, Describe(c));
        ScalarEmissionTest.EmitFixture(name, source, expected);
    }

    private static IEnumerable<Koto> Descendants(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static string Describe(Compilation c)
        => MinimalEmissionTest.Describe(c, null) + "\n" + string.Join("\n", c.Binding.Issues.Select(x => x.Node.GetType().Name + ":" + x.Node.BindingFailure + ":" + x.Node)) +
            "\n" + string.Join("\n", c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray().Select(x => x.Message)) +
            "\n" + string.Join("\n", c.Ownership.ControlFlow?.Issues.Select(x => x.Message) ?? []) +
            "\nPending: " + string.Join(", ", c.Ownership.ControlFlow?.PendingBinding.Select(x => x.ToString()) ?? []);
}
