// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class SignatureProjectionCertificateBindingTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidProjectionWitnessCannotCertifySignature(bool property)
    {
        var c = MinimalEmissionTest.Analyze(Prefix("internal") + Consumer(property));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(Definition(c).IsVerified);
        if (property)
        {
            Assert.False(Structure(c).Members.OfType<PropertyKoto>().Single().BoundSymbol!.Property!.IsVerified);
        }

        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData(false, false, "internal", false)]
    [InlineData(true, false, "internal", false)]
    [InlineData(false, true, "internal", false)]
    [InlineData(true, true, "internal", false)]
    [InlineData(false, false, "public", true)]
    [InlineData(true, false, "public", true)]
    [InlineData(false, true, "public", true)]
    [InlineData(true, true, "public", true)]
    public void TransitiveWitnessesDoNotDependOnSourceOrder(bool property, bool reverse, string access, bool valid)
    {
        var middle = Consumer(property).Replace("contract C\n", "contract C\n    associate Item\n", StringComparison.Ordinal).Replace("Self is C\n", "Self is C\n    associate C.Item is i32\n", StringComparison.Ordinal);
        const string last = "contract D\n    func end(x: i32) -> i32\nstruct Last\n    Self is D\n    public func end(x: S.C.Item) -> i32 => x\n";
        var c = MinimalEmissionTest.Analyze(reverse ? last + middle + Prefix(access) : Prefix(access) + middle + last);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Definition(c).IsVerified);
        CheckLast(c);
        var restored = Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
        CheckLast(restored);
        void CheckLast(Compilation compilation)
        {
            var type = compilation.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Last");
            var contract = compilation.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "D");
            Assert.Equal(valid, compilation.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!.IsVerified);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepairingOriginalWitnessRestoresDependentSignature(bool property)
    {
        var source = Prefix("internal") + Consumer(property);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(Definition(c).IsVerified);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<FunctionKoto>().Single();
        var original = function.Parameters[1].Type;
        var donor = MinimalEmissionTest.Analyze(source.Replace("x: Local.Hidden.Item", "x: i32", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<FunctionKoto>().Single().Parameters[1].Type;
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.True(c.Bind().IsComplete);
        Assert.True(Definition(c).IsVerified);
        Assert.True(KotoHelper.Replace(function, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidSelfProjectionDoesNotLoseItsEvidence(bool property)
    {
        var source = Consumer(property).Replace("contract C\n", "contract C\n    associate Item\n", StringComparison.Ordinal).Replace("Self is C\n", "Self is C\n    associate C.Item is i32\n", StringComparison.Ordinal).Replace("Source.Origin.Item", "Self.C.Item", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Definition(c).IsVerified);
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void InvalidFunctionSignatureCannotRetainDirectCall()
    {
        var c = MinimalEmissionTest.Analyze(Prefix("internal") + "group G\n    func take(x: Source.Origin.Item) -> i32 => x\n    func call() -> i32 => take(1)");
        Assert.False(c.Binding.Result.IsComplete);
        var call = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
        Assert.Null(Assert.IsType<InvocationKoto>(call.ExpressionBody).BoundCall);
    }

    [Fact]
    public void WarmSignatureProofsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Prefix("public") + Consumer(true));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Signature projection verification failed.");
            }
        }));
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

    private static string Prefix(string access)
        => access + " contract Hidden\n    associate Item\npublic struct Local\n    Self is Hidden\n    associate Hidden.Item is i32\npublic contract Origin\n    associate Item\n    func f(self: ref/Self, x: i32) -> i32\npublic struct Source\n    Self is Origin\n    associate Origin.Item is i32\n    public func f(self: ref/Self, x: Local.Hidden.Item) -> i32 => x\n";

    private static string Consumer(bool property)
        => property
            ? "contract C\n    property value: i32 has get\nstruct S\n    Self is C\n    public let value: Source.Origin.Item\n"
            : "contract C\n    func g(self: ref/Self, x: i32) -> i32\nstruct S\n    Self is C\n    public func g(self: ref/Self, x: Source.Origin.Item) -> i32 => x\n";

    private static DeclarationContainerKoto Structure(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");

    private static BoundConformance Definition(Compilation c)
    {
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        return c.Binding.GetConformanceDefinition(Structure(c).BoundType!, contract.BoundSymbol!)!;
    }
}
