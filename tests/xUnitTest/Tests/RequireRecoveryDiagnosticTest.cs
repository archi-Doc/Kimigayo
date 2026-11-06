// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>
/// A require failure body that recovery supplied in place of a missing one: the fallthrough check still runs and rests on the
/// syntax Error, while a failure body written in source is judged on its own (docs/dev/DIAGNOSTICS.md §4.3, Later phases).
/// </summary>
public class RequireRecoveryDiagnosticTest
{
    // The forms of ARejectedDoKeepsTheRecordsOfItsParts: a source and its published Errors by location.
    private static readonly Dictionary<string, (string Source, string Records)> RejectedDos = new()
    {
        ["default"] = (
            "func value(c: bool, y: i32 = (do\n    require c else => ()\n    let z = 1\n    z + 7\n)) -> i32 => y\nlet v = value(true)\n",
            "TypeMismatch_Kd@30 RequireFallthrough_Kd@55"),
        ["let"] = (
            "func f(c: bool) -> i32\n    let y: i32 = do\n        require c else => ()\n        7\n    return y\nlet v = f(true)\n",
            "TypeMismatch_Kd@40 RequireFallthrough_Kd@69"),
        ["parenthesized"] = (
            "func f(c: bool) -> i32\n    let y: i32 = (do\n        require c else => ()\n        7\n    )\n    return y\nlet v = f(true)\n",
            "TypeMismatch_Kd@41 RequireFallthrough_Kd@70"),
        ["nested"] = (
            "func f(c: bool) -> i32\n    let y: i32 = do\n        let w: i32 = do\n            require c else => ()\n        require c else => ()\n" +
            "    return 1\nlet v = f(true)\n",
            "TypeMismatch_Kd@40 TypeMismatch_Kd@64 RequireFallthrough_Kd@97 RequireFallthrough_Kd@126"),
        ["continue"] = (
            "func f(c: bool) -> i32\n    while c\n        let y: i32 = do\n            if c => continue\n            require c else => ()\n" +
            "    return 1\nlet v = f(true)\n",
            "TypeMismatch_Kd@56 RequireFallthrough_Kd@118"),
        ["return"] = (
            "func f(c: bool) -> i32\n    let y: i32 = do\n        if c => return 2\n        require c else => ()\n    return 1\nlet v = f(true)\n",
            "TypeMismatch_Kd@40 RequireFallthrough_Kd@94"),
        ["exit to a built do"] = (
            "func f(c: bool) -> i32\n    let y: i32 = label outer: do\n        let w: i32 = do\n            if c => exit to outer 2\n" +
            "            require c else => ()\n        exit to outer 3\n    return y\nlet v = f(true)\n",
            "TypeMismatch_Kd@77 RequireFallthrough_Kd@146"),
        ["failed exit"] = (
            "func f(c: bool) -> i32\n    let y: i32 = label done: do\n        if c => exit to done ()\n        require c else => ()\n        ()\n" +
            "    return 1\nlet v = f(true)\n",
            "TypeMismatch_Kd@52 TypeMismatch_Kd@84 RequireFallthrough_Kd@113"),
        ["exit to it"] = (
            "func f(c: bool) -> i32\n    let y: i32 = label done: do\n        if c => exit to done 2\n        require c else => ()\n" +
            "    return y\nlet v = f(true)\n",
            "TypeMismatch_Kd@52"),
        ["unreachable exit to it"] = (
            "func f(c: bool) -> i32\n    let y: i32 = label done: do\n        if c\n            return 1\n            exit to done 2\n" +
            "        require c else => ()\n    return y\nlet v = f(true)\n",
            "TypeMismatch_Kd@52"),
        ["inner exit to it"] = (
            "func f(c: bool) -> i32\n    let y: i32 = label outer: do\n        let w: i32 = do\n            if c => exit to outer 2\n" +
            "            require c else => ()\n        require c else => ()\n    return y\nlet v = f(true)\n",
            "TypeMismatch_Kd@53 TypeMismatch_Kd@77"),
        ["if"] = (
            "func f(c: bool) -> i32\n    let y: i32 = if c\n        require c else => ()\n    else\n        ()\n    return 1\nlet v = f(true)\n",
            "TypeMismatch_Kd@40"),
    };

    // The statement, the code of the one published Error and the source text it underlines (empty at an insertion point).
    [Theory]
    [InlineData("require a == 1 && a == 2 else => $abort(\"x\")", nameof(DiagnosticCode.MisplacedSyntax_Kd), "&&")]
    [InlineData("require a == 1 || a == 2 else => $abort(\"x\")", nameof(DiagnosticCode.MisplacedSyntax_Kd), "||")]
    [InlineData("require a == 1 b else => $abort(\"x\")", nameof(DiagnosticCode.ExpectedSyntax_Kd), "b")]
    [InlineData("require a == 1\nlet b = 2", nameof(DiagnosticCode.MissingSyntax_Kd), "")]
    [InlineData("require a == 1 else\nlet b = 2", nameof(DiagnosticCode.MissingSyntax_Kd), "")]
    [InlineData("require a == 1 else =>\nlet b = 2", nameof(DiagnosticCode.MissingSyntax_Kd), "")]
    public void AGuessedFailureBodyRestsOnTheSyntaxError(string statement, string code, string text)
    {
        var c = MinimalEmissionTest.Analyze("let a = 1\n" + statement);
        var require = Assert.Single(Descendants(c.Kotonoha.RootKoto).OfType<RequireKoto>());

        // The check runs at the body the recovery supplied, which stands for the syntax Error.
        Assert.Same(require.ElseBody, Assert.Single(ControlFlowAnalysisTest.RequireFallthroughs(c)));
        Assert.NotNull(require.ElseBody is ErrorKoto error ? error.Cause : require.CodeContext.RecoveryCause(require.ElseBody));

        // Its record is derived: without the syntax partition its prerequisite is unresolved and the record is published ...
        var published = Publish(c);
        var derived = Assert.Single(c.Diagnostics.Finalize(DiagnosticPartition.ControlFlow, DiagnosticPartition.ControlFlow).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.PrerequisiteUnavailable_Kd), derived.Code);

        // ... and with it, the syntax Error explains the problem alone.
        var only = Assert.Single(published);
        Assert.Equal(code, only.Code);
        Assert.Equal(text, only.Text);
    }

    // A failure body written in source falls through whether or not else was written, whatever else failed on the line, and
    // even when it is itself a recovered form: the recovery guessed its combination, not its completion. Through a form Binding
    // rejected, written syntax keeps its completion (PLAN G47): a match's arm that completes, whatever arm the match lacks (SPEC
    // 14.8.4), and an if without else whose condition is not bool.
    [Theory]
    [InlineData("require a == 1 else\n    match a == 2\n        true => ()", "match a == 2\n        true => ()", new[] { nameof(DiagnosticCode.RequireFallthrough_Kd), nameof(DiagnosticCode.NonExhaustiveMatch_Kd) })]
    [InlineData("require a == 1 else\n    match a == 2\n        true => ()\n        false => $abort(\"x\")", "match a == 2\n        true => ()\n        false => $abort(\"x\")", new[] { nameof(DiagnosticCode.RequireFallthrough_Kd) })]
    [InlineData("require a == 1 else\n    if a => $abort(\"x\")", "if a => $abort(\"x\")", new[] { nameof(DiagnosticCode.RequireFallthrough_Kd), nameof(DiagnosticCode.TypeMismatch_Kd) })]
    [InlineData("require a == 1 else => ()", "()", new[] { nameof(DiagnosticCode.RequireFallthrough_Kd) })]
    [InlineData("require a == 1 else\n    ()", "()", new[] { nameof(DiagnosticCode.RequireFallthrough_Kd) })]
    [InlineData("require a == 1 => ()", "()", new[] { nameof(DiagnosticCode.ExpectedSyntax_Kd), nameof(DiagnosticCode.RequireFallthrough_Kd) })]
    [InlineData("require a == 1 => ()\nrequire a == 2 && a == 3 else => $abort(\"x\")", "()", new[] { nameof(DiagnosticCode.ExpectedSyntax_Kd), nameof(DiagnosticCode.RequireFallthrough_Kd), nameof(DiagnosticCode.MisplacedSyntax_Kd) })]
    [InlineData("require a == 1 else => 0 < a < 2", "0 < a < 2", new[] { nameof(DiagnosticCode.RequireFallthrough_Kd), nameof(DiagnosticCode.ChainedComparison_Kd) })]
    public void AWrittenFailureBodyIsJudgedOnItsOwn(string statement, string body, string[] codes)
    {
        var c = MinimalEmissionTest.Analyze("let a = 1\n" + statement);
        var published = Publish(c);
        Assert.Equal(codes, published.Select(static x => x.Code));
        Assert.Equal(body, Assert.Single(published, static x => x.Code == nameof(DiagnosticCode.RequireFallthrough_Kd)).Text);

        // The MIR's A3 records every falling failure body (DECISIONS R4-M7a), a guessed failure body's included.
        Assert.Equal(Descendants(c.Kotonoha.RootKoto).OfType<RequireKoto>().Select(static x => (Koto)x.ElseBody), ControlFlowAnalysisTest.RequireFallthroughs(c));
    }

    // SPEC 23.1.2: a written failure body is judged on its own also inside an expression whose own check failed: an indented do body
    // supplies Unit (SPEC 14.2), which does not fit the default's i32, and that failure does not explain the failure body that completes.
    // The rejected do keeps the parts Binding resolved (DECISIONS BU-106, PLAN G414 (1); fail-before: since H5 the body reached A3 as one
    // Opaque, R5-M63's residual line, and the record was lost).
    [Fact]
    public void AFailureBodyInARejectedExpressionIsJudgedOnItsOwn()
    {
        const string source = "func value(c: bool, y: i32 = (do\n    require c else => ()\n    7\n)) -> i32 => y\nlet v = value(true)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Accepts);
        var published = Publish(c);
        Assert.Equal(2, published.Length);
        var mismatch = Assert.Single(published, static x => x.Code == nameof(DiagnosticCode.TypeMismatch_Kd));
        Assert.Equal((source.IndexOf("do\n", StringComparison.Ordinal), "do\n    require c else => ()\n    7"), (mismatch.Span.Start, mismatch.Text));
        var fallthrough = Assert.Single(published, static x => x.Code == nameof(DiagnosticCode.RequireFallthrough_Kd));
        Assert.Equal((source.IndexOf("()", StringComparison.Ordinal), "()"), (fallthrough.Span.Start, fallthrough.Text));
    }

    // SPEC 23.1.2 (DECISIONS BU-106): a rejected do's parts keep their own records wherever the do stands (a default, a let initializer,
    // in parentheses, nested, beside a continue, a return or an exit to a built construct, or beside a failed exit, which is derived and
    // goes to no target); a labeled do that a jump published without failure goes to keeps nothing, also where the jump is unreachable
    // (SPEC 14.10.3), since the builder opens no target for it; a rejected if keeps nothing (PLAN G414 (2)).
    [Theory]
    [InlineData("default")]
    [InlineData("let")]
    [InlineData("parenthesized")]
    [InlineData("nested")]
    [InlineData("continue")]
    [InlineData("return")]
    [InlineData("exit to a built do")]
    [InlineData("failed exit")]
    [InlineData("exit to it")]
    [InlineData("unreachable exit to it")]
    [InlineData("inner exit to it")]
    [InlineData("if")]
    public void ARejectedDoKeepsTheRecordsOfItsParts(string form)
    {
        var (source, records) = RejectedDos[form];
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Accepts);
        Assert.Equal(records, string.Join(" ", Publish(c).OrderBy(static x => x.Span.Start).Select(static x => $"{x.Code}@{x.Span.Start}")));
    }

    [Theory]
    [InlineData("require a == 1 and a == 2 else => $abort(\"x\")")]
    [InlineData("require a == 1 or not (a == 2) else\n    $abort(\"x\")")]
    public void TheLogicalKeywordsAreValid(string statement)
        => Assert.Empty(Publish(MinimalEmissionTest.Analyze("let a = 1\n" + statement)));

    private static TestDiagnostic[] Publish(Compilation c)
    {
        c.Binding.ReportDiagnostics();
        c.Binding.ReportStartupDiagnostics();
        c.ReportAnalysis();
        return TestDiagnostics.Of(c, "Hello.kimi").Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
    }

    private static IEnumerable<Koto> Descendants(Koto node)
    {
        foreach (var child in node.ChildNodes)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
