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
        var issue = Assert.Single(c.Ownership.ControlFlow!.Issues);
        Assert.Equal(DiagnosticCode.RequireFallthrough_Kd, issue.Code);
        Assert.Same(require.ElseBody, issue.Node);
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
    // even when it is itself a recovered form: the recovery guessed its combination, not its completion.
    [Theory]
    [InlineData("require a == 1 else => ()", "()", new[] { nameof(DiagnosticCode.RequireFallthrough_Kd) })]
    [InlineData("require a == 1 else\n    ()", "()", new[] { nameof(DiagnosticCode.RequireFallthrough_Kd) })]
    [InlineData("require a == 1 => ()", "()", new[] { nameof(DiagnosticCode.ExpectedSyntax_Kd), nameof(DiagnosticCode.RequireFallthrough_Kd) })]
    [InlineData("require a == 1 => ()\nrequire a == 2 && a == 3 else => $abort(\"x\")", "()", new[] { nameof(DiagnosticCode.ExpectedSyntax_Kd), nameof(DiagnosticCode.RequireFallthrough_Kd), nameof(DiagnosticCode.MisplacedSyntax_Kd) })]
    [InlineData("require a == 1 else => 0 < a < 2", "0 < a < 2", new[] { nameof(DiagnosticCode.RequireFallthrough_Kd), nameof(DiagnosticCode.ChainedComparison_Kd) })]
    public void AWrittenFailureBodyIsJudgedOnItsOwn(string statement, string body, string[] codes)
    {
        var published = Publish(MinimalEmissionTest.Analyze("let a = 1\n" + statement));
        Assert.Equal(codes, published.Select(static x => x.Code));
        Assert.Equal(body, Assert.Single(published, static x => x.Code == nameof(DiagnosticCode.RequireFallthrough_Kd)).Text);
    }

    // SPEC 14.8.4, 14.11.2: a Result-requiring match in the failure body completes only through its arms; its missing
    // coverage is the one Error.
    [Theory]
    [InlineData("require a == 1 else => match a\n    0 => $abort(\"x\")", new[] { nameof(DiagnosticCode.NonExhaustiveMatch_Kd) })]
    [InlineData("require a == 1 else => match a\n    0 => $abort(\"x\")\n    _ => ()", new[] { nameof(DiagnosticCode.RequireFallthrough_Kd) })]
    public void AFailureBodyMatchCompletesOnlyThroughItsArms(string statement, string[] codes)
        => Assert.Equal(codes, Publish(MinimalEmissionTest.Analyze("let a = 1\n" + statement)).Select(static x => x.Code));

    [Theory]
    [InlineData("require a == 1 and a == 2 else => $abort(\"x\")")]
    [InlineData("require a == 1 or not (a == 2) else\n    $abort(\"x\")")]
    public void TheLogicalKeywordsAreValid(string statement)
        => Assert.Empty(Publish(MinimalEmissionTest.Analyze("let a = 1\n" + statement)));

    private static TestDiagnostic[] Publish(Compilation c)
    {
        c.Binding.ReportDiagnostics();
        c.Binding.ReportStartupDiagnostics();
        c.Ownership.ControlFlow!.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
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
