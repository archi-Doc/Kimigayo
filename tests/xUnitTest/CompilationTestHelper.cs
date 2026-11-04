// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

internal static class CompilationTestHelper
{
    /// <summary>The prefix of a library document's location.</summary>
    internal const string Library = "Kimi/";

    // Preparation and parsing only: callers choose when to Bind and analyze ownership.
    internal static Compilation Parse(string source, string? path = null)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        if (path is null)
        {
            c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        }
        else
        {
            c.Kotonoha.AddSource(new SourceDocument(path, source));
        }

        return c;
    }

    internal static Compilation ParseSuccess(string source, string? path = null)
    {
        var c = Parse(source, path);
        Assert.Empty(TestDiagnostics.Of(c));
        return c;
    }

    internal static Compilation BindSuccess(string source)
    {
        var c = ParseSuccess(source);
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
        return c;
    }

    // Restore syntax without Binding or ownership analysis so invalidation stays observable.
    internal static Compilation Reload(Compilation c)
    {
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.NotNull(tree);
        Assert.Same(restored.Kotonoha, tree);
        tree.OnDeserialized(restored);
        return restored;
    }

    internal static string WriteIr(Compilation c)
    {
        using var writer = new StringWriter();
        if (!c.Generate(writer))
        {
            Assert.Fail(MinimalEmissionTest.Describe(c));
        }

        return writer.ToString();
    }

    // Binding's warnings of the latest final pass, such as its HIR lints (SPEC 17.5), as ReportDiagnostics publishes them.
    internal static TestDiagnostic[] BindingWarnings(Compilation c)
    {
        c.Binding.ReportDiagnostics();
        return [.. TestDiagnostics.Of(c).Where(static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Warning)];
    }

    // The Errors published once Binding reports the latest final pass, its HIR lints included (SPEC 14.3, 14.4, 14.5.2).
    internal static TestDiagnostic[] BindingErrors(Compilation c)
    {
        c.Binding.ReportDiagnostics();
        return [.. TestDiagnostics.Of(c).Where(static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error)];
    }

    // The problem codes of a Comparison Loan conflict: the conflicting access decides the code (OwnershipIssue.Code).
    internal static bool IsLoanConflict(this DiagnosticCode code)
        => code is DiagnosticCode.ComparisonLoanConflict_Kd or DiagnosticCode.CallReservationConflict_Kd or DiagnosticCode.CallActivationConflict_Kd or DiagnosticCode.PlacementActivationConflict_Kd;

    // The problem codes of an unproven Origin obligation: what is unproven decides the code (OwnershipIssue.Code).
    internal static bool IsUnprovenOrigin(this DiagnosticCode code)
        => code is DiagnosticCode.UnsatisfiedOriginRelation_Kd or DiagnosticCode.UnprovenOriginRelation_Kd;

    /// <summary>Gets a node's location as its document and start offset; a library document is <c>Kimi/</c> and its file name.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The location, or <c>?</c> without a node.</returns>
    internal static string Location(Koto? node)
        => node is null ? "?" : Document(node) + ":" + node.Span.Start;

    private static string Document(Koto? node)
    {
        var path = node?.CodeContext?.SourceDocument?.Path;
        return path is null ? "?" : path.StartsWith("compiler://", StringComparison.Ordinal) ? Library + path[(path.LastIndexOf('/') + 1)..] : path;
    }
}
