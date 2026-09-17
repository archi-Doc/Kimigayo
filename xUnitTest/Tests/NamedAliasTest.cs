// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class NamedAliasTest
{
    [Theory]
    [InlineData("Console.writeLine(\"Hello\")")]
    [InlineData("Kimi.Console.writeLine(\"Hello\")")]
    [InlineData("::Kimi.Console.writeLine(text: \"Hello\")")]
    [InlineData("alias Kimi.Console\nwriteLine(\"Hello\")")]
    [InlineData("alias ::Kimi.Console\nwriteLine(\"Hello\")")]
    [InlineData("alias O => Kimi.Console\nO.writeLine(\"Hello\")")]
    [InlineData("alias O => ::Kimi.Console\nO.writeLine(\"Hello\")")]
    [InlineData("alias K => Kimi\nK.Console.writeLine(\"Hello\")")]
    [InlineData("alias O => Kimi.Console\nalias O => ::Kimi.Console\nO.writeLine(\"Hello\")")]
    public void AliasesPreserveOutputIdentityAndEmission(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
        Assert.All(
            All(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Where(x => x.BoundCall is not null),
            x => Assert.Same(c.Library.WriteLine, x.BoundCall!.Target));
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out error), error);
        Assert.Contains("@__kimi_write_line", writer.ToString());
    }

    [Theory]
    [InlineData("writeLine(\"x\")")]
    [InlineData("Kimi.writeLine(\"x\")")]
    [InlineData("Core.writeLine(\"x\")")]
    [InlineData("alias A => Kimi.Console\nwriteLine(\"x\")")]
    [InlineData("alias K => Kimi\nalias A => K.Console")]
    [InlineData("alias A => i32")]
    [InlineData("alias A => Kimi.Copy")]
    [InlineData("alias A => Kimi.Option<i32>")]
    [InlineData("alias A => Kimi.Console.writeLine")]
    [InlineData("alias A = i32")]
    [InlineData("alias A.B => Kimi.Console")]
    [InlineData("alias A => 1 + 2")]
    [InlineData("alias A =>\n    Kimi.Console")]
    [InlineData("alias A => Kimi\nalias A => Kimi.Console")]
    [InlineData("alias A => Kimi.Console\nlet x = A")]
    [InlineData("alias A => Kimi.Console\nlet x: A")]
    [InlineData("alias A => Kimi.Console\n::A.writeLine(\"x\")")]
    [InlineData("group Kimi")]
    [InlineData("public alias A => Kimi.Console")]
    [InlineData("group G\n    alias A => Kimi.Console")]
    [InlineData("let x = 1\nalias A => Kimi.Console")]
    public void InvalidAliasesAndRemovedNamesAreRejected(string source)
    {
        var c = Parse(source);
        var result = c.Bind();
        Assert.True(c.Kimigayo.GetOrAddDiagnosticCollection("Aliases.kimi").GetArray().Any(x => x.Entry.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error) || !result.IsComplete, source);
    }

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "Kimi" }, false)]
    [InlineData(new[] { "Kimi.Console" }, true)]
    [InlineData(new[] { "::Kimi.Console", "Kimi", "::Kimi" }, true)]
    public void AdditionalDefaultsOpenConsoleOnlyWhenRequested(string[] additions, bool bareVisible)
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.Alias = additions;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Defaults.kimi", "writeLine(\"x\")"));
        Assert.Equal(bareVisible, c.Bind().IsComplete);
        Assert.Equal(additions, c.Project.ProjectFile.Alias);
    }

    [Fact]
    public void AliasesDoNotLeakAcrossDocumentsAndRegenerationRebindsTargets()
    {
        var c = Parse("alias A => G\nalias O => Kimi.Console\nO.writeLine(\"first\")");
        Assert.False(c.Binding.Bind(BindingMode.Provisional).IsComplete);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "group G\nConsole.writeLine(\"generated\")"));
        Assert.True(c.Bind().IsComplete, Describe(c));
        c.Kotonoha.AddSource(new SourceDocument("Other.kimi", "O.writeLine(\"error\")"));
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("alias A => Kimi.Console\ngroup A", true)]
    [InlineData("alias Kimi => Kimi.Console", true)]
    [InlineData("alias Kimi => Kimi", false)]
    [InlineData("alias A => A\ngroup A", false)]
    [InlineData("alias A => Kimi.Console\ngroup G\n    group A", false)]
    [InlineData("alias A => Missing\ngroup A", false)]
    [InlineData("alias A => Kimi\nalias A => Kimi.Console\ngroup A", false)]
    public void OnlyDistinctValidRootHidingWarns(string source, bool warns)
    {
        var c = Parse(source);
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(warns, c.Kimigayo.GetOrAddDiagnosticCollection("Aliases.kimi").GetArray().Any(x => x.Entry.Name == nameof(DiagnosticCode.HiddenNamedAlias_Kd)));
    }

    [Fact]
    public void OrdinaryCoreGroupAndExplicitStagesKeepTheirMeaning()
    {
        var c = Parse("alias Core\ngroup Core\n    public func writeLine(text: string) => ()\nwriteLine(\"user\")\n::Kimi.Console.writeLine(\"library\")");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var calls = All(c.Kotonoha.RootKoto).OfType<InvocationKoto>().ToArray();
        Assert.NotSame(c.Library.WriteLine, calls[0].BoundCall!.Target);
        Assert.Same(c.Library.WriteLine, calls[1].BoundCall!.Target);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MandatoryDefaultsShareTheAdditionalStage(bool explicitKimi)
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.Alias = ["Extras"];
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Aliases.kimi", (explicitKimi ? "alias Kimi\n" : string.Empty) + "group Extras\n    public group Console\nConsole.writeLine(\"x\")"));
        Assert.Equal(explicitKimi, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("alias A => Kimi.Console\ngroup A\nA.writeLine(\"x\")", false)]
    [InlineData("alias A => G\ngroup G\n    private func f() => ()\nA.f()", false)]
    [InlineData("alias A => G\ngroup G\n    public func f() => ()\nA.f()", true)]
    [InlineData("alias A => G\nalias Open\ngroup G\n    public func f() => ()\ngroup Open\n    public group A\n        public func f() => ()\nA.f()", false)]
    [InlineData("alias G => G\ngroup G\n    public func f() => ()\nG.f()", true)]
    [InlineData("#if false\nalias A => Missing\nalias A => Kimi.Console\nA.writeLine(\"x\")", true)]
    [InlineData("alias O => Kimi.Console\nstruct S\n    public func writeLine(self: ref/Self, text: string) => ()\nfunc call(O: ref/S) => O.writeLine(\"x\")", false)]
    public void LookupRetainsAccessStagesAndBothNamespaces(string source, bool valid)
    {
        var c = Parse(source);
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void FunctionValueBindingAndSourceReloadRetainTheOriginalDeclaration()
    {
        var c = Parse("alias O => ::Kimi.Console\nlet f: (string) -> () = O.writeLine\nf(\"x\")");
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.True(c.Bind().IsComplete, Describe(c));
            Assert.Contains(All(c.Kotonoha.RootKoto).OfType<MemberAccessKoto>(), x => ReferenceEquals(x.BoundSymbol, c.Library.WriteLine));
            Assert.Single(All(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.IsValueCall);
            c.Kotonoha.OnDeserialized(c);
        }
    }

    [Fact]
    public void WarmAliasResolutionAllocatesNothing()
    {
        var c = Parse("alias O => Kimi.Console\nalias O => ::Kimi.Console\nalias Kimi.Console\nO.writeLine(\"a\")\nwriteLine(\"b\")");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete, Describe(c));
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
    }

    [Fact]
    public void NativeOutputKeepsUtf8LabelsAndCleanup()
    {
        ScalarEmissionTest.EmitFixture("NamedAliasesOutput", "alias O => Kimi.Console\nalias ::Kimi.Console\nlet message = \"Hello 日本語\"\nO.writeLine(text: message)\nwriteLine(\"\")\n::Kimi.Console.writeLine(\"done\")", "Hello 日本語\n\ndone\n");
    }

    [Fact]
    public void EffectiveDefaultsAreNormalizedWithoutChangingSavedAdditions()
    {
        Assert.Equal(Compilation.EffectiveAliases([]), Compilation.EffectiveAliases(["Kimi", "::Kimi", "Kimi"]));
        var empty = Tinyhand.TinyhandSerializer.Serialize(new ProjectFile());
        var explicitKimi = Tinyhand.TinyhandSerializer.Serialize(new ProjectFile { Alias = ["Kimi"] });
        Assert.False(empty.AsSpan().SequenceEqual(explicitKimi));
        var saved = Tinyhand.TinyhandSerializer.Serialize(new ProjectFile { Alias = ["Kimi.Console"] });
        var restored = Tinyhand.TinyhandSerializer.Deserialize<ProjectFile>(saved)!;
        Assert.Equal(new[] { "Kimi.Console" }, restored.Alias);
        Assert.Equal(new[] { "Kimi", "Kimi.Console" }, Compilation.EffectiveAliases(restored.Alias));
    }

    [Fact]
    public void MigratedExamplesAndMilestonesRetainValidSyntax()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var count = 0;
        foreach (var folder in new[] { "examples", "milestones" })
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(root, folder), "*.kimi", SearchOption.AllDirectories))
            {
                if (Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Contains("bin"))
                {
                    continue;
                }

                var c = Compilation.CreateForTest();
                Assert.True(c.Prepare(WindowsProfile.Target));
                c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, File.ReadAllText(path));
                Assert.True(c.Kotonoha.DiagnosticCollection.GetArray().Length == 0, path + ": " + string.Join("; ", c.Kotonoha.DiagnosticCollection.GetArray().Select(x => x.Message)));
                count++;
            }
        }

        Assert.True(count >= 55);
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Aliases.kimi", source));
        return c;
    }

    private static IEnumerable<Koto> All(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in All(child))
            {
                yield return nested;
            }
        }
    }

    private static string Describe(Compilation c)
        => string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
