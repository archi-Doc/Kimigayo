// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConstraintProjectionCertificateBindingTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LateInvalidProjectionCannotSupplyDeclarationConstraint(int form)
    {
        var c = MinimalEmissionTest.Analyze(Prefix("internal") + Consumer(form));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        AssertCertificate(c, form, false);
        Assert.False(c.Bind().IsComplete);
        AssertCertificate(c, form, false);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        AssertCertificate(restored, form, false);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ValidProjectionPreservesDeclarationEvidence(int form)
    {
        var c = MinimalEmissionTest.Analyze(Prefix("public") + Consumer(form));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        AssertCertificate(c, form, true);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        AssertCertificate(restored, form, true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ForwardProjectionDependenciesCannotCertify(int form)
    {
        var c = MinimalEmissionTest.Analyze(Consumer(form) + "\n" + Prefix("internal"));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        AssertCertificate(c, form, false);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RepairingWitnessRestoresConstraintEvidence(int form)
    {
        var source = Prefix("internal") + Consumer(form);
        var c = MinimalEmissionTest.Analyze(source);
        AssertCertificate(c, form, false);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<FunctionKoto>().Single();
        var original = function.Parameters[1].Type;
        var donor = MinimalEmissionTest.Analyze(source.Replace("x?: Local.Hidden.Item", "x?: i32", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<FunctionKoto>().Single().Parameters[1].Type;
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.True(c.Bind().IsComplete);
        AssertCertificate(c, form, true);
        Assert.True(KotoHelper.Replace(function, replacement, original));
        Assert.False(c.Bind().IsComplete);
        AssertCertificate(c, form, false);
    }

    [Fact]
    public void DependentQualifiersUseTheDeclaringEnvironment()
    {
        var c = MinimalEmissionTest.Analyze("contract Origin\n    associate Item\nstruct Source<T>\n    T is Copy\n    Self is Origin\n    associate Origin.Item is i32\ncontract C\nstruct S<T, U>\n    T is Copy\n    U is Source<T>.Origin.Item\n    Self is C");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        AssertCertificate(c, 1, true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void WarmConstraintProjectionProofsAllocateNothing(int form)
    {
        var c = MinimalEmissionTest.Analyze(Prefix("public") + Consumer(form));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Constraint projection verification failed.");
            }
        }));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void ProvisionalAndFinalPassesPreserveCertificateValidity(int form, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Prefix(valid ? "public" : "internal") + Consumer(form));
        var provisional = c.Binding.Bind(BindingMode.Provisional);
        Assert.False(provisional.IsComplete);
        Assert.Equal(valid, provisional.InvalidCount == 0);
        AssertCertificate(c, form, valid);
        Assert.Equal(valid, c.Bind().IsComplete);
        AssertCertificate(c, form, valid);
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
        => access + " contract Hidden\n    associate Item\npublic struct Local\n    Self is Hidden\n    associate Hidden.Item is i32\npublic contract Origin\n    associate Item\n    func f(self: ref/Self, x?: i32) -> i32\npublic struct Source\n    Self is Origin\n    associate Origin.Item is i32\n    public func f(self: ref/Self, x?: Local.Hidden.Item) -> i32 => x\n";

    private static string Consumer(int form) => form switch
    {
        0 => "group G\n    func take<T>(value?: T)\n        T is Source.Origin.Item\n        ()",
        1 => "contract C\nstruct S<T>\n    T is Source.Origin.Item\n    Self is C",
        2 => "contract C\nenum S<T>\n    T is Source.Origin.Item\n    A\n    Self is C",
        _ => "contract C\n    associate Item\n    Self.Item is Source.Origin.Item\nstruct S\n    Self is C\n    associate C.Item is i32",
    };

    private static void AssertCertificate(Compilation c, int form, bool valid)
    {
        if (form == 0)
        {
            var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single();
            Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, function.BindingState);
            return;
        }

        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.Equal(valid, c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!.IsVerified);
    }
}
