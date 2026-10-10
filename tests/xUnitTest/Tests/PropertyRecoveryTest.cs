// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class PropertyRecoveryTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("struct", "", "get(self: ref/Self) -> i32 => storage")]
    [InlineData("struct", "", "set(self: uniq/Self, value: i32) -> () => storage = value")]
    [InlineData("struct", " = 7", "set(self: uniq/Self, value: i32) -> () => storage = value")]
    [InlineData("group", "", "get() -> i32 => storage")]
    [InlineData("group", "", "set(value: i32) -> () => storage = value")]
    [InlineData("group", " = 7", "set(value: i32) -> () => storage = value")]
    public void StorageUsesTheUnavailableHeaderWithoutReenteringTheBody(string owner, string initializer, string accessor)
    {
        var source = owner + " S\n    var item: Missing" + initializer + "\n        " + accessor + "\nlet wrong: i32 = true";
        var c = MinimalEmissionTest.Analyze(source);
        var property = Property(c);
        Assert.Null(property.Type);
        Assert.False(property.IsVerified);
        Assert.All(KotoTree.Walk(property.Declaration).OfType<IdentifierNameKoto>().Where(static x => x.IdentifierName == "storage"), static x => Assert.Null(x.TypeOf()));
        this.AssertRecords(c, ["UnresolvedBinding_Kd", "TypeMismatch_Kd"]);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        Assert.False(c.Bind().IsComplete);
        this.AssertRecords(c, ["UnresolvedBinding_Kd", "TypeMismatch_Kd"]);
    }

    [Theory]
    [InlineData("set(self: uniq/Self, value: Missing) -> () => storage = value")]
    [InlineData("get(self: ref/Self) -> Missing => storage")]
    [InlineData("set(self: Missing, value: i32) -> () => storage = value")]
    public void AnUnavailableAccessorTypeKeepsItsWrittenCause(string accessor)
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    var item: i32\n        " + accessor + "\nlet wrong: i32 = true");
        Assert.False(Property(c).IsVerified);
        this.AssertRecords(c, ["UnresolvedBinding_Kd", "TypeMismatch_Kd"]);
    }

    [Fact]
    public void AnUnavailableLocalAnnotationCannotAcquireTheInitializerType()
    {
        var c = MinimalEmissionTest.Analyze("let item: Missing = 7\nlet next = item\nlet wrong: i32 = true");
        var variable = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "item");
        Assert.Null(variable.TypeOf());
        this.AssertRecords(c, ["UnresolvedBinding_Kd", "TypeMismatch_Kd"]);
    }

    [Fact]
    public void AHeaderInferenceCycleRemainsDistinctFromRuntimeInitialization()
    {
        var inferred = MinimalEmissionTest.Analyze("group Values\n    var a = b\n    var b = a");
        Assert.False(inferred.Binding.Result.IsComplete);
        inferred.Binding.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(inferred), static x => x.Code == "CyclicBinding_Kd");
        var declared = MinimalEmissionTest.Analyze("group Values\n    var a: i32 = b\n    var b: i32 = a");
        Assert.True(declared.Binding.Result.IsComplete, MinimalEmissionTest.Describe(declared, null));
    }

    [Fact]
    public void CorrectingTheHeaderRestoresItsExistingAccessors()
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    var item: Missing\n        set(self: uniq/Self, value: i32) -> () => storage = value");
        var property = Property(c);
        var original = property.Declaration.TypeKoto!;
        var donor = CompilationTestHelper.ParseSuccess("struct S\n    var item: i32");
        var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<PropertyKoto>().Single().TypeKoto!;
        Assert.True(KotoHelper.Replace(property.Declaration, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(property.IsVerified);
        this.AssertRecords(c, []);
        Assert.True(KotoHelper.Replace(property.Declaration, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.False(property.IsVerified);
        this.AssertRecords(c, ["UnresolvedBinding_Kd"]);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Missing", false)]
    [InlineData("i32", true)]
    public void RebindingTheAccessorReusesItsDependencyStorage(string type, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    var item: " + type + "\n        set(self: uniq/Self, value: i32) -> () => storage = value");
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    [Fact]
    public void ResolvedHeadersExecuteThroughBothAccessors()
    {
        const string Source = "struct S\n    public var item: i32 = 1\n        get() -> i32 => storage\n        set(value: i32) -> () => storage = value\nvar value = S.init()\nvalue.item = 7\nConsole.writeLine(\"\\(value.item)\")";
        ScalarEmissionTest.EmitFixture("PropertyRecoveryValid", Source, "7\n");
    }

    [Fact]
    public void PublicOutputRetainsIndependentBodyErrors()
    {
        const string Source = "struct S\n    var item: Missing\n        set(self: uniq/Self, value: i32) -> ()\n            storage = value\n            let inside: i32 = true\nlet outside: i32 = true";
        var path = Path.GetFullPath("property-recovery.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(["UnresolvedBinding_Kd", "TypeMismatch_Kd", "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(Source.IndexOf("Missing", StringComparison.Ordinal), 7), result.Diagnostics[0].Span);
        Assert.Equal(new SourceSpan(Source.IndexOf("true", StringComparison.Ordinal), 4), result.Diagnostics[1].Span);
        Assert.Equal(new SourceSpan(Source.LastIndexOf("true", StringComparison.Ordinal), 4), result.Diagnostics[2].Span);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var records = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Select(static x => x.Code), records.Select(static x => x.Code));
            Assert.Equal(result.Diagnostics.Select(static x => x.Display!.Range!.Value), records.Select(static x => x.Range));
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(records));
        }
    }

    private static BoundProperty Property(Compilation c)
        => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<PropertyKoto>().Single().SymbolOf()!.Property!;

    private void AssertRecords(Compilation c, string[] codes)
    {
        c.Binding.ReportDiagnostics();
        var records = c.Diagnostics.Finalize(rejected: !c.Binding.Result.IsComplete).Diagnostics;
        output.WriteLine(string.Join("\n", records.Select(static x => x.Code + " " + x.Span + " " + x.Message)));
        Assert.Equal(codes, records.Select(static x => x.Code));
    }
}
