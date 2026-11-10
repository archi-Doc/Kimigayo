// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class TypeArityBindingTest
{
    private const string Contract = "contract C<T>\n    func get(self) -> i32\n";

    [Theory]
    [InlineData("struct Box\nstruct Box<T>")]
    [InlineData("struct Box<T>\nstruct Box")]
    [InlineData("struct Box<T, U>\nstruct Box\nstruct Box<T>")]
    [InlineData("struct Box<T>\nstruct Box\nstruct Box<T>\nstruct Box")]
    public void ExplicitAritiesSelectDistinctDeclarations(string declarations)
    {
        var c = Parse(declarations + "\nfunc plain(x: Box) => ()\nfunc generic(x: Box<i32>) => ()");
        Assert.Empty(TestDiagnostics.Of(c));
        Assert.True(c.Bind().IsComplete, Describe(c));
        var functions = c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().ToArray();
        var plain = functions.Single(x => x.Name == "plain").Parameters[0].Type.TypeOf()!;
        var generic = functions.Single(x => x.Name == "generic").Parameters[0].Type.TypeOf()!;
        Assert.NotSame(plain.Symbol, generic.Symbol);
        Assert.Empty(Assert.IsType<StructKoto>(plain.Symbol!.Declaration).GenericParameterNodes);
        Assert.Single(Assert.IsType<StructKoto>(generic.Symbol!.Declaration).GenericParameterNodes);
    }

    [Theory]
    [InlineData("G.Box", "G.Box<i32>")]
    [InlineData("::G.Box", "::G.Box<i32>")]
    [InlineData("Box", "Box<i32>")]
    public void QualifiedRootedAndImportedTypesSelectArity(string plain, string generic)
    {
        var c = Parse($"alias G\ngroup G\n    public struct Box\n    public struct Box<T>\nfunc f(x: {plain}, y: {generic}) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void ImportStageSelectsArityBeforeDeclaringAmbiguity()
    {
        var c = Parse("alias A\nalias B\ngroup A\n    public struct Box\ngroup B\n    public struct Box<T>\nfunc f(x: Box, y: Box<i32>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void NearerWrongArityBlocksOuterDeclaration()
    {
        var c = Parse("struct Box<T>\ngroup G\n    struct Box<T, U>\n    func f(x: Box<i32>) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTypeFormation_Kd);
    }

    [Theory]
    [InlineData("struct Box\nstruct Box<T, U>\nstruct Box<T>", "func f(x: Box<i32, u8>) => ()")]
    [InlineData("enum Box\n    A\nenum Box<T>\n    B(T)", "func f(x: Box, y: Box<i32>) => ()")]
    [InlineData("struct Box\n    public func value() -> i32 => 1\nstruct Box<T>\n    public func value() -> i64 => 2", "func f() -> i32 => Box.value()\nfunc g() -> i64 => Box<i32>.value()")]
    [InlineData("open struct Base\n    public func value() -> i64 => 1\nopen struct Base<T>\n    public func value() -> i32 => 2\nstruct Derived: Base<i32>", "func f() -> i32 => Derived.value()")]
    [InlineData("#Layout(\"C\")\nstruct Box\n    var a: i32\n#Layout(\"Kimigayo\")\nstruct Box<T>\n    var a: i32\nstruct Box<T>\n    var b: i32", "func f(x: Box, y: Box<i32>) => ()")]
    [InlineData("struct Box<T>\n#if false\n    struct Box<T>\n        var a: Missing\nstruct Box", "func f(x: Box, y: Box<i32>) => ()")]
    public void AritySelectionPreservesRelatedSemantics(string declarations, string use)
    {
        var c = Parse(declarations + "\n" + use);
        Assert.Empty(TestDiagnostics.Of(c));
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("group Box\nstruct Box<T>", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("struct Box<T>\ngroup Box", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("struct Box\nenum Box<T>\n    A", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("struct Box<T>\nstruct Box\nstruct Box<U>", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("enum Box<T>\n    A\nenum Box\n    B\nenum Box<T>\n    C", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("struct Box\nstruct Box<T>\nfunc f(x: Box<i32, u8>) => ()", DiagnosticCode.InvalidTypeFormation_Kd)]
    [InlineData("struct Box<T>\nstruct Box<T, U>\nfunc f(x: Box) => ()", DiagnosticCode.InvalidTypeFormation_Kd)]
    [InlineData("alias A\nalias B\ngroup A\n    public struct Box<T>\ngroup B\n    public struct Box<T>\nfunc f(x: Box<i32>) => ()", DiagnosticCode.AmbiguousBinding_Kd)]
    [InlineData("struct Box<T>\ngroup G\n    struct Box<T, U>\n    func f(x: Box<i32>) => ()", DiagnosticCode.InvalidTypeFormation_Kd)]
    public void InvalidAritiesAndDeclarationsRemainErrors(string source, DiagnosticCode expected)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == expected);
        var restored = CompilationTestHelper.Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Contains(restored.Binding.Issues, x => x.Code == expected);
    }

    // SPEC 9.7, 8.4, 23.1.2 (PLAN G513): explicit Type arguments resolve in the use-site context, so a head or arity that forms no
    // application, a Type's or a Contract reference's (a parent's and a clause's included), keeps its arguments' own failures; a
    // Contract without parameters takes no written arguments. The arity-only guard keeps one record; the valid twins bind.
    [Theory]
    [InlineData("struct T<U>\nfunc f(b: T<i32, Missing>) => ()", "InvalidTypeFormation_Kd T<i32, Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData("struct T<U, V>\nfunc f(b: T<Missing>) => ()", "InvalidTypeFormation_Kd T<Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData("group H\n    struct P\nstruct T<U>\nfunc f(b: T<i32, H.P>) => ()", "InaccessibleBinding_Kd H.P|InvalidTypeFormation_Kd T<i32, H.P>")]
    [InlineData("group H\nstruct T<U>\nfunc f(b: T<i32, H>) => ()", "InvalidTypeFormation_Kd T<i32, H>|UnresolvedBinding_Kd H")]
    [InlineData("struct T<U>\nfunc f(b: T<i32, Option<Missing>>) => ()", "InvalidTypeFormation_Kd T<i32, Option<Missing>>|UnresolvedBinding_Kd Missing")]
    [InlineData("enum E<T>\n    A(T)\nfunc f(e: E<i32, Missing>) => ()", "InvalidTypeFormation_Kd E<i32, Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData("struct T<U>\nfunc f<X>(b: T<X, Missing>) => ()", "InvalidTypeFormation_Kd T<X, Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData("struct T<U>\nfunc f(b: ::T<i32, Missing>) => ()", "InvalidTypeFormation_Kd T<i32, Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData("func f(b: Nope<Missing>) => ()", "UnresolvedBinding_Kd Missing|UnresolvedBinding_Kd Nope")]
    [InlineData("func f<X>(b: X<Missing>) => ()", "InvalidTypeFormation_Kd X<Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData(Contract + "func f(b: C<Missing>) => ()", "InvalidTypeFormation_Kd C<Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData(Contract + "func f<X>(x: X)\n    X is C<i32, Missing>\n    return", "InvalidConstraint_Kd func f<X>(x: X)\n    X is C<i32, Missing>\n    return|TypeMismatch_Kd C<i32, Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData("contract C<T, U>\n    func get(self) -> i32\nfunc f<X>(x: X)\n    X is C<Missing>\n    return", "InvalidConstraint_Kd func f<X>(x: X)\n    X is C<Missing>\n    return|TypeMismatch_Kd C<Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData("contract D\n    func get(self) -> i32\nfunc f<X>(x: X)\n    X is D<i32>\n    return", "InvalidConstraint_Kd func f<X>(x: X)\n    X is D<i32>\n    return|TypeMismatch_Kd D<i32>")]
    [InlineData("contract D\n    func get(self) -> i32\nfunc f<X>(x: X)\n    X is D<Missing>\n    return", "InvalidConstraint_Kd func f<X>(x: X)\n    X is D<Missing>\n    return|TypeMismatch_Kd D<Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData(Contract + "struct S\n    Self is C<i32, Missing>\n    public func get(self) -> i32 => 1", "InvalidConstraint_Kd Self is C<i32, Missing>|InvalidConstraint_Kd struct S|TypeMismatch_Kd C<i32, Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData(Contract + "contract D : C<i32, Missing>\n    func put(self) -> i32", "InvalidConstraint_Kd C<i32, Missing>|InvalidConstraint_Kd contract D|UnresolvedBinding_Kd Missing")]
    [InlineData("struct T<U>\nfunc f(b: T<i32, bool>) => ()", "InvalidTypeFormation_Kd T<i32, bool>")]
    [InlineData("struct T<U>\nfunc f(b: T<i32>) => ()", "")]
    [InlineData(Contract + "func f<X>(x: X)\n    X is C<i32>\n    return", "")]
    [InlineData("contract D\n    func get(self) -> i32\nfunc f<X>(x: X)\n    X is D\n    return", "")]
    [InlineData(Contract + "contract D : C<i32>\n    func put(self) -> i32", "")]
    public void AFailedHeadOrArityKeepsItsArgumentsOwnFailures(string source, string records)
    {
        var c = Parse(source);
        Assert.Equal(records.Length == 0, c.Bind().IsComplete);
        Assert.Equal(records, string.Join('|', c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}").Order(StringComparer.Ordinal)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceOrderReloadAndCanonicalWritingRetainEveryArity(bool reverse)
    {
        var c = Parse(string.Empty);
        var first = new SourceDocument("one.kimi", "struct Box\n    var a: i32\nstruct Box<T>\n    var a: i64");
        var second = new SourceDocument("two.kimi", "struct Box<T>\n    var b: u8\nstruct Box\n    var b: u16\nfunc f(x: Box, y: Box<i32>) => ()");
        c.Kotonoha.AddSource(reverse ? second : first);
        c.Kotonoha.AddSource(reverse ? first : second);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(2, c.Kotonoha.RootKoto.NestedContainers.Count);
        Assert.All(c.Kotonoha.RootKoto.NestedContainers, x => Assert.Equal(2, x.Members.Count));
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
        var builder = default(IndentedStringBuilder);
        try
        {
            c.Kotonoha.RootKoto.UnparseAll(ref builder);
            var written = Parse(builder.ToString());
            Assert.True(written.Bind().IsComplete, Describe(written));
            Assert.Equal(2, written.Kotonoha.RootKoto.NestedContainers.Count);
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void ReplacingAnArityRebuildsFragmentLookupWithoutLosingItsSibling()
    {
        var c = Parse("struct Box\nstruct Box<T>\nstruct Box<T, U>");
        Assert.True(c.Bind().IsComplete);
        var root = c.Kotonoha.RootKoto;
        var original = root.NestedContainers.Single(x => x.GenericParameterNodes.Count == 1);
        var replacement = Assert.Single(Parse("struct Box<T>\n    var a: i32").Kotonoha.RootKoto.NestedContainers);
        ParseTestHelper.Detach(replacement, new GroupKoto(replacement.CodeContext, default, default));
        Assert.True(KotoHelper.Replace(root, original, replacement));
        c.Kotonoha.AddSource(new SourceDocument("later.kimi", "struct Box<T>\n    var b: i32\nfunc f(x: Box, y: Box<i32>, z: Box<i32, u8>) => ()"));
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(3, root.NestedContainers.Count);
        Assert.Equal(2, replacement.Members.Count);
        var duplicate = Assert.Single(Parse("struct Box").Kotonoha.RootKoto.NestedContainers);
        ParseTestHelper.Detach(duplicate, new GroupKoto(duplicate.CodeContext, default, default));
        Assert.False(KotoHelper.Replace(root, replacement, duplicate));
        Assert.True(c.Bind().IsComplete);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmArityBindingAllocatesNothing()
    {
        var c = Parse("alias A\nalias B\ngroup A\n    public struct Box\ngroup B\n    public struct Box<T>\nfunc f(x: Box, y: Box<i32>) => ()");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Type arity Binding failed.");
            }
        }));
    }

    private static Compilation Parse(string source)
        => CompilationTestHelper.Parse(source, "arity.kimi");

    private static string Describe(Compilation c) => string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
