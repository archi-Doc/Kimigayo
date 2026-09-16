// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class LibraryImportTargetBindingTest
{
    [Theory]
    [InlineData("contract C\n#LibraryImport(\"library\", \"symbol\")\nstruct S\n    Self is C")]
    [InlineData("#LibraryImport(\"library\", \"symbol\")\ncontract C\nstruct S\n    Self is C")]
    public void NonFunctionImportTargetCannotCertifyConformance(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!.IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!.IsVerified);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        var restoredType = restored.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var restoredContract = restored.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.False(restored.Binding.GetConformanceDefinition(restoredType.BoundType!, restoredContract.BoundSymbol!)!.IsVerified);
    }

    [Fact]
    public void ImportOnPropertyCannotCertifyStorage()
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    #LibraryImport(\"library\", \"symbol\")\n    var value: i32");
        Assert.False(c.Binding.Result.IsComplete);
        var property = Assert.IsType<PropertyKoto>(c.Kotonoha.RootKoto.NestedContainers.Single().Members.Single());
        Assert.False(property.BoundSymbol!.Property!.IsVerified);
    }

    [Fact]
    public void ImportOnEnumCannotPublishConstruction()
    {
        var c = MinimalEmissionTest.Analyze("#LibraryImport(\"library\", \"symbol\")\nenum E\n    Empty\ngroup Consumer\n    func make() -> E => .Empty");
        Assert.False(c.Binding.Result.IsComplete);
        var use = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single().ExpressionBody!;
        Assert.False(c.Binding.TryGetEnumConstruction(use, out _));
    }

    [Fact]
    public void ImportOnGroupInvalidatesNestedProperty()
    {
        var c = MinimalEmissionTest.Analyze("#LibraryImport(\"library\", \"symbol\")\ngroup G\n    let value: i32 = 1");
        Assert.False(c.Binding.Result.IsComplete);
        var property = Assert.IsType<PropertyKoto>(c.Kotonoha.RootKoto.NestedContainers.Single().Members.Single());
        Assert.False(property.BoundSymbol!.Property!.IsVerified);
    }

    [Theory]
    [InlineData("#Layout(\"C\")\n#LibraryImport(\"library\", \"symbol\")")]
    [InlineData("#LibraryImport(\"library\", \"symbol\")\n#Layout(\"C\")")]
    public void RemovingImportFromChainRestoresConformance(string markers)
    {
        var c = MinimalEmissionTest.Analyze("contract C\n" + markers + "\nstruct S\n    Self is C");
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        var definition = c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!;
        Assert.False(definition.IsVerified);
        var import = type.AttributeChain!;
        if (import.IdentifierKoto is not IdentifierNameKoto { IdentifierName: "LibraryImport" })
        {
            import = import.AttributeChain!;
        }

        Assert.True(type.RemoveAttribute(import));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(definition.IsVerified);
    }

    [Fact]
    public void ForeignFunctionSupportRemainsUnimplemented()
    {
        var c = MinimalEmissionTest.Analyze("group Native\n    #LibraryImport(\"library\", \"symbol\")\n    public unsafe func imported(value: i32) -> i32");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
    }

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
}
