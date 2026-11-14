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
    // header's Type arguments the limits below do not apply. Rows are named by G863's coverage table (row and program).
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
    // one-key specializations included); an Origin-carrying argument still meets the target search, the inherited Constraints and
    // the one key. Each row lists its records' nodes in source order.
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
    [InlineData("C23 i4", "specialize func f<Box>(x: ref/Box) -> i32 => 1\nspecialize func f<Box during static>(x: ref/Box) -> i32 => 2", "specialize func f<Box during static>(x: ref/Box) -> i32 => 2", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("C23 i6", "specialize func f<Box>(x: ref/Box) -> i32 => 1\nspecialize func f<Box>(x: ref/Box) -> i32 => 2", "specialize func f<Box>(x: ref/Box) -> i32 => 1|specialize func f<Box>(x: ref/Box) -> i32 => 2", Kimi.DiagnosticCode.DuplicateBinding_Kd)]
    public void RejectsWrittenArgumentOrigins(string row, string specialization, string located, Kimi.DiagnosticCode code)
        => AssertRecords(row, specialization, located.Split('|').Select(x => (x, code)));

    // SPEC 8.13.2, 15.4.2 (PLAN G863): an omitted Origin of T's binding that no row completes yet is one located Unsupported_Kd per
    // occurrence, and the header rests on it: a borrow layer in a Type argument (outer, uniq, nested in a Type argument, Tuple,
    // borrow target or Option, a pair's outer layer) or nested in a restated input, a Function Type result that no input of its own
    // gives (a borrow, an aggregate, nested in an Option or a Function Type's input), and an undecided inherited Constraint over the
    // header's universal Origins (`T is Owned`).
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
    public void UncompletedArgumentOriginsAreALocatedLimit(string row, string specialization, string located)
        => AssertRecords(row, specialization, located.Split('|').Select(x => (x, Kimi.DiagnosticCode.Unsupported_Kd)));

    // SPEC 23.1.2 (PLAN G863): independent records of one header, each at its node, in source order: `static` cannot supply a uniq
    // slot beside the restriction (C3 i5); no target beside the limit of the restated nested layer (C19 i8); the Function Type
    // result's limit beside the restated result's pre-existing missing Origin (C20 v18, G863's remainder (c)); a local borrow as the
    // call's Origin in the body (C10 r05; Binding records it once, the CLI's report shows it twice as for its ordinary twin: G56's row).
    [Theory]
    [InlineData("C3 i5", "func w<T>(d: uniq/T) -> i32 => 0\nspecialize func w<W during static>(d: uniq/W) -> i32 => 1", "IncompatibleContractImplementation_Kd: specialize func w<W during static>(d: uniq/W) -> i32 => 1|InvalidOriginBinding_Kd: W during static|InvalidOriginBinding_Kd: W")]
    [InlineData("C19 i8", "specialize func missing<i32>(x: Option<ref/i32>) -> i32 => 1", "MissingSpecializationTarget_Kd: specialize func missing<i32>(x: Option<ref/i32>) -> i32 => 1|Unsupported_Kd: ref/i32")]
    [InlineData("C20 v18", "func apply<T>(f: T) -> i32 => 0\nspecialize func apply<() -> uniq/i32>(f: () -> uniq/i32) -> i32 => 1", "Unsupported_Kd: uniq/i32|MissingOriginBinding_Kd: uniq/i32")]
    [InlineData("C10 r05", "func make<T>(x: i32) -> Option<T> => .None\nspecialize func make<Box>(x: i32) -> Option<Box>\n    let n: i32 = 1\n    return .Some(Box.init(n@ref))", "UnsatisfiedOriginRelation_Kd: Box.init(n@ref)")]
    public void ReportsEveryIndependentHeaderRecord(string row, string specialization, string records)
        => AssertRecords(row, specialization, records.Split('|').Select(x => (x[(x.IndexOf(": ", StringComparison.Ordinal) + 2)..], Enum.Parse<Kimi.DiagnosticCode>(x[..x.IndexOf(": ", StringComparison.Ordinal)]))));

    // SPEC 8.13.3: specializations never enter the candidate set; an invalid one does not fail calls to the original.
    [Fact]
    public void InvalidSpecializationDoesNotCascadeIntoCalls()
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + "specialize func weight<i32>(other: ref/i32) -> i32 => 2\nfunc forward<T>(value: ref/T) -> i32 => weight<T>(value)\nlet value: i32 = 4\nlet result = weight<i32>(value@ref) + forward<i32>(value@ref)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.All(c.Binding.Issues, x => Assert.Equal(Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd, x.Code));
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
