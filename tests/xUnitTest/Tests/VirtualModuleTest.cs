// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class VirtualModuleTest(ITestOutputHelper output)
{
    private const string Derived = "public struct Derived : Lib.Base\n    public init() : base() => ()\n    let bonus: i32 = 41\n    override func score(self: objref/Self, bias: i32) -> i32 => base.score(bias) + self.bonus\n    public func permitted(self: objref/Self) -> i32 => self.score()\n";

    [Theory]
    [InlineData("public", true)]
    [InlineData("protected", true)]
    [InlineData("protected internal", true)]
    [InlineData("internal", false)]
    [InlineData("private protected", false)]
    public void OverrideTargetsKeepTheOriginalAccessDomain(string access, bool valid)
    {
        var c = ModuleBindingTest.Create(Derived + "public func main() => ()", Library(access));
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.Equal(valid, c.Bind().IsComplete);
            var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsOverride);
            Assert.Equal(valid, c.Binding.TryGetVirtualOverride(implementation, out var target));
            if (valid)
            {
                Assert.Same(Original(c), target.Slot.Original);
                Assert.Equal("Base", target.Slot.DeclaringType.Symbol!.Name);
            }
            else
            {
                Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.MissingOverrideTarget_Kd && ReferenceEquals(x.Node, implementation));
            }

            foreach (var module in c.SourceModules)
            {
                module.OnDeserialized(c);
            }
        }
    }

    [Theory]
    [InlineData("public", "member", true)]
    [InlineData("public", "qualified", true)]
    [InlineData("public", "item", true)]
    [InlineData("public", "erased", true)]
    [InlineData("protected internal", "member", false)]
    [InlineData("protected internal", "qualified", false)]
    [InlineData("protected internal", "item", false)]
    [InlineData("protected internal", "erased", false)]
    [InlineData("protected", "member", false)]
    [InlineData("protected", "qualified", false)]
    [InlineData("protected", "item", false)]
    [InlineData("protected", "erased", false)]
    public void TheDerivedModuleDoesNotAcquireTheOriginalModulesAccess(string access, string form, bool valid)
    {
        var use = form switch
        {
            "member" => "_ = value.score()",
            "qualified" => "_ = Derived.score(value@objref/Lib.Base, 1)",
            "item" => "let action = Derived.score",
            _ => "let action: (objref/Lib.Base, i32) -> i32 = Derived.score",
        };
        var c = ModuleBindingTest.Create(Derived + "public func main()\n    let value = Derived.init()@obj\n    " + use, Library(access));
        Assert.Equal(valid, c.Bind().IsComplete);
        if (!valid)
        {
            c.Binding.ReportDiagnostics();
            Assert.Contains(TestDiagnostics.Of(c, "root.kimi"), x => x.Code == "InaccessibleBinding_Kd");
        }
    }

    [Theory]
    [InlineData("public", "obj")]
    [InlineData("protected internal", "obj")]
    [InlineData("public", "rc")]
    [InlineData("public", "arc")]
    public void ImportedSlotsUsePrivateImplementationBodiesAndSharedValueEntries(string access, string mode)
    {
        const string Api = "public group Api\n    public func call(value: objref/Base) -> i32 => value.score()\n    public func items(value: objref/Base) -> i32\n        let item = Base.score\n        let erased: (objref/Base, i32) -> i32 = Base.score\n        return item(value, 1) + erased(value, 1) + Api.invoke(item, value)\n    func invoke<F>(action: ref/F, value: objref/Base) -> i32\n        F is Callable<(objref/Base, i32) -> i32>\n        return action(value, 1)\n";
        var c = ModuleBindingTest.Create(Derived + $"public func main()\n    let value = Derived.init()@{mode}\n    require value.permitted() == 42 else => $abort(\"member\")\n    require Lib.Api.call(value@objref/Lib.Base) == 42 else => $abort(\"library\")\n    require Lib.Api.items(value@objref/Lib.Base) == 126 else => $abort(\"items\")", Library(access) + Api);
        var ir = Emit(c);
        var original = Original(c);
        var references = c.SourceModules.SelectMany(x => KotoTree.Walk(x.RootKoto)).OfType<MemberAccessKoto>().Where(x => x.Right is IdentifierNameKoto { IdentifierName: "score" } && x.Left is not BaseReferenceKoto).ToArray();
        Assert.NotEmpty(references);
        Assert.All(references, x => Assert.Same(original, x.BoundSymbol!.Declaration));
        ScalarEmissionTest.WriteFixture("VirtualModule" + access.Replace(" ", string.Empty, StringComparison.Ordinal) + mode, ir, string.Empty);
    }

    [Fact]
    public void ImportedConformanceKeepsThePublicSlotAsItsWitness()
    {
        var c = ModuleBindingTest.Create(Derived + "public func main()\n    let value = Derived.init()@obj\n    require Lib.Api.call(value@objref/Lib.Base) == 42 else => $abort(\"witness\")", ConformingLibrary());
        ScalarEmissionTest.WriteFixture("VirtualModuleWitness", Emit(c), string.Empty);
    }

    [Fact]
    public void UnimplementedInheritedConformanceDoesNotPublishCode()
    {
        // SPEC 8.4.4 requires this form; G83 records the missing inherited identity/witness composition.
        // This is a support-boundary regression, not a language rejection or a completed positive case.
        var c = ModuleBindingTest.Create(Derived + "public func main()\n    let value = Derived.init()@obj\n    _ = Lib.Api.call(value@objref)", ConformingLibrary());
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void AccessDiagnosticsKeepTheOriginalDeclarationAndIndependentErrors()
    {
        var source = Derived + "public func main()\n    let action = Derived.score\n    let bad: i32 = false";
        var rootPath = Path.GetFullPath("root.kimi");
        var libraryPath = Path.GetFullPath("library.kimi");
        var c = ModuleBindingTest.Create(string.Empty, string.Empty);
        c.SourceModules[0].AddSource(new SourceDocument(rootPath, source));
        c.SourceModules[1].AddSource(new SourceDocument(libraryPath, Library("protected internal")));
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(rootPath)!, c.SourceModules[0]);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(libraryPath)!, c.SourceModules[1]);
        var result = c.Diagnostics.Finalize();
        Assert.Equal(2, result.Diagnostics.Length);
        var error = Assert.Single(result.Diagnostics, x => x.Code == "InaccessibleBinding_Kd");
        Assert.Equal("Derived.score", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        var related = Assert.Single(error.Related!);
        Assert.Equal("declaration", related.Role);
        Assert.Equal(libraryPath, result.Sources[related.Source].Path);
        Assert.Contains("score", related.Label, StringComparison.Ordinal);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("inaccessible", console.Text, StringComparison.Ordinal);
        Assert.Contains("score", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var root = SourceIdentity.FromPath("root.kimi");
        var library = SourceIdentity.FromPath("library.kimi");
        foreach (var relatedInformation in new[] { false, true })
        {
            var diagnostics = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [root, library], root, relatedInformation)[root];
            Assert.Equal(2, diagnostics.Length);
            var sent = Assert.Single(diagnostics, x => x.Code == "InaccessibleBinding_Kd");
            Assert.Equal(6, sent.Range.Start.Line);
            Assert.Equal(17, sent.Range.Start.Character);
            if (relatedInformation)
            {
                var location = Assert.Single(sent.RelatedInformation!).Location;
                Assert.Equal(library.ToUri(), location.Uri);
                Assert.Equal(2, location.Range.Start.Line);
            }
            else
            {
                Assert.Null(sent.RelatedInformation);
                Assert.Contains("library.kimi", sent.Message, StringComparison.Ordinal);
            }

            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(diagnostics));
        }
    }

    [Fact]
    public void InaccessibleInheritedNamesDoNotReserveDerivedNames()
    {
        const string LibrarySource = "public open struct Ancestor\n    private func read(self: ref/Self) -> i32 => 1\npublic open struct Base : Ancestor\n";
        var source = "public struct Derived : Lib.Base\n    public func read(self: ref/Self) -> i32 => 3\npublic func main() => ()";
        var c = ModuleBindingTest.Create(source, LibrarySource);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedCrossModuleBindingAndGenerationReuseStorage()
    {
        var c = ModuleBindingTest.Create(Derived + "public func main()\n    let value = Derived.init()@obj\n    require value.permitted() == 42 else => $abort(\"module\")", Library("public"));
        Emit(c);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    private static string Library(string access)
        => "public open struct Base\n    public init() => ()\n    " + access + " virtual func score(self: objref/Self, bias: i32 = 1) -> i32 => bias\n";

    private static string ConformingLibrary()
        => "public contract Reader\n    Self is ObjectPayload\n    func score(self: objref/Self, bias: i32) -> i32\n" +
            Library("public").Replace("    public init()", "    Self is Reader\n    public init()", StringComparison.Ordinal) +
            "public group Api\n    public func call<T>(value: objref/T) -> i32\n        T is Reader\n        return value.score(1)\n";

    private static FunctionKoto Original(Compilation c)
        => KotoTree.Walk(c.SourceModules[1].RootKoto).OfType<FunctionKoto>().Single(x => x.IsVirtual);

    private static string Emit(Compilation c)
    {
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        return CompilationTestHelper.WriteIr(c);
    }
}
