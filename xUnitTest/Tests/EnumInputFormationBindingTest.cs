// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class EnumInputFormationBindingTest
{
    [Theory]
    [InlineData("string", false)]
    [InlineData("i32", true)]
    public void NormalizedPayloadInputsControlConstructionCertificates(string argument, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\nenum E\n    A(Source<" + argument + ">.Origin.Item)\n    Self is C\ngroup G\n    func make() -> E => E.A(1)");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Definition(c).IsVerified);
        Assert.Equal(valid, c.Binding.TryGetEnumConstruction(Construction(c), out _));
        Assert.Equal(valid, c.Bind().IsComplete);
        var restored = Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
        Assert.Equal(valid, Definition(restored).IsVerified);
        Assert.Equal(valid, restored.Binding.TryGetEnumConstruction(Construction(restored), out _));
    }

    [Theory]
    [InlineData("Source<string>.Origin.Item", false)]
    [InlineData("(Source<string>.Origin.Item, i32)", false)]
    [InlineData("[2 of Source<string>.Origin.Item]", false)]
    [InlineData("Source<i32>.Origin.Item", true)]
    [InlineData("(Source<i32>.Origin.Item, i32)", true)]
    [InlineData("[2 of Source<i32>.Origin.Item]", true)]
    public void UnusedNestedPayloadInputsAreChecked(string payload, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\nenum E\n    A(" + payload + ")\n    Self is C");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Definition(c).IsVerified);
    }

    [Fact]
    public void DependentPayloadInputsUseDeclarationEvidence()
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\nenum E<U>\n    U is i32\n    A(Source<U>.Origin.Item)\n    Self is C");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Definition(c).IsVerified);
    }

    [Fact]
    public void WarmPayloadInputChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\nenum E\n    A(Source<i32>.Origin.Item)\n    Self is C\ngroup G\n    func make() -> E => E.A(1)");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Enum input formation failed.");
            }
        }));
    }

    private const string Prefix = "contract Origin\n    associate Item\nstruct Source<T>\n    T is i32\n    Self is Origin\n    associate Origin.Item is i32\n";

    private static Compilation Reload(Compilation c)
    {
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        return restored;
    }

    private static Koto Construction(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single().ExpressionBody!;

    private static BoundConformance Definition(Compilation c)
    {
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "E");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        return Assert.IsType<BoundConformance>(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!));
    }
}
