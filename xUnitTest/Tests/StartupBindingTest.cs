// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class StartupBindingTest
{
    [Theory]
    [InlineData("()")]
    [InlineData("let value: i32")]
    [InlineData("var value: string")]
    [InlineData("let text = \"Hello world\"\nwriteLine(text)")]
    [InlineData("writeLine(\"Hello world\")")]
    [InlineData("public func main() => ()")]
    [InlineData("public func main() -> ()\n    writeLine(\"Hello world\")")]
    [InlineData("#if true\nwriteLine(\"selected\")")]
    public void SelectsOriginalRuntimeBodyOrMain(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var result = c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(result.IsComplete, Describe(c));
        Assert.Same(c.Kotonoha.SourceDocuments[0], result.Source);
        if (source.StartsWith("public", StringComparison.Ordinal))
        {
            Assert.Equal(StartupKind.Explicit, result.Kind);
            Assert.Same(GetMain(c), result.Function);
            Assert.Empty(c.Binding.StartupItems);
        }
        else
        {
            Assert.Equal(StartupKind.Implicit, result.Kind);
            Assert.Same(c.Kotonoha.GeneratedFunction, result.Function);
            Assert.Same(c.Kotonoha.GeneratedFunction!.Body!.Items[0], c.Binding.StartupItems[0]);
        }

        Assert.False(c.Core.IsCompleteLibrary);
    }

    [Theory]
    [InlineData("")]
    [InlineData("func helper() => ()")]
    [InlineData("func main() => ()")]
    [InlineData("public func Main() => ()")]
    [InlineData("group G\n    public func main() => ()")]
    [InlineData("struct S\n    public func main() => ()")]
    [InlineData("func outer()\n    public func main() => ()")]
    [InlineData("#if false\nwriteLine(\"excluded\")")]
    public void DeclarationFragmentsBindWithoutBecomingApplications(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.MissingStartupBody_Kd);
        Assert.True(c.Binding.CheckStartup(OutputKind.Library).IsComplete, Describe(c));
        Assert.Equal(StartupKind.None, c.Binding.Startup.Kind);
        Assert.Empty(c.Binding.Issues);
    }

    [Theory]
    [InlineData("public func main(x: i32) => ()")]
    [InlineData("public func main(self: i32) => ()")]
    [InlineData("public func main<T>() => ()")]
    [InlineData("public func main origin a() => ()")]
    [InlineData("public func main() -> i32 => 1")]
    [InlineData("public unsafe func main() => ()")]
    public void ApplicationMainRestrictionsDoNotLeakIntoLibraryBinding(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.InvalidStartupMain_Kd);
        Assert.Null(c.Binding.Startup.Function);
        Assert.True(c.Binding.CheckStartup(OutputKind.Library).IsComplete, Describe(c));
        Assert.Empty(c.Binding.StartupIssues);
        Assert.True(c.Binding.CheckBound().IsComplete);
    }

    [Theory]
    [InlineData("()")]
    [InlineData("let x: i32")]
    [InlineData("writeLine(\"x\")")]
    public void RuntimeItemsAreRejectedInLibrariesAndMixedApplications(string runtime)
    {
        var c = Parse("public func main() => ()\n" + runtime);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.MixedStartupBodies_Kd);
        Assert.Empty(c.Binding.StartupItems);
        Assert.False(c.Binding.CheckStartup(OutputKind.Library).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.LibraryRuntimeBody_Kd);
    }

    [Fact]
    public void EveryPublicMainIsValidatedBeforeAnySelection()
    {
        var c = Parse("public func main() => ()", "public func main(x: i32) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.MultipleStartupMains_Kd);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.InvalidStartupMain_Kd);
        Assert.True(c.Binding.CheckStartup(OutputKind.Library).IsComplete);
    }

    [Fact]
    public void SameSignaturePublicMainsAreSharedRootDuplicates()
    {
        var c = Parse("public func main() => ()", "public func main() => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateBinding_Kd);
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.MultipleStartupMains_Kd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceOrderDoesNotChooseBetweenRuntimeBodies(bool reverse)
    {
        var sources = reverse ? new[] { "let x: i32", "()" } : new[] { "()", "let x: i32" };
        var c = Parse(sources);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.MultipleStartupSources_Kd);
        Assert.Null(c.Binding.Startup.Source);
    }

    [Fact]
    public void HelpersDoNotCountAsAnotherRuntimeDocument()
    {
        var c = Parse("func helper() => ()", "()\nlet x: i32", "struct S");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Same(c.Kotonoha.SourceDocuments[1], c.Binding.Startup.Source);
        Assert.Equal(2, c.Binding.StartupItems.Count);
    }

    [Fact]
    public void MainIsCallableAcrossSourcesAndKeepsItsDeclarationAlias()
    {
        var c = Parse(
            "alias A\ngroup A\n    public func helper() => ()\npublic func main() => helper()",
            "alias B\ngroup B\n    public func helper() -> i32 => 1\nfunc callMain() => main()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete, Describe(c));
        var main = GetMain(c);
        var bodyCall = Assert.IsType<InvocationKoto>(main.ExpressionBody);
        Assert.Equal("A", Assert.IsType<GroupKoto>(bodyCall.BoundCall!.Target.Declaration.Parent).Name);
        var call = All(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(x => x.Method.ToString() == "main");
        Assert.Same(main.BoundSymbol, call.BoundCall!.Target);
        Assert.Same(c.Kotonoha.SourceDocuments[0], main.CodeContext.SourceDocument);
    }

    [Fact]
    public void MainCannotCaptureRuntimeLocals()
    {
        var c = Parse("let text = \"x\"\npublic func main() => writeLine(text)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidCaptureBinding_Kd);
    }

    [Theory]
    [InlineData("writeLine(\"x\")")]
    [InlineData("Core.writeLine(\"x\")")]
    [InlineData("::Core.writeLine(text: \"x\")")]
    [InlineData("let Core = 1\nlet writeLine = 2\n::Core.writeLine(\"x\")")]
    public void WriteLineUsesTheCanonicalLanguageFunction(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Assert.Single(All(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).BoundCall!;
        Assert.Same(c.Core.WriteLine, call.Target);
        Assert.Equal(CompilerFunctionKind.WriteLine, call.Target.CompilerFunction);
        Assert.Null(call.Receiver);
        Assert.Equal("()", call.ReturnType.Name);
        Assert.Equal(new[] { 0 }, call.ArgumentToParameter.ToArray());
        var function = Assert.IsType<FunctionKoto>(call.Target.Declaration);
        Assert.Equal("string", Assert.Single(function.Parameters).Type.BoundType!.Name);
        Assert.Null(function.Body);
        Assert.Null(function.ExpressionBody);
    }

    [Theory]
    [InlineData("writeLine()")]
    [InlineData("writeLine(1)")]
    [InlineData("writeLine(\"a\", \"b\")")]
    [InlineData("writeLine(value: \"x\")")]
    [InlineData("let writeLine = 1\nwriteLine(\"x\")")]
    [InlineData("func writeLine(x: i32) => ()\nwriteLine(\"x\")")]
    public void OrdinaryCallFailuresDoNotFallBackToCore(string source)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Null(Assert.Single(All(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).BoundCall);
    }

    [Fact]
    public void UserWriteLineHasNoCompilerImplementationIdentity()
    {
        var c = Parse("func writeLine(text: string) => ()\nwriteLine(\"x\")");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var target = Assert.Single(All(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).BoundCall!.Target;
        Assert.NotSame(c.Core.WriteLine, target);
        Assert.Equal(CompilerFunctionKind.None, target.CompilerFunction);
    }

    [Fact]
    public void RebindingInvalidatesSelectionAndPreservesCoreAndCallIdentity()
    {
        var c = Parse("writeLine(\"x\")");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        var call = Assert.Single(All(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).BoundCall;
        var core = c.Core.WriteLine;
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "public func main() => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Binding.Startup.IsComplete);
        Assert.Empty(c.Binding.StartupItems);
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(c.Binding.StartupIssues, x => x.Code == DiagnosticCode.MixedStartupBodies_Kd);
        Assert.Same(core, c.Core.WriteLine);
        Assert.Same(call, Assert.Single(All(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).BoundCall);
    }

    [Fact]
    public void StartupRequiresFinalBinding()
    {
        var c = Parse("()");
        Assert.Throws<InvalidOperationException>(() => c.Binding.CheckStartup(OutputKind.Application));
        c.Binding.Bind(BindingMode.Provisional);
        Assert.Throws<InvalidOperationException>(() => c.Binding.CheckStartup(OutputKind.Application));
    }

    [Fact]
    public void OutputKindRoundTripsAndDefaultsToApplication()
    {
        Assert.Equal(OutputKind.Application, new ProjectFile().OutputKind);
        var restored = TinyhandSerializer.Deserialize<ProjectFile>(TinyhandSerializer.Serialize(new ProjectFile { OutputKind = OutputKind.Library }));
        Assert.Equal(OutputKind.Library, restored!.OutputKind);
        var settings = TinyhandSerializer.DeserializeFromUtf8<ProjectFile>("OutputKind = \"Library\""u8);
        Assert.Equal(OutputKind.Library, settings!.OutputKind);
        Assert.Throws<TinyhandException>(() => TinyhandSerializer.DeserializeFromUtf8<ProjectFile>("OutputKind = \"Unknown\""u8));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(32, false)]
    [InlineData(512, false)]
    [InlineData(1, true)]
    [InlineData(32, true)]
    [InlineData(512, true)]
    public void WarmStartupAndWriteLineBindingAllocateNothing(int calls, bool explicitMain)
    {
        var source = new System.Text.StringBuilder(explicitMain ? "public func main()\n" : string.Empty);
        for (var i = 0; i < calls; i++)
        {
            source.Append(explicitMain ? "    " : string.Empty).Append("writeLine(\"Hello world\")\n");
        }

        var c = Parse(source.ToString());
        for (var i = 0; i < 12; i++)
        {
            Assert.True(c.Bind().IsComplete, Describe(c));
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        }

        var allocated = AllocationMeasurement.Measure(
            () =>
            {
                c.Bind();
                c.Binding.CheckStartup(OutputKind.Application);
            },
            16);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("#LibraryImport(\"kernel32.dll\") public func main()")]
    [InlineData("specialize func main<i32>() => ()")]
    public void ImportedOrSpecializedMainCannotBecomeStartup(string source)
    {
        var c = Parse(source);
        c.Bind();
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Null(c.Binding.Startup.Function);
        Assert.True(GetMain(c).IsSpecialization || c.Binding.StartupIssues.Any(x => x.Code == DiagnosticCode.InvalidStartupMain_Kd));
    }

    [Fact]
    public void LocalHelpersRemainSourceLocal()
    {
        var c = Parse("public func helper() => ()", "public func main() => helper()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
    }

    [Fact]
    public void CoreAliasAndUserCoreGroupUseOrdinaryLookup()
    {
        var c = Parse("alias Core\ngroup Core\n    public func writeLine(text: string) => ()\nCore.writeLine(\"user\")\n::Core.writeLine(\"compiler\")\nwriteLine(\"alias\")");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var calls = All(c.Kotonoha.RootKoto).OfType<InvocationKoto>().ToArray();
        Assert.Equal(CompilerFunctionKind.None, calls[0].BoundCall!.Target.CompilerFunction);
        Assert.Same(c.Core.WriteLine, calls[1].BoundCall!.Target);
        Assert.Same(c.Core.WriteLine, calls[2].BoundCall!.Target);
    }

    [Fact]
    public void InvalidCoreDeclarationsFailAfterPreviouslySuccessfulBinding()
    {
        var c = Parse("writeLine(\"x\")");
        Assert.True(c.Bind().IsComplete, Describe(c));
        c.Core.Kotonoha.CreateCodeContext().Parse(c.Core.Kotonoha.RootKoto, "public func writeLine(text: i32) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidCoreIntrinsics_Kd);
        Assert.False(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
    }

    [Fact]
    public void ClearingAndReparsingSourceDropsStaleMainSelection()
    {
        var c = Parse("public func main() => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        c.Kotonoha.RootKoto.Clear();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(StartupKind.Implicit, c.Binding.Startup.Kind);
    }

    [Fact]
    public void StartupSelectionDoesNotPermitTopLevelReturn()
    {
        var c = Parse("return");
        c.Bind();
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.NotEmpty(flow.Issues);
    }

    [Theory]
    [InlineData("writeLine(\"Hello world\")")]
    [InlineData("::Core.writeLine(text: \"Hello world\")")]
    [InlineData("public func main()\n    let text = \"Hello world\"\n    ::Core.writeLine(text)")]
    [InlineData("#switch\n    #case true\n        writeLine(\"selected\")\n    #case _\n        absent()")]
    public void WriteLineSuppliesTheExistingControlFlowChecks(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Empty(c.Binding.Obligations);
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.True(flow.PendingBinding.Count == 0, string.Join("\n", flow.PendingBinding.Select(x => $"{x.Akind}: {x}, {x.BindingState}, {x.BoundType}")));
        Assert.Empty(flow.Issues);
    }

    [Theory]
    [InlineData("unsafe func risky() => ()\nrisky()", true)]
    [InlineData("unsafe func risky() => ()\nunsafe\n    risky()", false)]
    public void CommittedCallsRetainUnsafePermissionChecks(string source, bool error)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.PendingBinding);
        Assert.Equal(error, flow.Issues.Count != 0);
    }

    [Theory]
    [InlineData("x.f(1)")]
    [InlineData("S.f(1, x)")]
    public void FlowUsesTheCommittedReceiverAndArgumentNodes(string expression)
    {
        var c = Parse($"struct S\n    public func f(value: i32, self: ref/Self) -> i32 => value\nfunc use(x: ref/S) -> i32 => {expression}");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Assert.Single(All(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.PendingBinding);
        Assert.Empty(flow.Issues);
        if (call.BoundCall!.Receiver is { } receiver)
        {
            Assert.True(flow.Nodes.ContainsKey(receiver));
        }

        foreach (var argument in call.ArgumentNodes)
        {
            Assert.True(flow.Nodes.ContainsKey(argument));
        }

        Assert.False(flow.Nodes.ContainsKey(call.Method));
    }

    [Theory]
    [InlineData(OutputKind.Application, "", false)]
    [InlineData(OutputKind.Application, "writeLine(\"Hello world\")", true)]
    [InlineData(OutputKind.Application, "public func main() => ()", true)]
    [InlineData(OutputKind.Library, "", true)]
    [InlineData(OutputKind.Library, "public func main(x: i32) => ()", true)]
    [InlineData(OutputKind.Library, "let x: i32", false)]
    public async Task ProjectBuildChecksTheConfiguredStartupMode(OutputKind kind, string source, bool expected)
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.OutputKind = kind;
        c.Project.AddSource("startup.kimi", source);
        Assert.Equal(expected, await c.Project.Build());
    }

    private static FunctionKoto GetMain(Compilation c)
        => All(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "main");

    private static Compilation Parse(params string[] sources)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        for (var i = 0; i < sources.Length; i++)
        {
            c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, new SourceDocument($"source{i}.kimi", sources[i]));
        }

        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        return c;
    }

    private static string Describe(Compilation c)
        => string.Join(Environment.NewLine, c.Binding.Issues.Concat(c.Binding.StartupIssues).Select(x => $"{x.Code}: {x.Node}"));

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
}
