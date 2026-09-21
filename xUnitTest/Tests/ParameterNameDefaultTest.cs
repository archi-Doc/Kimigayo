// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class ParameterNameDefaultTest
{
    [Theory]
    [InlineData("! x: i32", "f(x: 3)", true, 0)]
    [InlineData("! x: i32", "f(3)", false, 0)]
    [InlineData("! x: i32", "f()", false, 0)]
    [InlineData("! x: i32 = 10", "f(x: 3)", true, 0)]
    [InlineData("! x: i32 = 10", "f(3)", false, 0)]
    [InlineData("! x: i32 = 10", "f()", true, 1)]
    [InlineData("x: i32", "f(x: 3)", true, 0)]
    [InlineData("x: i32", "f(3)", true, 0)]
    [InlineData("x: i32", "f()", false, 0)]
    [InlineData("x: i32 = 10", "f(x: 3)", true, 0)]
    [InlineData("x: i32 = 10", "f(3)", true, 0)]
    [InlineData("x: i32 = 10", "f()", true, 1)]
    public void NamesAndDefaultsAreIndependent(string parameter, string call, bool accepted, int defaults)
    {
        var c = MinimalEmissionTest.Analyze("func f(" + parameter + ") -> i32 => x\n" + call);
        Assert.Equal(accepted, c.Binding.Result.IsComplete);
        Assert.Equal(accepted, c.Emission.Validate(out _));
        if (accepted)
        {
            Assert.Equal(defaults, Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).BoundCall!.DefaultArguments.Length);
        }
    }

    [Theory]
    [InlineData("external => local: i32", "f(3)", true)]
    [InlineData("external => local: i32", "f(external: 3)", true)]
    [InlineData("external => local: i32", "f(local: 3)", false)]
    [InlineData("! external => local: i32 = 7", "f()", true)]
    [InlineData("! external => local: i32 = 7", "f(3)", false)]
    [InlineData("! external => local: i32 = 7", "f(external: 3)", true)]
    public void RenamingUsesOnlyTheExternalName(string parameter, string call, bool accepted)
    {
        var c = MinimalEmissionTest.Analyze("func f(" + parameter + ") -> i32 => local\n" + call);
        Assert.Equal(accepted, c.Binding.Result.IsComplete);
        Assert.Equal(accepted, c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("func f(external => local?: i32) => ()")]
    [InlineData("let f = func (! x: i32) => x")]
    [InlineData("struct S\n    func f(self?: ref/Self) => ()")]
    [InlineData("contract C\n    func f(x: i32 = 1)")]
    [InlineData("contract C\n    func f(! x: i32 = 1)")]
    [InlineData("specialize func f<i32>(! x: i32) => ()")]
    public void RejectsMarkersAndDefaultsInInvalidContexts(string source)
        => Assert.NotEmpty(ParseTestHelper.Parse(source).DiagnosticCollection.GetArray());

    [Theory]
    [InlineData("! a: i32 = 1, b: i32", "f(3)", false)]
    [InlineData("! a: i32 = 1, b: i32", "f(b: 3)", true)]
    [InlineData("a: i32 = 1 ! b: i32", "f(3)", false)]
    [InlineData("a: i32 = 1 ! b: i32", "f(b: 3)", true)]
    [InlineData("a: i32 = 1, b: i32 = 2", "f(3, a: 4)", false)]
    [InlineData("a: i32 = 1, b: i32 = 2", "f(b: 3, a: 4)", true)]
    [InlineData("a: i32 = 1, b: i32 = 2", "f(b: 3, 4)", false)]
    public void PositionsNeverSkipParameters(string parameters, string call, bool accepted)
    {
        var c = MinimalEmissionTest.Analyze("func f(" + parameters + ") -> i32 => a + b\n" + call);
        Assert.Equal(accepted, c.Binding.Result.IsComplete);
        Assert.Equal(accepted, c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("f(3)", true)]
    [InlineData("f(x: 3)", false)]
    [InlineData("f()", false)]
    public void FunctionValuesEraseNamesAndDefaults(string call, bool accepted)
    {
        var c = MinimalEmissionTest.Analyze("func source(! x: i32 = 10) -> i32 => x\nlet f: (i32) -> i32 = source\n" + call);
        Assert.Equal(accepted, c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("value.f(3)", true)]
    [InlineData("S.f(value, 3)", true)]
    [InlineData("S.f(self: value, x: 3)", true)]
    [InlineData("value.f(x: 3)", true)]
    [InlineData("value.f()", false)]
    public void ReceiverSupplyIsIndependentOfOrdinaryNames(string call, bool accepted)
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    public func f(self, x: i32) -> i32 => x\nfunc use(value: ref/S) -> i32 => " + call);
        Assert.Equal(accepted, c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData(", ", " ! ", "value.f(3)", true)]
    [InlineData(" ! ", ", ", "value.f(3)", false)]
    [InlineData(" ! ", ", ", "value.f(x: 3)", true)]
    public void ContractCallsUseRequirementNamePermissions(string requirement, string implementation, string call, bool accepted)
    {
        var source = "contract C\n    func f(self" + requirement + "x: i32) -> i32\n" +
            "struct S\n    Self is C\n    public func f(self" + implementation + "x: i32) -> i32 => x\n" +
            "func use<T>(value: ref/T) -> i32\n    T is C\n    return " + call;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(accepted, c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("f<i32>(3)", false)]
    [InlineData("f<i32>(x: 3)", true)]
    [InlineData("f<i32>()", true)]
    public void SpecializationsInheritNamesAndDefaults(string call, bool accepted)
    {
        var c = MinimalEmissionTest.Analyze("func f<T>(! x: i32 = 10) -> i32 => x\nspecialize func f<i32>(x: i32) -> i32 => x + 1\n" + call);
        Assert.Equal(accepted, c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("func f(! x: i32) => ()\nfunc f(x: i32) => ()")]
    [InlineData("func f(! x: i32) => ()\nfunc f(! x: i32 = 1) => ()")]
    [InlineData("func f<T>(! x: T = 1) => ()\nf()")]
    [InlineData("func f(! x: i32 = unknown) => ()\nf(x: 3)")]
    public void NamesDoNotDistinguishSignaturesOrSuppressDefaultChecking(string source)
        => Assert.False(MinimalEmissionTest.Analyze(source).Binding.Result.IsComplete);

    [Fact]
    public void DeclarationReplacementInvalidatesNameAndDefaultPlans()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32 = 1) => ()\nf(3)\nf()");
        Assert.True(c.Binding.Result.IsComplete);
        var original = Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        var donor = MinimalEmissionTest.Analyze("func f(! x: i32) => ()");
        var replacement = Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        var parent = original.Parent!;
        Assert.True(KotoHelper.Replace(parent, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.All(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => Assert.Null(x.BoundCall));
        Assert.True(KotoHelper.Replace(parent, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(new[] { 0, 1 }, Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(x => x.BoundCall!.DefaultArguments.Length));
    }

    [Fact]
    public void ReloadAndWarmBindingPreserveIndependentFlags()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32 ! y: i32 = x + 1) => ()\nf(3)");
        Assert.True(c.Binding.Result.IsComplete);
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        var function = Walk(tree.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        Assert.Equal(1, function.PositionalParameterCount);
        Assert.Null(function.Parameters[0].DefaultValue);
        Assert.Equal(1, function.NameBoundaryIndex);
        Assert.NotNull(function.Parameters[1].DefaultValue);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
    }

    [Theory]
    [InlineData("NamedDefault", "func f(! x: i32 = 10) -> i32 => x\nif f() == 10 and f(x: 3) == 3 => Console.writeLine(\"ok\")")]
    [InlineData("SuppliedSkipsDefault", "func f(! x: i32 = 2147483647 + 1) -> i32 => x\nif f(x: 3) == 3 => Console.writeLine(\"ok\")")]
    [InlineData("DefaultBeforeRequired", "func f(! x: i32 = 10, y: i32) -> i32 => x + y\nif f(y: 3) == 13 => Console.writeLine(\"ok\")")]
    [InlineData("DeclarationOrder", "func f(! x: i32 = 10, y: i32 = x + 1, z: i32 = y + 1) -> i32 => z\nif f() == 12 and f(y: 20, x: 3) == 21 => Console.writeLine(\"ok\")")]
    [InlineData("Constructor", "struct S\n    let x: i32\n    public init(! x: i32 = 10)\n        self.x = x\n    public func read(self) -> i32 => self.x\nlet a = S.init()\nlet b = S.init(x: 3)\nif a.read() == 10 and b.read() == 3 => Console.writeLine(\"ok\")")]
    [InlineData("Generic", "func f<T>(value: ref/T ! count: i32 = 10) -> i32 => count\nlet n = 3\nif f<i32>(n@ref) == 10 => Console.writeLine(\"ok\")")]
    [InlineData("Specialization", "func f<T>(value: ref/T ! count: i32 = 10) -> i32 => count\nspecialize func f<i32>(value: ref/i32, count: i32) -> i32 => count + 1\nlet n = 3\nif f<i32>(n@ref) == 11 => Console.writeLine(\"ok\")")]
    [InlineData("Forwarded", "func f<T>(value: ref/T ! count: i32 = 10) -> i32 => count\nfunc g<T>(value: ref/T) -> i32 => f<T>(value)\nlet n = 3\nif g<i32>(n@ref) == 10 => Console.writeLine(\"ok\")")]
    [InlineData("ForwardedSlots", "func f<T>(value: ref/T, x: i32 ! y: i32 = x + 1, z: i32 = y + 1) -> i32 => x + y + z\nfunc g<T>(value: ref/T) -> i32 => f<T>(value, 2, z: 20)\nlet n = 3\nif g<i32>(n@ref) == 25 => Console.writeLine(\"ok\")")]
    [InlineData("ForwardedConcrete", "func f(x: i32 ! y: i32 = x + 1) -> i32 => y\nfunc g<T>(value: ref/T) -> i32 => f(3)\nlet n = 3\nif g<i32>(n@ref) == 4 => Console.writeLine(\"ok\")")]
    [InlineData("ForwardedReceiver", "struct S\n    public func f(! x: i32, self, y: i32 = x + 1) -> i32 => y\nfunc g<T>(value: ref/T, s: ref/S) -> i32 => s.f(x: 3)\nlet n = 3\nlet s = S.init()\nif g<i32>(n@ref, s) == 4 => Console.writeLine(\"ok\")")]
    public void EmitsNativeFixtures(string name, string source)
        => ScalarEmissionTest.EmitFixture("ParameterName" + name, source, "ok\n", 0);

    [Fact]
    public void OmittedNamedDefaultAbortsAtItsDeclaration()
    {
        const string Source = "func f(! x: i32 = 2147483647 + 1) -> i32 => x\nf()";
        var column = Source.IndexOf("2147483647", StringComparison.Ordinal) + 1;
        ScalarEmissionTest.EmitFixture("ParameterNameOmittedAbort", Source, string.Empty, 1, $"Hello.kimi:1:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    [Fact]
    public void WarmOwnershipAndEmissionReuseDefaultStorage()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32 ! y: i32 = x + 1) -> i32 => y\nf(3)");
        void Verify()
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        for (var i = 0; i < 100; i++)
        {
            Verify();
        }

        Assert.Equal(0, AllocationMeasurement.Measure(Verify));
    }

    private static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }
}
