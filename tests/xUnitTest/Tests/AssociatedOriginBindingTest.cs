// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AssociatedOriginBindingTest
{
    // A projection base S has one slot `s`; `Item` is `ref/i32 during s`. In the rows `#` spells the projection (`.` or `.(Src).`) and
    // `$` the qualifier of SO's projection (`.` or `.(Outer).`).
    private const string Source = "contract Src\n    associate Item\n    func read(self: ref/Self) -> Self.Item\n";
    private const string Base = Source + "struct S {s}\n    Self is Src\n    public let v: ref/i32 during s\n    public init(v: ref/i32 during s) => self.v = v\n    public func read(self: ref/Self) -> ref/i32 during s => self.v\n";
    private const string Pair = Base + "struct C {s}\n    public let r: ref/i32 during s\n    public init(r: ref/i32 during s) => self.r = r\n";
    private const string Generic = Source + "struct G<T> {s}\n    Self is Src\n    public let v: ref/T during s\n    public init(v: ref/T during s) => self.v = v\n    public func read(self: ref/Self) -> ref/T during s => self.v\n";
    private const string Nested = Base + "contract Outer\n    associate X\n    func x(self: ref/Self) -> Self.X\nstruct SO {s}\n    Self is Outer\n    public let v: ref/i32 during s\n    public init(v: ref/i32 during s) => self.v = v\n    public func x(self: ref/Self) -> S during s => S.init(self.v)\n";
    private const string Specified = Source + "struct S {s}\n    Self is Src\n    associate Item is ref/i32 during s\n    public let v: ref/i32 during s\n    public init(v: ref/i32 during s) => self.v = v\n    public func read(self: ref/Self) -> ref/i32 during s => self.v\n";
    private const string Unused = Source + "struct S {s}\n    Self is Src\n    public let v: ref/i32 during s\n    public init(v: ref/i32 during s) => self.v = v\n    public func read(self: ref/Self) -> i32 => 1\n";
    private const string Own = Source + "struct S {s}\n    Self is Src\n    public let v: ref/i32 during s\n";
    private const string OwnRest = "    public init(v: ref/i32 during s)\n        self.v = v\n        self.w = v\n    public func read(self: ref/Self) -> ref/i32 during s => self.v\n";
    private const string OwnPick = Own + "    public init(v: ref/i32 during s) => self.v = v\n    public func read(self: ref/Self) -> ref/i32 during s => self.v\n" +
        "    public func pick(self: ref/Self, x: S#Item) -> ref/i32 during s => self.v\n";

    public static TheoryData<string, bool> ValidBases => Spellings(
        Base + "func f(a: ref/i32) -> ref/i32 during a\n    let w: S#Item = a\n    return w\n",
        Pair + "func f(a: ref/i32) -> ref/i32 during a\n    let w: (S#Item, C{x}) = (a, C.init(a))\n        origin x.s == a\n    return w.1.r\n",
        Base + "func g(x: S#Item) -> S#Item => x\nfunc f(a: ref/i32) -> ref/i32 during a => g(a)\n",
        Base + "func g(x: ref/i32) -> S#Item => x\nfunc f(a: ref/i32) -> ref/i32 during a => g(a)\n",
        Base + "func f(a: ref/i32) -> i32\n    let h = func [] (x: S#Item) -> i32 => 0\n    return h(a)\n",
        Generic + "func f(a: ref/i32) -> ref/i32 during a\n    let w: G<i32>#Item = a\n    return w\n",
        Nested + "func f(a: ref/i32) -> ref/i32 during a\n    let w: SO$X#Item = a\n    return w\n",
        Specified + "func f(a: ref/i32) -> ref/i32 during a\n    let w: S#Item = a\n    return w\n",
        OwnPick + "func f(a: ref/i32) -> i32\n    let l = 5\n    let s = S.init(a)\n    let r = s.pick(l@ref)\n    return 0\n",
        Own + "    public let w: Self#Item\n" + OwnRest + "func f(a: ref/i32) -> ref/i32 during a\n    let s = S.init(a)\n    return s.w\n");

    public static TheoryData<string, bool, string, string> RejectedBases => Spellings(
        (Base + "struct K {k}\n    public let w: S#Item\n    public init(v: ref/i32 during k) => self.w = v\n", nameof(Kimi.DiagnosticCode.MissingOriginBinding_Kd), "S"),
        (Base + "enum E {k}\n    Has(S#Item)\n    Empty\n", nameof(Kimi.DiagnosticCode.MissingOriginBinding_Kd), "S"),
        (Own + "    public let w: S#Item\n" + OwnRest, nameof(Kimi.DiagnosticCode.MissingOriginBinding_Kd), "S"),
        (Unused + "struct K {k}\n    public let w: S#Item\n    public init() => self.w = 1\n", nameof(Kimi.DiagnosticCode.MissingOriginBinding_Kd), "S"),
        (Base + "func f(a: ref/i32) -> i32\n    var w: S#Item\n    w = a\n    return 0\n", nameof(Kimi.DiagnosticCode.MissingOriginBinding_Kd), "S"),
        (Base + "func f(a: ref/i32) -> ref/i32 during a\n    let l = 5\n    let w: S#Item = l@ref\n    return w\n", nameof(Kimi.DiagnosticCode.UnsatisfiedOriginRelation_Kd), "return w"),
        (Base + "func g(x: S#Item, y: ref/i32) -> ref/i32 during y => x\n", nameof(Kimi.DiagnosticCode.UnprovenOriginRelation_Kd), "x"));

    [Theory]
    [InlineData("contract C\n    associate Item(a) is i32\nstruct S\n    Self is C")]
    [InlineData("contract C\n    associate Item(a)\nstruct S\n    Self is C\n    associate C.Item(b) is i32")]
    public void ConstantFamilyNormalizesAtApplication(string declarations)
    {
        var c = MinimalEmissionTest.Analyze(declarations + "\nfunc f(x: ref/i32 during source) -> S.(C).Item(source) => 42");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        Assert.Same(BoundType.I32, function.ReturnType!.TypeOf());
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("S.Item")]
    [InlineData("S.Item(source, source)")]
    [InlineData("S.Item(missing)")]
    [InlineData("S.Item(_)")]
    [InlineData("S(source)")]
    [InlineData("S.C.Item(source)")]
    public void InvalidApplicationsDoNotBind(string result)
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Item(a) is i32\nstruct S\n    Self is C\nfunc f(x: ref/i32 during source) -> " + result + " => 42");
        Assert.False(c.Binding.Result.IsComplete);
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("contract C\n    associate Item(a, a)")]
    [InlineData("contract C\n    associate Item(a)\nstruct S\n    Self is C\n    associate C.Item is i32")]
    [InlineData("contract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item(a) is i32")]
    [InlineData("contract C\n    associate Item(a)\n    func f(self: ref/Self) -> Self.Item(a)")]
    [InlineData("contract C\n    associate Item(a) is i32\nstruct S\n    Self is C\n    associate C.Item(b, c) is i32")]
    public void InvalidBindersAndSpecificationsDoNotBind(string source)
        => Assert.False(MinimalEmissionTest.Analyze(source).Binding.Result.IsComplete);

    [Fact]
    public void EmitsConstantFamily()
    {
        const string source = "contract C\n    associate Item(a) is i32\nstruct S\n    Self is C\nfunc f(x: ref/i32 during source) -> S.(C).Item(source) => 42\nlet value = 1\nrequire f(value@ref) == 42 else => $abort(\"family\")\nConsole.writeLine(\"42\")";
        ScalarEmissionTest.EmitFixture("AssociatedOriginConstant", source, "42\n");
    }

    [Theory]
    [InlineData("Self.Item(a)")]
    [InlineData("Self.(C).Item(a)")]
    [InlineData("Item(a)")]
    public void MethodOriginsAreSeparateFromAssociatedBinders(string result)
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Item(a) is i32\n    func f(self: ref/Self during a) -> " + result + "\nstruct S\n    Self is C\n    public func f(self: ref/Self during b) -> i32 => 42");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var parameters = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<OriginApplicationKoto>().Select(x => x.ArgumentNodes.Single().OriginOf()).ToArray();
        Assert.NotNull(parameters[0]);
        Assert.NotSame(parameters[0], parameters[1]);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("(a and b)")]
    [InlineData("static")]
    public void GenericConstantFamilyAcceptsExistingOrigins(string argument)
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Item(step) is i32\nfunc f<T>(x: ref/T during a, y: ref/T during b) -> T.(C).Item(" + argument + ")\n    T is C\n    return 42");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void SerializedFamilyRetainsBindingAndWarmIdentity()
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Item(a) is i32\nstruct S\n    Self is C\nfunc f(x: ref/i32 during a) -> S.Item(a) => 42");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete, MinimalEmissionTest.Describe(restored, null));
        Assert.True(restored.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => restored.Bind()));
    }

    [Theory]
    [InlineData("contract C\n    associate Item(a)\n    func a(self: ref/Self)")]
    [InlineData("contract C<a>\n    associate Item(a)")]
    public void UnrelatedNamespacesDoNotHideOriginParameters(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void EnclosingOriginCannotBeHidden()
    {
        const string source = "struct Outer {a}\n    contract C\n        associate Item(a)";
        ParseTestHelper.ParseSuccess(source);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code.ToString() == "DuplicateBinding_Kd");
    }

    [Fact]
    public void EmitsGenericRequirementReturningAppliedFamily()
    {
        const string source = "contract C\n    associate Item(a) is i32\n    func get(self: ref/Self during a) -> Self.Item(a)\nstruct S\n    Self is C\n    public init() => ()\n    public func get(self: ref/Self during b) -> i32 => 42\nfunc read<T>(x: ref/T during a) -> T.(C).Item(a)\n    T is C\n    return x.get()\nlet value = S.init()\nrequire read(value@ref) == 42 else => $abort(\"generic family\")\nConsole.writeLine(\"generic family\")";
        ScalarEmissionTest.EmitFixture("AssociatedOriginGeneric", source, "generic family\n");
    }

    // SPEC 8.5: a universal family capability holds at every application, so a Copy of T.Item(b) is proven from it.
    [Fact]
    public void UniversalFamilyCapabilityHoldsAtEveryApplication()
    {
        const string source = "contract C\n    associate Item(step) is Copy\n    func get(self: ref/Self during step) -> Self.Item(step)\n" +
            "func twice<T>(x: ref/T during b) -> (T.(C).Item(b), T.(C).Item(b))\n    T is C\n    let v = x.get()\n    return (v, v)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n    origin a outlives b")]
    [InlineData("\n    T.Item(a) is i32")]
    public void UnknownFamilyDoesNotAssumeCovarianceOrUniversalIdentity(string condition)
    {
        var source = "contract C\n    associate Item(step)\nfunc f<T>(x: ref/i32 during a, y: ref/i32 during b, value: T.Item(a)) -> T.Item(b)\n    T is C" + condition + "\n    return value@move";
        ParseTestHelper.ParseSuccess(source);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
    }

    // SPEC 15.4.2, 8.5.1, 8.5.2: a projection base is a written Type occurrence, so the projection's position completes the base's
    // omitted slot before the projection is normalized: a local infers it, an input makes it universal, a result takes the defaults and
    // an anonymous function's input is a body variable. Both spellings are accepted on both pipelines, with no located limit.
    [Theory]
    [MemberData(nameof(ValidBases))]
    public void AProjectionBaseIsCompletedByTheProjectionsPosition(string source, bool qualified)
    {
        _ = qualified;
        Assert.Empty(ContextualCaptureTest.PublishedRecords(source));
    }

    // SPEC 15.4.2, 15.3.2: a Field, payload or local without an initializer must state the base's slot (`(S{o}).Item` with a clause, or
    // `(S during k).Item`), also inside S and when the projection does not use it; a projection whose base is completed keeps the other
    // rules' errors. Each is one Binding error, at the base or the rule's site.
    [Theory]
    [MemberData(nameof(RejectedBases))]
    public void AProjectionBaseIsRejectedWhereItsPositionNeedsExplicitInformation(string source, bool qualified, string code, string at)
    {
        _ = qualified;
        var c = MinimalEmissionTest.Analyze(source);
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal((code, at), (issue.Code.ToString(), issue.Node.ToString()!.Split('\n')[0]));
    }

    // SPEC 15.4.4: the local publishes the Origin its initializer infers for the base's slot, the input of f, never S's declared slot.
    [Fact]
    public void AProjectionLocalPublishesItsInferredOrigin()
    {
        var c = MinimalEmissionTest.Analyze(Base + "func f(a: ref/i32) -> i32\n    let w: S.Item = a\n    return 0\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var f = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        var local = KotoTree.Walk(f).OfType<VariableKoto>().Single();
        Assert.Same(f, local.NameKoto.TypeOf()!.Origin!.Binder);
    }

    private static TheoryData<string, bool> Spellings(params string[] sources)
    {
        var data = new TheoryData<string, bool>();
        foreach (var source in sources)
        {
            foreach (var qualified in new[] { false, true })
            {
                data.Add(Spell(source, qualified), qualified);
            }
        }

        return data;
    }

    private static TheoryData<string, bool, string, string> Spellings(params (string Source, string Code, string At)[] rows)
    {
        var data = new TheoryData<string, bool, string, string>();
        foreach (var row in rows)
        {
            foreach (var qualified in new[] { false, true })
            {
                data.Add(Spell(row.Source, qualified), qualified, row.Code, row.At);
            }
        }

        return data;
    }

    private static string Spell(string source, bool qualified)
        => source.Replace("#", qualified ? ".(Src)." : ".", StringComparison.Ordinal).Replace("$", qualified ? ".(Outer)." : ".", StringComparison.Ordinal) + "public func main() => ()\n";
}
