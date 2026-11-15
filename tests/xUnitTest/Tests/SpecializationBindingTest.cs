// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class SpecializationBindingTest
{
    private const string Ordinary = "func weight<T>(value: ref/T) -> i32 => 1\n";

    // SPEC 8.13: a length slot is supplied as an evaluated constant and enters the selection key (LengthKey(N)).
    private const string Lengths = "func pick<length N, T>(values: ref/[N of T]) -> i32 => 0\n";

    [Fact]
    public void LengthSlotsSelectTheSpecialization()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationLengthSlots",
            Lengths + "specialize func pick<3, i32>(values: ref/[3 of i32]) -> i32 => 1\nfunc forward<length N, T>(values: ref/[N of T]) -> i32 => pick<N, T>(values)\nlet a: [3 of i32] = [1, 2, 3]\nlet b: [2 of i32] = [1, 2]\nrequire pick<3, i32>(a@ref) == 1 and pick<2, i32>(b@ref) == 0 and forward<3, i32>(a@ref) == 1 and forward<2, i32>(b@ref) == 0 else => $abort(\"selection\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 8.13.1, 8.13.3: closed compound arguments (Tuple, fixed array) form distinct selection keys.
    [Fact]
    public void CompoundArgumentsSelectTheirSpecializations()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationCompound",
            Ordinary + "specialize func weight<(i32, bool)>(value: ref/(i32, bool)) -> i32 => 2\nspecialize func weight<[2 of i32]>(value: ref/[2 of i32]) -> i32 => 3\nlet pair = (1, true)\nlet array: [2 of i32] = [1, 2]\nlet n: i32 = 0\nrequire weight<(i32, bool)>(pair@ref) == 2 and weight<[2 of i32]>(array@ref) == 3 and weight<i32>(n@ref) == 1 else => $abort(\"weight\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 8.13.3: function references obey the same choice: an explicit Item, an Item selected by its expected signature and an Item
    // formed in a generic body run the specialization their key selects, and an Item whose key selects none runs the original.
    [Fact]
    public void FunctionItemsSelectTheSpecialization()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationFunctionItem",
            Ordinary + "specialize func weight<i32>(value: ref/i32) -> i32 => 2\nfunc relay<T>(value: ref/T) -> i32\n    let item = weight<T>\n    return item(value)\nlet n: i32 = 4\nlet flag = true\nlet f = weight<i32>\nlet g: (ref/i32) -> i32 = weight\nlet h = weight<bool>\nrequire f(n@ref) == 2 and g(n@ref) == 2 and relay<i32>(n@ref) == 2 and h(flag@ref) == 1 and relay<bool>(flag@ref) == 1 else => $abort(\"item\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 8.13.2: the original's generic Constraints are inherited; the closed arguments must satisfy them.
    private const string Constrained = "func twice<T>(value: T) -> T\n    T is Copy\n    return value\n";

    [Fact]
    public void ConstrainedOriginalsAcceptSatisfyingSpecializations()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationConstrained",
            Constrained + "specialize func twice<i32>(value: i32) -> i32 => value + value\nrequire twice<i32>(4) == 8 and twice<bool>(true) == true else => $abort(\"twice\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Fact]
    public void RejectsSpecializationsThatViolateInheritedConstraints()
    {
        var c = MinimalEmissionTest.Analyze("struct Token\n    drop => ()\n" + Constrained + "specialize func twice<Token>(value: Token) -> Token => value@move");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.UnsatisfiedConstraint_Kd);
    }

    // SPEC 8.13.1: an instance method's specialization restates the receiver at the original's position.
    private const string Receiver = "struct Scale\n    var unit: i32 = 1\n    public func weight<U>(self: ref/Self, other: ref/U) -> i32 => self.unit\n";

    [Fact]
    public void ReceiverSpecializationsSelectThroughMethodCalls()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationReceiver",
            Receiver + "    specialize func weight<i32>(self: ref/Self, other: ref/i32) -> i32 => self.unit + other\nfunc relay<U>(scale: ref/Scale, other: ref/U) -> i32 => scale.weight<U>(other)\nlet scale = Scale.init()\nlet n: i32 = 4\nlet flag = true\nrequire scale.weight<i32>(n@ref) == 5 and scale.weight<bool>(flag@ref) == 1 and relay<i32>(scale@ref, n@ref) == 5 else => $abort(\"weight\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Theory]
    [InlineData("    specialize func weight<i32>(self: uniq/Self, other: ref/i32) -> i32 => 2")]
    [InlineData("    specialize func weight<i32>(other: ref/i32) -> i32 => 2")]
    [InlineData("    specialize func weight<i32>(self: ref/Self, other: ref/i32) -> i64 => 2")]
    public void RejectsMismatchedReceiverContracts(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Receiver + specialization);
        Assert.False(c.Binding.Result.IsComplete);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
    }

    // SPEC 8.13.2: the header inherits the original's boundary and defaults; callers still use the original omission rules.
    private const string Defaults = "func find<T>(value: T, count: i32 = 1) -> i32 => count\n";

    [Fact]
    public void InheritsDefaultsWithoutRedeclaringThem()
    {
        var c = MinimalEmissionTest.Analyze(Defaults + "specialize func find<i32>(value: i32, count: i32) -> i32 => count + 1\nrequire find<i32>(10) == 2 and find<i32>(10, count: 5) == 6 and find<bool>(true) == 1 else => $abort(\"defaults\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // SPEC 8.13.2: a redeclared default is the specialization check's IncompatibleContractImplementation_Kd. The parser owns a
    // redeclared modifier or `!` boundary (syntax corpus `specialization-access-modifier`, `specialization-argument-boundary`).
    [Fact]
    public void RejectsRedeclaredDefaults()
    {
        var c = MinimalEmissionTest.Analyze(Defaults + "specialize func find<i32>(value: i32, count: i32 = 1) -> i32 => count");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    // SPEC 8.13.2: written binder names are inherited from the original; the body serves every admitted binding.
    private const string Named = "func first<T>(values: ref/[3 of T] during source) -> ref/T during source => values[0]@ref/T\n";

    [Fact]
    public void InheritedBindersKeepTheOriginalContract()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationInheritedBinders",
            Named + "specialize func first<i32>(values: ref/[3 of i32] during source) -> ref/i32 during source => values[2]@ref/i32\nfunc forward<T>(values: ref/[3 of T] during source) -> ref/T during source => first<T>(values)\nlet a: [3 of i32] = [1, 2, 3]\nlet b: [3 of bool] = [true, false, false]\ndo\n    let local: [3 of i32] = [7, 8, 9]\n    require first<i32>(local@ref) == 9 else => $abort(\"local\")\nrequire first<i32>(a@ref) == 3 and forward<i32>(a@ref) == 3 and first<bool>(b@ref) == true else => $abort(\"first\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Fact]
    public void OmittedBinderNamesAreInheritedThroughTheirInputs()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationOmittedBinders",
            Named + "specialize func first<i32>(values: ref/[3 of i32]) -> ref/i32 => values[2]@ref/i32\nlet a: [3 of i32] = [1, 2, 3]\ndo\n    let local: [3 of i32] = [7, 8, 9]\n    require first<i32>(local@ref) == 9 else => $abort(\"local\")\nrequire first<i32>(a@ref) == 3 else => $abort(\"first\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Theory]
    [InlineData("specialize func first<i32>(values: ref/[3 of i32] during other) -> ref/i32 during other => values[2]@ref/i32")]
    [InlineData("specialize func first<i32>(values: ref/[3 of i32] during source) -> ref/i32 during static => values[2]@ref/i32")]
    [InlineData("specialize func first<i32>(values: ref/[3 of i32] during source) -> ref/i32 during source => values[2]@ref/i32\nspecialize func first<i32>(values: ref/[3 of i32] during source) -> ref/i32 during source => values[1]@ref/i32")]
    public void RejectsRenamedNarrowedOrDuplicateBinders(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Named + specialization);
        Assert.False(c.Binding.Result.IsComplete);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("specialize func pick<2, i32>(values: ref/[3 of i32]) -> i32 => 1")]
    [InlineData("specialize func pick<i32, 3>(values: ref/[3 of i32]) -> i32 => 1")]
    [InlineData("specialize func pick<N, i32>(values: ref/[3 of i32]) -> i32 => 1")]
    [InlineData("specialize func pick<3, i32>(values: ref/[3 of i32]) -> i32 => 1\nspecialize func pick<3, i32>(values: ref/[3 of i32]) -> i32 => 2")]
    public void RejectsInvalidLengthSlots(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Lengths + specialization);
        Assert.False(c.Binding.Result.IsComplete);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i32 => 2")]
    [InlineData("specialize func weight<i64>(value: ref/i64) -> i32 => 3")]
    [InlineData("specialize func weight<i32>(value => renamed: ref/i32) -> i32 => 2")]
    public void AcceptsClosedImplementation(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + specialization + "\nfunc forward<T>(value: ref/T) -> i32 => weight<T>(value)\nlet value: i32 = 4\nlet result = forward<i32>(value@ref/i32)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("specialize func missing<i32>(value: ref/i32) -> i32 => 2")]
    [InlineData("specialize func weight<i32>(value: ref/i64) -> i32 => 2")]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i64 => 2")]
    [InlineData("specialize func weight<i32>(other: ref/i32) -> i32 => 2")]
    [InlineData("specialize func weight<i32, i64>(value: ref/i32) -> i32 => 2")]
    [InlineData("specialize func weight<T>(value: ref/T) -> i32 => 2")]
    [InlineData("specialize func weight<i32>(value: ref/i32 during static) -> i32 => 2")]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i32 => true")]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i32 => 2\nspecialize func weight<i32>(value: ref/i32) -> i32 => 3")]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i32\n    i32 is Copy\n    return 2")]
    public void RejectsInvalidImplementation(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + specialization);
        Assert.False(c.Binding.Result.IsComplete);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
    }

    // SPEC 8.13.2, 15.4.2 (PLAN G863): a Type argument's omitted Origin slots are the original's, each call's, and the restated
    // inputs and result inherit them: a ref, Slice and uniq slot, one nested in a Type argument, Tuple, array or Slice element, a
    // related pair, one argument in several inputs or only in the result, a method original, a restated binder; Origins bound
    // inside a Function Type argument (and their meet) restrict nothing; an Origin-free argument meets `T is Owned`; outside a
    // header's Type arguments the limits below do not apply. Rows are named by G863's coverage table (row and program). The body
    // serves the call's slot (the Middle's C-331 rows, named by its labels): a result over the input (directly, through a call or a
    // generic call, nested, in a method), a write through a uniq slot, one T's two inputs, a static value or a subslice in the result,
    // a Move; a raw layer states no Origin (PLAN G888: outer, in an enum or struct instance, a Tuple, an array, a raw layer or a
    // Function Type, over an aggregate whose slot is the call's, in a method original, in a Slice, through a projection, as a pair's
    // argument, under `T is Owned` (Z7: an Origin-free raw layer is Owned); a call writes any Origin). The Middle's rows (G888 Tn,
    // C-340): the valid twins of the rejected forms (a parameter beside it, nested, a Function Type input, the original's binder, the
    // `?` spelling, `T is Owned`, a raw layer's Type argument); a written Origin argument the bound Type does not use (T52); a raw
    // layer's Origin that is the call's, reached through a projection subject's omitted slot (T56: the rule's boundary).
    private const string Box = "struct Box {source}\n    public let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\nstruct W {source}\n    public let data: uniq/i32 during source\n    public init(data: uniq/i32 during source) => self.data = data\nstruct Wrap<T>\n    public let inner: T\n    public init(inner: T) => self.inner = inner@move\nfunc f<T>(x: ref/T) -> i32 => 0\n";

    [Theory]
    [InlineData("C1 v1", "specialize func f<Box>(x: ref/Box) -> i32 => 1")]
    [InlineData("C2 v2", "func g<T>(x: T) -> i32 => 0\nspecialize func g<Slice<u8>>(x: Slice<u8>) -> i32 => 1")]
    [InlineData("C3 v3", "func w<T>(d: uniq/T) -> i32 => 0\nspecialize func w<W>(d: uniq/W) -> i32 => 1")]
    [InlineData("C4 v5", "specialize func f<Wrap<Box>>(x: ref/Wrap<Box>) -> i32 => 1")]
    [InlineData("C4 r01", "specialize func f<Option<Box>>(x: ref/Option<Box>) -> i32 => 1")]
    [InlineData("C5 r02", "specialize func f<(Box, Box)>(x: ref/(Box, Box)) -> i32 => 1")]
    [InlineData("C6 n01", "specialize func f<[2 of Box]>(x: ref/[2 of Box]) -> i32 => 1")]
    [InlineData("C7 r09", "specialize func f<Slice<Box>>(x: ref/Slice<Box>) -> i32 => 1")]
    [InlineData("C8 r03", "struct Related {first, second}\n    origin first outlives second\n    public let left: ref/i32 during first\n    public let right: ref/i32 during second\n    public init(left: ref/i32 during first, right: ref/i32 during second)\n        self.left = left\n        self.right = right\nspecialize func f<Related>(x: ref/Related) -> i32 => 1")]
    [InlineData("C9 r11", "func p<A, B>(x: ref/A, y: ref/B) -> i32 => 0\nspecialize func p<Box, i32>(x: ref/Box, y: ref/i32) -> i32 => 1")]
    [InlineData("C10 two", "func two<T>(x: ref/T, y: Option<T>) -> i32 => 0\nspecialize func two<Box>(x: ref/Box, y: Option<Box>) -> i32 => 1")]
    [InlineData("C10 v6", "func k<T>(x: Option<T>) -> i32 => 0\nspecialize func k<Box>(x: Option<Box>) -> i32 => 1\nfunc two<T>(x: ref/T, y: ref/T) -> i32 => 0\nspecialize func two<Box>(x: ref/Box, y: ref/Box) -> i32 => 1")]
    [InlineData("C10 r04b", "func pick<T>(x: Option<T>, y: Option<T>) -> Option<T> => x@move\nspecialize func pick<Box>(x: Option<Box>, y: Option<Box>) -> Option<Box> => y@move")]
    [InlineData("C11 v9", "func make<T>(x: i32) -> Option<T> => .None\nspecialize func make<Box>(x: i32) -> Option<Box> => .None")]
    [InlineData("C11 r10", "func mk<T>(n: i32) -> Option<T> => .None\nspecialize func mk<Option<W>>(n: i32) -> Option<Option<W>> => .None")]
    [InlineData("C12 v7", "struct Holder\n    public let n: i32\n    public init(n: i32) => self.n = n\n    public func m<T>(self: ref/Self, x: ref/T) -> i32 => 0\n    specialize func m<Box>(self: ref/Self, x: ref/Box) -> i32 => 1")]
    [InlineData("C14 n10", "func first<T>(values: ref/[3 of T] during source) -> i32 => 0\nspecialize func first<Box>(values: ref/[3 of Box] during source) -> i32 => 1")]
    [InlineData("C16 n04", "func size<T>() -> i32 => 0\nspecialize func size<(ref/i32) -> i32>() -> i32 => 1")]
    [InlineData("C16 n05", "func size<T>() -> i32 => 0\nspecialize func size<(ref/i32, ref/i32) -> ref/i32>() -> i32 => 1")]
    [InlineData("C16 r18", "func apply<T>(f: T) -> i32 => 0\nspecialize func apply<(i32) -> i32>(f: (i32) -> i32) -> i32 => 1")]
    [InlineData("C18 s25", "func o<T>(x: ref/T) -> i32\n    T is Owned\n    return 0\nspecialize func o<i32>(x: ref/i32) -> i32 => 1")]
    [InlineData("C21 s33", "func g(f: () -> ref/i32) -> i32 => 0")]
    [InlineData("C21 s34", "specialize func f<i32>(x: ref/i32) -> i32\n    var h: Option<() -> ref/i32> = .None\n    return 1")]
    [InlineData("C21 s35", "func apply<T>(f: Option<T>) -> i32 => 0\nlet n = apply<() -> ref/i32>(.None)")]
    [InlineData("a1 C-331", "func r<T>(x: ref/T) -> ref/i32 => r<T>(x)\nspecialize func r<Box>(x: ref/Box) -> ref/i32 => x.value")]
    [InlineData("a3 C-331", "func r<T>(x: ref/T) -> ref/i32 => r<T>(x)\nfunc pass(y: ref/i32) -> ref/i32 => y\nspecialize func r<Box>(x: ref/Box) -> ref/i32 => pass(x.value)")]
    [InlineData("a4 C-331", "func r<T>(x: ref/T) -> ref/i32 => r<T>(x)\nfunc idg<U>(y: U) -> U => y@move\nspecialize func r<Box>(x: ref/Box) -> ref/i32 => idg(x.value)")]
    [InlineData("e1 C-331", "func r<T>(x: ref/T) -> ref/i32 => r<T>(x)\nspecialize func r<Wrap<Box>>(x: ref/Wrap<Box>) -> ref/i32 => x.inner.value")]
    [InlineData("m1 C-331", "struct Holder\n    public let n: i32\n    public init(n: i32) => self.n = n\n    public func m<T>(self: ref/Self, x: ref/T) -> ref/i32 => self.m<T>(x)\n    specialize func m<Box>(self: ref/Self, x: ref/Box) -> ref/i32 => x.value")]
    [InlineData("d1 C-331", "func w<T>(d: uniq/T) -> i32 => 0\nspecialize func w<W>(d: uniq/W) -> i32\n    d.data@follow = 7\n    return d.data@follow")]
    [InlineData("b1 C-331", "func two<T>(x: T, y: T) -> T => x@move\nspecialize func two<Box>(x: Box, y: Box) -> Box\n    var t = x@move\n    t = y@move\n    return t@move")]
    [InlineData("c1 C-331", "func make<T>(x: i32) -> Option<T> => .None\nfunc sref() -> ref/i32 => sref()\nspecialize func make<Box>(x: i32) -> Option<Box> => .Some(Box.init(sref()))")]
    [InlineData("s1 C-331", "func g2<T>(x: T) -> T => x@move\nspecialize func g2<Slice<u8>>(x: Slice<u8>) -> Slice<u8> => x[1..]")]
    [InlineData("mv2 C-331", "func k2<T>(x: T) -> i32 => 0\nspecialize func k2<W>(x: W) -> i32\n    var a = x@move\n    a.data@follow = 3\n    return a.data@follow")]
    [InlineData("k1 C-331", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/i32>(x: raw/i32) -> i32 => 1")]
    [InlineData("G888 R5", "func h<T>(x: T) -> i32 => 0\nspecialize func h<Option<raw/i32>>(x: Option<raw/i32>) -> i32 => 1")]
    [InlineData("G888 R7", "func h<T>(x: T) -> i32 => 0\nspecialize func h<Wrap<raw/i32>>(x: Wrap<raw/i32>) -> i32 => 1")]
    [InlineData("G888 R9", "func h<T>(x: T) -> i32 => 0\nspecialize func h<(raw/i32, i32)>(x: (raw/i32, i32)) -> i32 => 1")]
    [InlineData("G888 R11", "func h<T>(x: T) -> i32 => 0\nspecialize func h<[2 of raw/i32]>(x: [2 of raw/i32]) -> i32 => 1")]
    [InlineData("G888 R13", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/(raw/i32)>(x: raw/(raw/i32)) -> i32 => 1")]
    [InlineData("G888 R14", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/Box>(x: raw/Box) -> i32 => 1")]
    [InlineData("G888 R20", "func apply<T>(f: T) -> i32 => 0\nspecialize func apply<() -> raw/i32>(f: () -> raw/i32) -> i32 => 1")]
    [InlineData("G888 R23", "struct Holder\n    public let n: i32\n    public init(n: i32) => self.n = n\n    public func m<T>(self: ref/Self, x: T) -> i32 => 0\n    specialize func m<raw/i32>(self: ref/Self, x: raw/i32) -> i32 => 1")]
    [InlineData("G888 R27", "func h<T>(x: T) -> i32 => 0\nfunc c(p: raw/i32 during static) -> i32 => h<raw/i32 during static>(p)")]
    [InlineData("G888 X2", "func h<T>(x: T) -> i32 => 0\nspecialize func h<Slice<raw/i32>>(x: Slice<raw/i32>) -> i32 => 1")]
    [InlineData("G888 X12", "contract Holds\n    associate Item\nstruct P\n    Self is Holds\n    associate Holds.Item is raw/i32\nfunc h<T>(x: T) -> i32 => 0\nspecialize func h<P.(Holds).Item>(x: P.(Holds).Item) -> i32 => 1")]
    [InlineData("G888 X14", "func p<s/T>(x: s/T) -> i32 => 0\nspecialize func p<raw/i32>(x: raw/i32) -> i32 => 1")]
    [InlineData("G888 Z7", "func o3<T>(x: ref/i32, z: T) -> i32\n    T is Owned\n    return 0\nspecialize func o3<raw/i32>(x: ref/i32, z: raw/i32) -> i32 => 1")]
    [InlineData("G888 T31", "func apply<T>(f: T) -> i32 => 0\nspecialize func apply<(raw/i32) -> i32>(f: (raw/i32) -> i32) -> i32 => 1")]
    [InlineData("G888 T32", "func first<T>(values: ref/[3 of T] during source) -> i32 => 0\nspecialize func first<raw/i32>(values: ref/[3 of raw/i32] during source) -> i32 => 1")]
    [InlineData("G888 T33", "func h3<T>(y: ref/i32, x: T) -> i32 => 0\nspecialize func h3<raw/i32>(y: ref/i32, x: raw/i32) -> i32 => 1")]
    [InlineData("G888 T34", "func h3<T>(y: ref/i32, x: T) -> i32 => 0\nspecialize func h3<Option<raw/i32>>(y: ref/i32, x: Option<raw/i32>) -> i32 => 1")]
    [InlineData("G888 T35", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/i32?>(x: raw/i32?) -> i32 => 1")]
    [InlineData("G888 T36", "func o<T>(x: T) -> i32\n    T is Owned\n    return 0\nspecialize func o<raw/i32>(x: raw/i32) -> i32 => 1")]
    [InlineData("G888 T37", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/Wrap<raw/i32>>(x: raw/Wrap<raw/i32>) -> i32 => 1")]
    [InlineData("G888 T52", "contract Holds2\n    associate Item(o)\nstruct P2\n    Self is Holds2\n    associate Holds2.Item(o) is raw/i32\nfunc h<T>(x: T) -> i32 => 0\nspecialize func h<P2.(Holds2).Item(static)>(x: P2.(Holds2).Item(static)) -> i32 => 1")]
    [InlineData("G888 T56", "contract Holds\n    associate Item\nstruct P3 {s}\n    Self is Holds\n    associate Holds.Item is raw/i32 during s\n    public let r: ref/i32 during s\n    public init(r: ref/i32 during s) => self.r = r\nfunc h5<T>(n: i32) -> i32 => 0\nspecialize func h5<P3.(Holds).Item>(n: i32) -> i32 => 1")]
    public void ArgumentOriginsAreInheritedFromTheOriginal(string row, string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Box + specialization + "\n()");
        Assert.True(c.Binding.Result.IsComplete, row + ": " + MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, row + ": " + MinimalEmissionTest.Describe(c));
    }

    // SPEC 8.13.3: the key excludes Origins, so the specialization runs for a local Box and W, at a direct call and after substitution.
    [Fact]
    public void InheritedArgumentOriginsSelectTheSpecialization()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationInheritedArgumentOrigins",
            Box + "specialize func f<Box>(x: ref/Box) -> i32 => 1\nfunc relay<T>(x: ref/T) -> i32 => f(x)\nfunc w<T>(d: uniq/T) -> i32 => 0\nspecialize func w<W>(d: uniq/W) -> i32 => 1\nfunc local() -> i32\n    let n = 5\n    let b = Box.init(n)\n    var m = 6\n    var u = W.init(m@uniq)\n    return f(b) + relay(b) * 10 + w(u@uniq) * 100\nrequire local() == 111 else => $abort(\"the generic body ran\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 8.13.3 (coverage C22 r16): an `Option<Box>` argument's specialization runs for a Box of a parameter's Origin, directly and
    // forwarded.
    [Fact]
    public void InheritedArgumentOriginsSelectAtAParameterOrigin()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationInheritedOptionArgumentOrigins",
            Box + "specialize func f<Option<Box>>(x: ref/Option<Box>) -> i32 => 1\nfunc fwd<T>(x: ref/T) -> i32 => f(x)\nfunc local(r: ref/i32) -> i32\n    let o: Option<Box> = .Some(Box.init(r))\n    return f(o@ref) + fwd(o@ref) * 10\nlet n: i32 = 3\nrequire local(n@ref) == 11 else => $abort(\"the generic body ran\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 8.13.1-8.13.3: the header writes no Origin of the inherited contract: a Type argument's slot written static, as a
    // parameter's Origin, as the original's or an added binder, inside a nested argument, a Tuple element or a pair's outer layer,
    // in a method original, and a restated input's slot written static restrict the bindings the original admits (the second of two
    // one-key specializations included); so do C-331's: `static` over a direct input, a parameter's Origin in a method original, a
    // fresh binder in a restated input's inner or outer layer, a parameter's Origin inside a Function Type argument. An
    // Origin-carrying argument still meets the target search, the inherited Constraints (q1i: q1n's Origin-free control) and the one
    // key. PLAN G888: so does a raw layer's written Origin (SPEC 5.7), `static` or a parameter's, outer, in an enum or struct instance,
    // a Tuple, an array, a raw layer, a Function Type's result or input, over an aggregate, in a method original, the original's binder,
    // the second of one key; a restated input's alone is C15's. The review's rows (G888 Xn): in a Slice, the `?` spelling, a raw
    // layer's Type argument, a nested Type's qualifier, one the argument's bound Type states through a projection (X11), a borrow's at
    // a Function Type input (X19: any variance); under an inherited Constraint that is proven (`T is Owned`, X6), undecided (X7, as its
    // Origin-free twin X24: the Constraint's record) or an identity (X21 fails it; X20 states the one Origin the original admits and
    // is rejected by G863's reading of a written Origin, as dev's borrow and slot twins are: a disputed label, the owner's question
    // O-318); a raw pair's argument fits by dropping its Origin, so the restated input's is C15's (X13). The implementation review's
    // rows: a slot's Origin projected from a parameter (`y.source`, Z5); a parameter's Origin on a raw layer under an undecided
    // `T is Owned` keeps the Constraint's record (Z2: the limit's gate reads no raw layer; Z7 is its valid twin). The Middle's rows:
    // an Origin argument the bound Type gives a raw layer (T51, T52's twin), `static` where no parameter states T (T59, T56's twin),
    // the same invalid header twice (T40: each header's own record, no duplicate). Each row lists its records' nodes in source order.
    [Theory]
    [InlineData("C1 i2", "specialize func f<Box during static>(x: ref/Box) -> i32 => 1", "specialize func f<Box during static>(x: ref/Box) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("C4 r13", "specialize func f<Option<Box during static>>(x: ref/Option<Box>) -> i32 => 1", "specialize func f<Option<Box during static>>(x: ref/Option<Box>) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("C5 s36", "specialize func f<(Box during static, i32)>(x: ref/(Box, i32)) -> i32 => 1", "specialize func f<(Box during static, i32)>(x: ref/(Box, i32)) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("C12 n03", "struct Holder\n    public let n: i32\n    public init(n: i32) => self.n = n\n    public func m<T>(self: ref/Self, x: ref/T) -> i32 => 0\n    specialize func m<Box during static>(self: ref/Self, x: ref/Box) -> i32 => 1", "specialize func m<Box during static>(self: ref/Self, x: ref/Box) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("C13 i3", "specialize func f<Box>(x: ref/i32) -> i32 => 1", "specialize func f<Box>(x: ref/i32) -> i32 => 1", Kimi.DiagnosticCode.SpecializationInputMismatch_Kd)]
    [InlineData("C14 r12", "specialize func f<Box during x>(x: ref/Box) -> i32 => 1", "specialize func f<Box during x>(x: ref/Box) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("C14 n09", "func first<T>(values: ref/[3 of T] during source) -> i32 => 0\nspecialize func first<Box during source>(values: ref/[3 of Box] during source) -> i32 => 1", "specialize func first<Box during source>(values: ref/[3 of Box] during source) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("C14 n02", "func p<s/T>(x: s/T) -> i32 => 0\nspecialize func p<ref/Box during static>(x: ref/Box) -> i32 => 1", "specialize func p<ref/Box during static>(x: ref/Box) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("C14 i1", "specialize func f<Box during source>(x: ref/(Box during source)) -> i32 => 1", "specialize func f<Box during source>(x: ref/(Box during source)) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("C15 i7", "specialize func f<Box>(x: ref/(Box during static)) -> i32 => 1", "specialize func f<Box>(x: ref/(Box during static)) -> i32 => 1", Kimi.DiagnosticCode.UnprovenOriginContract_Kd)]
    [InlineData("C16 s27", "func apply<T>(f: T) -> i32 => 0\nspecialize func apply<(ref/i32) -> ref/i32 during static>(f: (ref/i32) -> ref/i32 during static) -> i32 => 1", "specialize func apply<(ref/i32) -> ref/i32 during static>(f: (ref/i32) -> ref/i32 during static) -> i32 => 1", Kimi.DiagnosticCode.UnprovenOriginContract_Kd)]
    [InlineData("C18 n08", "func c<T>(x: ref/T) -> i32\n    T is Copy\n    return 0\nspecialize func c<W>(x: ref/W) -> i32 => 1", "specialize func c<W>(x: ref/W) -> i32 => 1", Kimi.DiagnosticCode.UnsatisfiedConstraint_Kd)]
    [InlineData("q1i C-331", "contract Marker\nfunc tr<T>(x: ref/T) -> i32\n    T is Marker\n    return 0\nspecialize func tr<i32>(x: ref/i32) -> i32 => 1", "specialize func tr<i32>(x: ref/i32) -> i32 => 1", Kimi.DiagnosticCode.UnsatisfiedConstraint_Kd)]
    [InlineData("a2s C-331", "func g<T>(x: T) -> ref/i32 => g(x@move)\nspecialize func g<Box during static>(x: Box) -> ref/i32 => x.value", "specialize func g<Box during static>(x: Box) -> ref/i32 => x.value", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("m2 C-331", "struct Holder\n    public let n: i32\n    public init(n: i32) => self.n = n\n    public func m<T>(self: ref/Self, x: ref/T) -> ref/i32 => self.m<T>(x)\n    specialize func m<Box during x>(self: ref/Self, x: ref/Box) -> ref/i32 => x.value", "specialize func m<Box during x>(self: ref/Self, x: ref/Box) -> ref/i32 => x.value", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("w1 C-331", "specialize func f<Box>(x: ref/(Box during q)) -> i32 => 1", "specialize func f<Box>(x: ref/(Box during q)) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("w2 C-331", "specialize func f<Box>(x: ref/Box during q) -> i32 => 1", "specialize func f<Box>(x: ref/Box during q) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("w6 C-331", "func apply2<T>(x: ref/i32, f: T) -> i32 => 0\nspecialize func apply2<() -> ref/i32 during x>(x: ref/i32, f: () -> ref/i32 during x) -> i32 => 1", "specialize func apply2<() -> ref/i32 during x>(x: ref/i32, f: () -> ref/i32 during x) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("C23 i4", "specialize func f<Box>(x: ref/Box) -> i32 => 1\nspecialize func f<Box during static>(x: ref/Box) -> i32 => 2", "specialize func f<Box during static>(x: ref/Box) -> i32 => 2", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("C23 i6", "specialize func f<Box>(x: ref/Box) -> i32 => 1\nspecialize func f<Box>(x: ref/Box) -> i32 => 2", "specialize func f<Box>(x: ref/Box) -> i32 => 1|specialize func f<Box>(x: ref/Box) -> i32 => 2", Kimi.DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("G888 R2 k2", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/i32 during static>(x: raw/i32 during static) -> i32 => 1", "specialize func h<raw/i32 during static>(x: raw/i32 during static) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 R3 k3", "func h3<T>(y: ref/i32, x: T) -> i32 => 0\nspecialize func h3<raw/i32 during y>(y: ref/i32, x: raw/i32 during y) -> i32 => 1", "specialize func h3<raw/i32 during y>(y: ref/i32, x: raw/i32 during y) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 R4 k4", "func h<T>(x: T) -> i32 => 0\nspecialize func h<Option<raw/i32 during static>>(x: Option<raw/i32 during static>) -> i32 => 1", "specialize func h<Option<raw/i32 during static>>(x: Option<raw/i32 during static>) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 R6", "func h<T>(x: T) -> i32 => 0\nspecialize func h<Wrap<raw/i32 during static>>(x: Wrap<raw/i32 during static>) -> i32 => 1", "specialize func h<Wrap<raw/i32 during static>>(x: Wrap<raw/i32 during static>) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 R8", "func h<T>(x: T) -> i32 => 0\nspecialize func h<(raw/i32 during static, i32)>(x: (raw/i32 during static, i32)) -> i32 => 1", "specialize func h<(raw/i32 during static, i32)>(x: (raw/i32 during static, i32)) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 R10", "func h<T>(x: T) -> i32 => 0\nspecialize func h<[2 of raw/i32 during static]>(x: [2 of raw/i32 during static]) -> i32 => 1", "specialize func h<[2 of raw/i32 during static]>(x: [2 of raw/i32 during static]) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 R12", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/(raw/i32 during static)>(x: raw/(raw/i32 during static)) -> i32 => 1", "specialize func h<raw/(raw/i32 during static)>(x: raw/(raw/i32 during static)) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 R15", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/Box during static>(x: raw/Box during static) -> i32 => 1", "specialize func h<raw/Box during static>(x: raw/Box during static) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 R16", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/(Box during static)>(x: raw/(Box during static)) -> i32 => 1", "specialize func h<raw/(Box during static)>(x: raw/(Box during static)) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 R17", "func h3<T>(y: ref/i32, x: T) -> i32 => 0\nspecialize func h3<Option<raw/i32 during y>>(y: ref/i32, x: Option<raw/i32 during y>) -> i32 => 1", "specialize func h3<Option<raw/i32 during y>>(y: ref/i32, x: Option<raw/i32 during y>) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 R19", "func apply<T>(f: T) -> i32 => 0\nspecialize func apply<() -> raw/i32 during static>(f: () -> raw/i32 during static) -> i32 => 1", "specialize func apply<() -> raw/i32 during static>(f: () -> raw/i32 during static) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 R21", "func apply<T>(f: T) -> i32 => 0\nspecialize func apply<(raw/i32 during static) -> i32>(f: (raw/i32 during static) -> i32) -> i32 => 1", "specialize func apply<(raw/i32 during static) -> i32>(f: (raw/i32 during static) -> i32) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 R22", "struct Holder\n    public let n: i32\n    public init(n: i32) => self.n = n\n    public func m<T>(self: ref/Self, x: T) -> i32 => 0\n    specialize func m<raw/i32 during static>(self: ref/Self, x: raw/i32 during static) -> i32 => 1", "specialize func m<raw/i32 during static>(self: ref/Self, x: raw/i32 during static) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 R24", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/i32>(x: raw/i32 during static) -> i32 => 1", "specialize func h<raw/i32>(x: raw/i32 during static) -> i32 => 1", Kimi.DiagnosticCode.UnprovenOriginContract_Kd)]
    [InlineData("G888 R25", "func first<T>(values: ref/[3 of T] during source) -> i32 => 0\nspecialize func first<raw/i32 during source>(values: ref/[3 of raw/i32 during source] during source) -> i32 => 1", "specialize func first<raw/i32 during source>(values: ref/[3 of raw/i32 during source] during source) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 R26", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/i32>(x: raw/i32) -> i32 => 1\nspecialize func h<raw/i32 during static>(x: raw/i32 during static) -> i32 => 2", "specialize func h<raw/i32 during static>(x: raw/i32 during static) -> i32 => 2", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 X1", "func h<T>(x: T) -> i32 => 0\nspecialize func h<Slice<raw/i32 during static>>(x: Slice<raw/i32 during static>) -> i32 => 1", "specialize func h<Slice<raw/i32 during static>>(x: Slice<raw/i32 during static>) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 X5", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/i32? during static>(x: raw/i32? during static) -> i32 => 1", "specialize func h<raw/i32? during static>(x: raw/i32? during static) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 X6", "func o<T>(x: T) -> i32\n    T is Owned\n    return 0\nspecialize func o<raw/i32 during static>(x: raw/i32 during static) -> i32 => 1", "specialize func o<raw/i32 during static>(x: raw/i32 during static) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 X7", "contract Marker\nfunc tr<T>(x: T) -> i32\n    T is Marker\n    return 0\nspecialize func tr<raw/i32 during static>(x: raw/i32 during static) -> i32 => 1", "specialize func tr<raw/i32 during static>(x: raw/i32 during static) -> i32 => 1", Kimi.DiagnosticCode.UnprovenConstraint_Kd)]
    [InlineData("G888 X24", "contract Marker\nfunc tr<T>(x: T) -> i32\n    T is Marker\n    return 0\nspecialize func tr<raw/i32>(x: raw/i32) -> i32 => 1", "specialize func tr<raw/i32>(x: raw/i32) -> i32 => 1", Kimi.DiagnosticCode.UnprovenConstraint_Kd)]
    [InlineData("G888 X11", "contract Holds\n    associate Item\nstruct P\n    Self is Holds\n    associate Holds.Item is raw/i32 during static\nfunc h<T>(x: T) -> i32 => 0\nspecialize func h<P.(Holds).Item>(x: P.(Holds).Item) -> i32 => 1", "specialize func h<P.(Holds).Item>(x: P.(Holds).Item) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 X13", "func p<s/T>(x: s/T) -> i32 => 0\nspecialize func p<raw/i32 during static>(x: raw/i32 during static) -> i32 => 1", "specialize func p<raw/i32 during static>(x: raw/i32 during static) -> i32 => 1", Kimi.DiagnosticCode.UnprovenOriginContract_Kd)]
    [InlineData("G888 X16", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/Wrap<raw/i32 during static>>(x: raw/Wrap<raw/i32 during static>) -> i32 => 1", "specialize func h<raw/Wrap<raw/i32 during static>>(x: raw/Wrap<raw/i32 during static>) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 X19", "func apply<T>(f: T) -> i32 => 0\nspecialize func apply<(ref/i32 during static) -> i32>(f: (ref/i32 during static) -> i32) -> i32 => 1", "specialize func apply<(ref/i32 during static) -> i32>(f: (ref/i32 during static) -> i32) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 X20", "func q<T>(x: T) -> i32\n    T is raw/i32 during static\n    return 0\nspecialize func q<raw/i32 during static>(x: raw/i32 during static) -> i32 => 1", "specialize func q<raw/i32 during static>(x: raw/i32 during static) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 X21", "func q<T>(x: T) -> i32\n    T is raw/i32 during static\n    return 0\nspecialize func q<raw/i32>(x: raw/i32) -> i32 => 1", "specialize func q<raw/i32>(x: raw/i32) -> i32 => 1", Kimi.DiagnosticCode.UnsatisfiedConstraint_Kd)]
    [InlineData("G888 X22b", "struct Outer<T>\n    public let t: i32\n    public init(t: i32) => self.t = t\n    public struct Inner\n        public let n: i32\n        public init(n: i32) => self.n = n\nfunc h<T>(x: T) -> i32 => 0\nspecialize func h<Outer<raw/i32 during static>.Inner>(x: Outer<raw/i32 during static>.Inner) -> i32 => 1", "specialize func h<Outer<raw/i32 during static>.Inner>(x: Outer<raw/i32 during static>.Inner) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 Z5", "func h4<T>(y: ref/Box, x: T) -> i32 => 0\nspecialize func h4<raw/i32 during y.source>(y: ref/Box, x: raw/i32 during y.source) -> i32 => 1", "specialize func h4<raw/i32 during y.source>(y: ref/Box, x: raw/i32 during y.source) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 Z2", "func o3<T>(x: ref/i32, z: T) -> i32\n    T is Owned\n    return 0\nspecialize func o3<raw/i32 during x>(x: ref/i32, z: raw/i32 during x) -> i32 => 1", "specialize func o3<raw/i32 during x>(x: ref/i32, z: raw/i32 during x) -> i32 => 1", Kimi.DiagnosticCode.UnprovenConstraint_Kd)]
    [InlineData("G888 T51", "contract Holds2\n    associate Item(o)\nstruct P2\n    Self is Holds2\n    associate Holds2.Item(o) is raw/i32 during o\nfunc h<T>(x: T) -> i32 => 0\nspecialize func h<P2.(Holds2).Item(static)>(x: P2.(Holds2).Item(static)) -> i32 => 1", "specialize func h<P2.(Holds2).Item(static)>(x: P2.(Holds2).Item(static)) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 T59", "func h5<T>(n: i32) -> i32 => 0\nspecialize func h5<raw/i32 during static>(n: i32) -> i32 => 1", "specialize func h5<raw/i32 during static>(n: i32) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("G888 T40", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/i32 during static>(x: raw/i32 during static) -> i32 => 1\nspecialize func h<raw/i32 during static>(x: raw/i32 during static) -> i32 => 2", "specialize func h<raw/i32 during static>(x: raw/i32 during static) -> i32 => 1|specialize func h<raw/i32 during static>(x: raw/i32 during static) -> i32 => 2", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    public void RejectsWrittenArgumentOrigins(string row, string specialization, string located, Kimi.DiagnosticCode code)
        => AssertRecords(row, specialization, located.Split('|').Select(x => (x, code)));

    // SPEC 8.13.2, 15.4.2 (PLAN G863): an omitted Origin of T's binding that no row completes yet is one located Unsupported_Kd per
    // occurrence, and the header rests on it: a borrow layer in a Type argument (outer, uniq, nested in a Type argument, Tuple,
    // borrow target or Option, a pair's outer layer) or nested in a restated input, a Function Type result that no input of its own
    // gives (a borrow, an aggregate, nested in an Option or a Function Type's input; C-331's h1, whose body would read it as
    // `static`), and an undecided inherited Constraint over the header's universal Origins (`T is Owned`; `T is Marker` that Box does
    // not meet, C-331's q1n, G863's remainder (b)). PLAN G888: a raw layer's written Origin under an omitted borrow layer rests on
    // that layer's limit (X3); an undecided Constraint over a header's written parameter Origins, a meet (X8) as its atom (X27), is
    // the limit too (G863's remainder (b): the header's own restriction is not reported beside it).
    [Theory]
    [InlineData("C19 v10", "func h<T>(x: T) -> i32 => 0\nspecialize func h<ref/i32>(x: ref/i32) -> i32 => 1", "ref/i32")]
    [InlineData("C19 v13", "func h<T>(x: T) -> i32 => 0\nspecialize func h<uniq/i32>(x: uniq/i32) -> i32 => 1", "uniq/i32")]
    [InlineData("C19 v12", "func h<T>(x: T) -> i32 => 0\nspecialize func h<Wrap<ref/i32>>(x: Wrap<ref/i32>) -> i32 => 1", "ref/i32|ref/i32")]
    [InlineData("C19 v14", "func h<T>(x: T) -> i32 => 0\nspecialize func h<(ref/i32, i32)>(x: (ref/i32, i32)) -> i32 => 1", "ref/i32|ref/i32")]
    [InlineData("C19 v15", "func h<T>(x: T) -> i32 => 0\nspecialize func h<ref/(ref/i32)>(x: ref/(ref/i32)) -> i32 => 1", "ref/i32|ref/i32")]
    [InlineData("C19 v16", "specialize func f<ref/i32>(x: ref/(ref/i32)) -> i32 => 1", "ref/i32|ref/i32")]
    [InlineData("C19 v17", "func g<T>(x: Option<ref/T during a>) -> i32 => 0\nspecialize func g<i32>(x: Option<ref/i32>) -> i32 => 1", "ref/i32")]
    [InlineData("C19 r14", "func h<T>(x: T) -> i32 => 0\nspecialize func h<Option<ref/i32>>(x: Option<ref/i32>) -> i32 => 1", "ref/i32|ref/i32")]
    [InlineData("C19 r15", "func p<s/T>(x: s/T) -> i32 => 0\nspecialize func p<ref/Box>(x: ref/Box) -> i32 => 1", "ref/Box")]
    [InlineData("C20 r08", "func apply<T>(f: ref/T) -> i32 => 0\nspecialize func apply<() -> ref/i32>(f: ref/(() -> ref/i32)) -> i32 => 1", "ref/i32")]
    [InlineData("C20 r20", "func apply<T>(f: T) -> i32 => 0\nspecialize func apply<() -> ref/i32>(f: () -> ref/i32) -> i32 => 1", "ref/i32")]
    [InlineData("C20 s23", "func apply<T>(f: T) -> i32 => 0\nspecialize func apply<() -> Box>(f: () -> Box) -> i32 => 1", "Box")]
    [InlineData("C20 s24", "func apply<T>(f: ref/T) -> i32 => 0\nspecialize func apply<Option<() -> ref/i32>>(f: ref/Option<() -> ref/i32>) -> i32 => 1", "ref/i32")]
    [InlineData("C20 s32", "func apply<T>(f: T) -> i32 => 0\nspecialize func apply<(() -> ref/i32) -> i32>(f: (() -> ref/i32) -> i32) -> i32 => 1", "ref/i32")]
    [InlineData("C18 r06", "func o<T>(x: ref/T) -> i32\n    T is Owned\n    return 0\nspecialize func o<Box>(x: ref/Box) -> i32 => 1", "specialize func o<Box>(x: ref/Box) -> i32 => 1")]
    [InlineData("h1 C-331", "func apply<T>(f: T) -> Option<ref/i32> => .None\nspecialize func apply<() -> ref/i32>(f: () -> ref/i32) -> Option<ref/i32> => .Some(f())", "ref/i32")]
    [InlineData("q1n C-331", "contract Marker\nfunc tr<T>(x: ref/T) -> i32\n    T is Marker\n    return 0\nspecialize func tr<Box>(x: ref/Box) -> i32 => 1", "specialize func tr<Box>(x: ref/Box) -> i32 => 1")]
    [InlineData("G888 X3", "func h<T>(x: T) -> i32 => 0\nspecialize func h<ref/(raw/i32 during static)>(x: ref/(raw/i32 during static)) -> i32 => 1", "ref/(raw/i32 during static)")]
    [InlineData("G888 X8", "func o2<T>(x: ref/i32, y: ref/i32, z: T) -> i32\n    T is Owned\n    return 0\nspecialize func o2<ref/i32 during (x and y)>(x: ref/i32, y: ref/i32, z: ref/i32 during (x and y)) -> i32 => 1", "specialize func o2<ref/i32 during (x and y)>(x: ref/i32, y: ref/i32, z: ref/i32 during (x and y)) -> i32 => 1")]
    [InlineData("G888 X27", "func o2<T>(x: ref/i32, y: ref/i32, z: T) -> i32\n    T is Owned\n    return 0\nspecialize func o2<ref/i32 during x>(x: ref/i32, y: ref/i32, z: ref/i32 during x) -> i32 => 1", "specialize func o2<ref/i32 during x>(x: ref/i32, y: ref/i32, z: ref/i32 during x) -> i32 => 1")]
    public void UncompletedArgumentOriginsAreALocatedLimit(string row, string specialization, string located)
        => AssertRecords(row, specialization, located.Split('|').Select(x => (x, Kimi.DiagnosticCode.Unsupported_Kd)));

    // SPEC 23.1.2 (PLAN G863): independent records of one header, each at its node, in source order: `static` cannot supply a uniq
    // slot beside the restriction (C3 i5); no target beside the limit of the restated nested layer (C19 i8); the Function Type
    // result's limit beside the restated result's pre-existing missing Origin (C20 v18, G863's remainder (c)); a local borrow as the
    // call's Origin in the body (C10 r05; Binding records it once, the CLI's report shows it twice as for its ordinary twin: G56's row).
    // SPEC 8.13.2, 15.6 (C-331): a body that reads the call's slot as `static` (directly, through a generic call, nested, a Slice's
    // element), as `self`'s, a parameter's or another argument's Origin gets its direct twin's record. SPEC 5.7 (G888 R18): `self` on a
    // raw layer of a Type argument binds nothing, in the argument and the restated input; the header's verdict is C-331 F5's pattern.
    // SPEC 5.7 (G888 T50): a written raw Origin is one atom, so a meet is invalid at each layer that writes it.
    [Theory]
    [InlineData("C3 i5", "func w<T>(d: uniq/T) -> i32 => 0\nspecialize func w<W during static>(d: uniq/W) -> i32 => 1", "IncompatibleContractImplementation_Kd: specialize func w<W during static>(d: uniq/W) -> i32 => 1|InvalidOriginBinding_Kd: W during static|InvalidOriginBinding_Kd: W")]
    [InlineData("C19 i8", "specialize func missing<i32>(x: Option<ref/i32>) -> i32 => 1", "MissingSpecializationTarget_Kd: specialize func missing<i32>(x: Option<ref/i32>) -> i32 => 1|Unsupported_Kd: ref/i32")]
    [InlineData("C20 v18", "func apply<T>(f: T) -> i32 => 0\nspecialize func apply<() -> uniq/i32>(f: () -> uniq/i32) -> i32 => 1", "Unsupported_Kd: uniq/i32|MissingOriginBinding_Kd: uniq/i32")]
    [InlineData("C10 r05", "func make<T>(x: i32) -> Option<T> => .None\nspecialize func make<Box>(x: i32) -> Option<Box>\n    let n: i32 = 1\n    return .Some(Box.init(n@ref))", "UnsatisfiedOriginRelation_Kd: Box.init(n@ref)")]
    [InlineData("a2 C-331", "func g<T>(x: T) -> ref/i32 => g(x@move)\nspecialize func g<Box>(x: Box) -> ref/i32 => x.value", "UnprovenOriginRelation_Kd: x.value")]
    [InlineData("a5 C-331", "func g<T>(x: T) -> ref/i32 => g(x@move)\nfunc idg<U>(y: U) -> U => y@move\nspecialize func g<Box>(x: Box) -> ref/i32 => idg(x.value)", "UnprovenOriginRelation_Kd: idg(x.value)")]
    [InlineData("e2 C-331", "func g<T>(x: T) -> ref/i32 => g(x@move)\nspecialize func g<Wrap<Box>>(x: Wrap<Box>) -> ref/i32 => x.inner.value", "UnprovenOriginRelation_Kd: x.inner.value")]
    [InlineData("s2 C-331", "func g3<T>(x: T) -> Option<ref/u8> => .None\nspecialize func g3<Slice<u8>>(x: Slice<u8>) -> Option<ref/u8> => x.tryGet(0)", "UnprovenOriginRelation_Kd: x.tryGet(0)")]
    [InlineData("m3 C-331", "struct Holder\n    public let n: i32\n    public init(n: i32) => self.n = n\n    public func m2<T>(self: ref/Self, x: T) -> ref/i32 => self.m2(x@move)\n    specialize func m2<Box>(self: ref/Self, x: Box) -> ref/i32 => x.value", "UnprovenOriginRelation_Kd: x.value")]
    [InlineData("c2 C-331", "func mk2<T>(x: ref/i32) -> Option<T> => .None\nspecialize func mk2<Box>(x: ref/i32) -> Option<Box> => .Some(Box.init(x))", "UnprovenOriginRelation_Kd: Box.init(x)")]
    [InlineData("b2 C-331", "func two2<A, B>(x: A, y: B) -> A => x@move\nspecialize func two2<Box, Box>(x: Box, y: Box) -> Box => y@move", "UnprovenOriginRelation_Kd: y@move")]
    [InlineData("G888 R18", "func h<T>(x: T) -> i32 => 0\nspecialize func h<raw/i32 during self>(x: raw/i32 during self) -> i32 => 1", "InvalidTypeFormation_Kd: specialize func h<raw/i32 during self>(x: raw/i32 during self) -> i32 => 1|MissingOriginBinding_Kd: self|MissingOriginBinding_Kd: self")]
    [InlineData("G888 T50", "func h4<T>(y: ref/i32, z: ref/i32, x: T) -> i32 => 0\nspecialize func h4<raw/i32 during (y and z)>(y: ref/i32, z: ref/i32, x: raw/i32 during (y and z)) -> i32 => 1", "InvalidOriginBinding_Kd: raw/i32 during (y and z)|InvalidOriginBinding_Kd: raw/i32 during (y and z)")]
    public void ReportsEveryIndependentHeaderRecord(string row, string specialization, string records)
        => AssertRecords(row, specialization, records.Split('|').Select(x => (x[(x.IndexOf(": ", StringComparison.Ordinal) + 2)..], Enum.Parse<Kimi.DiagnosticCode>(x[..x.IndexOf(": ", StringComparison.Ordinal)]))));

    // SPEC 15.6, 15.1.3 (C-331): A5 judges a body's Loans and Moves over the call's slot as its direct twin's: two live exclusive
    // reborrows through a uniq slot, a second Move of an argument.
    [Theory]
    [InlineData("d2 C-331", "func w<T>(d: uniq/T) -> i32 => 0\nspecialize func w<W>(d: uniq/W) -> i32\n    let a = d.data@follow@uniq\n    let b = d.data@follow@uniq\n    a@follow = 1\n    b@follow = 2\n    return 0", "ComparisonLoanConflict_Kd d.data@follow@uniq")]
    [InlineData("mv1 C-331", "func k2<T>(x: T) -> i32 => 0\nspecialize func k2<W>(x: W) -> i32\n    let a = x@move\n    let b = x@move\n    return 0", "MovedPlace_Kd x")]
    public void AnalysisJudgesTheSlotInTheBody(string row, string specialization, string problems)
    {
        var c = MinimalEmissionTest.Analyze(Box + specialization + "\n()");
        Assert.True(c.Binding.Result.IsComplete, row + ": " + MinimalEmissionTest.Describe(c));
        Assert.Equal(problems, MinimalEmissionTest.Problems(c));
    }

    // SPEC 8.13.3: specializations never enter the candidate set; an invalid one does not fail calls to the original.
    [Fact]
    public void InvalidSpecializationDoesNotCascadeIntoCalls()
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + "specialize func weight<i32>(other: ref/i32) -> i32 => 2\nfunc forward<T>(value: ref/T) -> i32 => weight<T>(value)\nlet value: i32 = 4\nlet result = weight<i32>(value@ref) + forward<i32>(value@ref)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.All(c.Binding.Issues, x => Assert.Equal(Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd, x.Code));
    }

    // SPEC 8.13.3: a call that no candidate fits counts the original alone; its specialization is no candidate.
    [Fact]
    public void ASpecializationIsNoCandidateOfAFailedCall()
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + "specialize func weight<i32>(value: ref/i32) -> i32 => 2\nlet n = weight(1, 2)");
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(nameof(Kimi.DiagnosticCode.NoApplicableOverload_Kd), error.Code);
        Assert.Equal("none of 1 candidates applies", error.Label);
    }

    // SPEC 8.13.1: diagnostics distinguish no target, an input-structure mismatch, multiple targets and a contract mismatch.
    [Theory]
    [InlineData("specialize func missing<i32>(value: ref/i32) -> i32 => 2", Kimi.DiagnosticCode.MissingSpecializationTarget_Kd)]
    [InlineData("specialize func weight<i32, i64>(value: ref/i32) -> i32 => 2", Kimi.DiagnosticCode.MissingSpecializationTarget_Kd)]
    [InlineData("specialize func weight<i32>(value: ref/i64) -> i32 => 2", Kimi.DiagnosticCode.SpecializationInputMismatch_Kd)]
    [InlineData("specialize func weight<i32>(value: ref/i32, other: i32) -> i32 => 2", Kimi.DiagnosticCode.SpecializationInputMismatch_Kd)]
    [InlineData("func weight<T>(other: ref/i32) -> i32 => 3\nspecialize func weight<i32>(value: ref/i32) -> i32 => 2", Kimi.DiagnosticCode.AmbiguousBinding_Kd)]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i64 => 2", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    public void TargetIdentificationDiagnosticsAreDistinct(string specialization, Kimi.DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + specialization);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }

    [Fact]
    public void StillChecksOrdinaryBody()
    {
        var c = MinimalEmissionTest.Analyze("func weight<T>(value: ref/T) -> i32 => true\nspecialize func weight<i32>(value: ref/i32) -> i32 => 2\nlet value: i32 = 4\nlet result = weight<i32>(value@ref)");
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void CannotUseResultsOrNamesToResolveTargetAmbiguity()
    {
        var c = MinimalEmissionTest.Analyze("func f<T>(value: T) -> i32 => 1\nfunc f<T>(other: i32) -> bool => true\nspecialize func f<i32>(value: i32) -> i32 => 2\n()");
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void RebindingRechecksTheSpecializationSet()
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + "specialize func weight<i32>(value: ref/i32) -> i32 => 2\n()");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Bind().IsComplete);
        c.Kotonoha.AddSource(new Kimi.Compiler.SourceDocument("duplicate.kimi", "specialize func weight<i32>(value: ref/i32) -> i32 => 3"));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.MissingSpecializationTarget_Kd);
    }

    [Fact]
    public void InheritedOriginRelationsAreAvailableInTheSpecializedBody()
        => ScalarEmissionTest.EmitFixture("SpecializationInheritedRelations", "func choose<T>(a: ref/T, b: ref/T) -> ref/T during b\n    origin a outlives b\n    return a\nspecialize func choose<i32>(a: ref/i32, b: ref/i32) -> ref/i32 during b => a\nlet a = 7\nlet b = 9\nrequire choose(a@ref, b@ref) == 7 else => $abort(\"inherited relation\")", string.Empty);

    [Theory]
    [InlineData("origin a outlives b")]
    [InlineData("origin a == b")]
    [InlineData("origin a == static")]
    public void AFullSpecializationCannotAddAnOriginPrecondition(string clause)
    {
        var c = MinimalEmissionTest.Analyze("func choose<T>(a: ref/T, b: ref/T) -> i32 => 1\nspecialize func choose<i32>(a: ref/i32, b: ref/i32) -> i32\n    " + clause + "\n    return 2\n()");
        var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.FunctionKoto>().Single(x => x.IsSpecialization);
        Assert.Single(Kimi.Compiler.Parsing.OriginClauses.Get(implementation));
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.UnprovenOriginContract_Kd);
    }

    // A rejecting row's records as (node, code) pairs in source order.
    private static void AssertRecords(string row, string specialization, IEnumerable<(string Node, Kimi.DiagnosticCode Code)> records)
    {
        var c = MinimalEmissionTest.Analyze(Box + specialization + "\n()");
        Assert.False(c.Binding.Result.IsComplete, row);
        Assert.Equal(records, c.Binding.Issues.OrderBy(x => x.Node.Span.Start).Select(x => (x.Node.ToString(), x.Code)));
    }
}
