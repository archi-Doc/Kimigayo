// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class FunctionItemTest
{
    private const string Need = "func need<T>(x: T) -> i32\n    T is Owned\n    return 0\n";

    // A Container body never sees a document-local function (SPEC 6.1.1, 9.5), so a struct's members reach `need` through a group.
    private const string GroupNeed = "group N\n    public func need<T>(x: T) -> i32\n        T is Owned\n        return 0\n";

    // A Contract nested in an Origin-bearing struct inherits its Origin (SPEC 6.1.3).
    private const string ViewC2 = "struct S {source}\n    public let v: ref/u8 during source\n    public init(v: ref/u8 during source) => self.v = v\n    public contract C2\n        func get(self: ref/Self) -> i32\n";

    [Theory]
    [InlineData("Local", "let f = inc\nlet g = f\nrequire f(40) + g(0) == 42 else => $abort(\"copy\")")]
    [InlineData("Tuple", "let pair = (inc, 40)\nrequire pair.0(pair.1) == 41 else => $abort(\"tuple\")")]
    [InlineData("Array", "let items = [inc, inc]\nrequire items[1](41) == 42 else => $abort(\"array\")")]
    [InlineData("Borrow", "let f = inc\nlet r = f@ref\nrequire r(41) == 42 else => $abort(\"borrow\")")]
    [InlineData("Erase", "let f = inc\nlet erased: (i32) -> i32 = f\nrequire erased(40) + f(0) == 42 else => $abort(\"erase\")")]
    [InlineData("EraseArgument", "func apply(f: (i32) -> i32) -> i32 => f(41)\nlet f = inc\nrequire apply(f) == 42 else => $abort(\"argument\")")]
    [InlineData("Capture", "let f = inc\nlet closure = func [f] (v: i32) -> i32 => f(v)\nlet erased: (i32) -> i32 = closure\nrequire erased(41) == 42 and closure(40) == 41 else => $abort(\"capture\")")]
    [InlineData("Option", "let item = inc\nlet option = Option.Some(item)\nmatch option\n    .Some(let f) => require f(41) == 42 else => $abort(\"option\")\n    .None => $abort(\"none\")")]
    [InlineData("Iteration", "let items = [inc, inc]\nvar total = 0\nfor f in items@move\n    total += f(20)\nrequire total == 42 else => $abort(\"iteration\")")]
    [InlineData("Default", "func answer(value: i32 = 41) -> i32 => value + 1\nlet f = answer\nrequire f(41) == 42 else => $abort(\"explicit input\")")]
    [InlineData("SharedInput", "func read(value: ref/i32) -> i32 => value + 1\nlet f = read\nlet value = 41\nrequire f(value@ref) == 42 else => $abort(\"input\")")]
    [InlineData("Aggregate", "func sum(value: (i32, i32)) -> (i32, i32) => (value.0 + 1, value.1)\nlet f = sum\nlet result = f((40, 2))\nrequire result.0 == 41 and result.1 == 2 else => $abort(\"aggregate\")")]
    [InlineData("Qualified", "group G\n    public func answer(value: i32) -> i32 => value + 1\nlet f = G.answer\nrequire f(41) == 42 else => $abort(\"qualified\")")]
    public void StoredItemsKeepTheirDeclarationAndCopySemantics(string name, string body)
    {
        var source = "func inc(value: i32) -> i32 => value + 1\n" + body;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        ScalarEmissionTest.EmitFixture("FunctionItem" + name, source, string.Empty);
    }

    [Fact]
    public void DifferentDeclarationsHaveDifferentItemTypes()
    {
        var c = MinimalEmissionTest.Analyze("func first() => ()\nfunc second() => ()\nlet a = first\nlet b = second\nlet c = first");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var variables = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().ToArray();
        Assert.NotSame(variables[0].SymbolOf()!.Type, variables[1].SymbolOf()!.Type);
        Assert.Same(variables[0].SymbolOf()!.Type, variables[2].SymbolOf()!.Type);
    }

    [Theory]
    [InlineData("let x: i32 = inc")]
    [InlineData("func g() -> i32 => inc")]
    [InlineData("let b = inc == inc")]
    [InlineData("let f = inc\nlet r = f(value: 1)")]
    [InlineData("let f = inc\nlet r = f()")]
    [InlineData("let f = inc\nlet r = f<i32>(1)")]
    [InlineData("let f = inc\nlet bad: (bool) -> i32 = f")]
    [InlineData("let f = inc\nlet bad: (i32) -> i32 = f@ref")]
    public void ItemsDoNotInventConversionsLabelsDefaultsOrTypeParameters(string body)
    {
        var c = MinimalEmissionTest.Analyze("func inc(value: i32 = 41) -> i32 => value + 1\n" + body);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Generate(TextWriter.Null));
    }

    // SPEC 15.3.4 (last bullet), 15.2.3, 8.15 (PLAN G826): an Item is Owned when its bound arguments and what its declared signature
    // inherits from an enclosing body are: an enclosing Origin (a parameter's, a meet's, a local's) or Type parameter that the signature
    // names anywhere (a parameter, the result, a Function Type parameter) and that is not proven static or Owned fails at the use, also
    // through a generic call, an Option or a forwarding function (o1blk was accepted). An erasure judges its bound arguments alone, and
    // the required Function Type states the signature (SPEC 7.7.4; o8era, o8eraT, e4hide and its Closure twin e5hidec stay valid). A
    // nested function's own Type parameter bound to a Function Type is never an inherited atom (x3fnnest). The function sits in an inner
    // block (G811's limit covers one written directly in the body).
    [Theory]
    [InlineData("o1blk", "func h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return need(pk)\n    return 0\n", "need(pk)")]
    [InlineData("o1vblk", "func h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        let f = pk\n        return need(f)\n    return 0\n", "need(f)")]
    [InlineData("o1tup", "func h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return need((pk, 1))\n    return 0\n", "need((pk, 1))")]
    [InlineData("o1res", "func h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk() -> Option<ref/u8 during a> => .None\n        return need(pk)\n    return 0\n", "need(pk)")]
    [InlineData("o1deep", "func h(anchor0: ref/u8 during a) -> i32\n    if true\n        func q() -> i32\n            if true\n                func pk(x: ref/u8 during a) -> i32 => 0\n                return need(pk)\n            return 0\n        return q()\n    return 0\n", "need(pk)")]
    [InlineData("o2genblk", "func h<T>(x: ref/T) -> i32\n    if true\n        func pk(y: T) -> i32 => 0\n        return need(pk)\n    return 0\n", "need(pk)")]
    [InlineData("o3rel", "func h(anchor0: ref/u8 during a) -> i32\n    origin a outlives static\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return need(pk)\n    return 0\n", null)]
    [InlineData("o4own", "func h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk(x: ref/u8 during c) -> ref/u8 during c => x\n        return need(pk)\n    return 0\n", null)]
    [InlineData("o2genok", "func h<T>(x: ref/T) -> i32\n    T is Owned\n    if true\n        func pk(y: T) -> i32 => 0\n        return need(pk)\n    return 0\n", null)]
    [InlineData("o8era", "func h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        let g: (ref/u8 during a) -> i32 = pk\n        return g(anchor0)\n    return 0\n", null)]
    [InlineData("a4idres", "func id<T>(x: T) -> T => x@move\nfunc h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return need(id(pk))\n    return 0\n", "need(id(pk))")]
    [InlineData("a6opt", "func wrap<T>(x: T) -> Option<T> => .Some(x@move)\nfunc h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return need(wrap(pk))\n    return 0\n", "need(wrap(pk))")]
    [InlineData("a7genpass", "func fwd<F>(f: F) -> i32\n    F is Owned\n    return need(f@move)\nfunc h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return fwd(pk)\n    return 0\n", "fwd(pk)")]
    [InlineData("a8Tcopy", "func h<T>(x: ref/T) -> i32\n    T is Copy\n    if true\n        func pk(y: T) -> i32 => 0\n        return need(pk)\n    return 0\n", "need(pk)")]
    [InlineData("a10Tres", "func h<T>(x: ref/T) -> i32\n    if true\n        func pk() -> Option<T> => .None\n        return need(pk)\n    return 0\n", "need(pk)")]
    [InlineData("x6meet", "func h(anchor0: ref/u8 during a, anchor1: ref/u8 during b) -> i32\n    if true\n        func pk(x: ref/u8 during (a and b)) -> i32 => 0\n        return need(pk)\n    return 0\n", "need(pk)")]
    [InlineData("y1fnsig", "func h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk(f: (ref/u8 during a) -> i32) -> i32 => 0\n        return need(pk)\n    return 0\n", "need(pk)")]
    [InlineData("y3fnsigT", "func h<T>(x: ref/T) -> i32\n    if true\n        func pk(f: (T) -> i32) -> i32 => 0\n        return need(pk)\n    return 0\n", "need(pk)")]
    [InlineData("y5local", "func h(anchor0: ref/u8 during a) -> i32\n    let r: ref/u8 = anchor0\n    if true\n        func pk(x: ref/u8 during r) -> i32 => 0\n        return need(pk)\n    return 0\n", "need(pk)")]
    [InlineData("o8eraT", "func h<T>(x: T) -> i32\n    if true\n        func pk(y: T) -> i32 => 0\n        let g: (T) -> i32 = pk\n        return g(x@move)\n    return 0\n", "pk\n        return g")]
    [InlineData("e4hide", "func h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        let g: (ref/u8 during static) -> i32 = pk\n        return need(g@move)\n    return 0\n", null)]
    [InlineData("e5hidec", "func h(anchor0: ref/u8 during a) -> i32\n    let g: (ref/u8 during static) -> i32 = func (x: ref/u8 during a) -> i32 => 0\n    return need(g@move)\n", null)]
    [InlineData("x3fnnest", "func h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk<U>(y: U) -> i32 => 0\n        return need(pk<(ref/u8) -> i32>)\n    return 0\n", null)]
    public void AnItemIsOwnedOnlyWhenWhatItsSignatureNamesIs(string id, string body, string? at)
        => AssertOwnedRecord(id, Need + body + "public func main() -> () => ()\n", at);

    // SPEC 15.2.3, 15.3.4, 7.7.4, 6.1.3, 3.5 (PLAN G826, G811-o revisions 1-2): an Item's Owned parts are its bound arguments, a
    // requirement Contract's inherited Origins included, and the atoms its declared signature inherits from an enclosing body (an
    // enclosing struct's Type parameter or Origin for a function in a method's block: x8bstructT, x10bstructS), never a Type a projection
    // or a bound argument derives (p1projgen, r2reqok, p7selfreq, x1fnarg, x2fnoptarg, x4assocfn) or its own Constraint set's slot
    // (b8setk); Copy reads no Origin (a1copy). An erasure judges the environment's parts, the Contract's inherited Origin included
    // (w3era, w6unusedera, w9eraneed, w12setform), in their one numbering (q1era2: a requirement Item's second Contract-reference Type
    // argument).
    [Theory]
    [InlineData("a1copy", "func needc<T>(x: T) -> i32\n    T is Copy\n    return 0\nfunc h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return needc(pk)\n    return 0\n", null)]
    [InlineData("a2copyown", "func needco<T>(x: T) -> i32\n    T is Copy and Owned\n    return 0\nfunc h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return needco(pk)\n    return 0\n", "needco(pk)")]
    [InlineData("q1era2", "contract Pick<A, B>\n    func pick(self: ref/Self, a: ref/A, b: ref/B) -> i32\nfunc r<T, U>(x: ref/T, u: ref/U, n: ref/i32) -> i32\n    T is Pick<i32, U> and Owned\n    let op: (ref/T, ref/i32, ref/U) -> i32 = T.pick\n    return op(x, n, u)\n", "T.pick")]
    [InlineData("p1projgen", Need + "contract Source\n    associate Item\nfunc pk<T>(x: ref/T) -> Option<T.(Source).Item>\n    T is Source\n    return .None\nfunc h<T>(x: ref/T) -> i32\n    T is Source and Owned\n    return need(pk<T>)\n", null)]
    [InlineData("r2reqok", Need + "func r<T, U>(x: ref/T, y: ref/U) -> i32\n    T is Addable<U> and Owned\n    U is Owned\n    return need(T.added)\n", null)]
    [InlineData("p7selfreq", Need + "contract Maker\n    associate Out\n    func make(self: ref/Self) -> Option<Self.(Maker).Out>\nfunc r<T>(x: ref/T) -> i32\n    T is Maker and Owned\n    return need(T.make)\n", null)]
    [InlineData("b8setk", Need + "contract Source\n    associate Item\nstruct Cursor {at}\n    Self is Source\n    associate Item is i32\n    public let item: ref/i32 during at\n    public init(item: ref/i32 during at) => self.item = item\nstruct Src\n    Self is Source\n    associate Item is i32\nfunc f<T>(v: ref/T, x: ref/i32 during k.at) -> i32\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    return 0\nfunc h() -> i32 => need(f<Src>)\n", null)]
    [InlineData("x1fnarg", Need + "func pk<U>(y: U) -> i32 => 0\nfunc h() -> i32 => need(pk<(ref/u8) -> i32>)\n", null)]
    [InlineData("x4assocfn", Need + "contract Source\n    associate Item\nstruct Src\n    Self is Source\n    associate Item is (ref/u8) -> i32\nfunc pk<T>(x: ref/T) -> Option<T.(Source).Item>\n    T is Source\n    return .None\nfunc h() -> i32 => need(pk<Src>)\n", null)]
    [InlineData("w6unusedera", ViewC2 + "func r<T>(x: ref/T, anchor: ref/u8 during a) -> i32\n    T is (S during a).C2 and Owned\n    let op: (ref/T) -> i32 = T.get\n    return op(x)\n", "T.get")]
    [InlineData("w7unuseraok", ViewC2 + "func r<T>(x: ref/T, anchor: ref/u8 during a) -> i32\n    origin a outlives static\n    T is (S during a).C2 and Owned\n    let op: (ref/T) -> i32 = T.get\n    return op(x)\n", null)]
    [InlineData("x8bstructT", GroupNeed + "struct Box<T>\n    var v: T\n    public func m(self: ref/Self) -> i32\n        if true\n            func pk(y: T) -> i32 => 0\n            return N.need(pk)\n        return 0\n", "N.need(pk)")]
    [InlineData("x10bstructS", GroupNeed + "struct View {s}\n    public let r: ref/u8 during s\n    public init(r: ref/u8 during s) => self.r = r\n    public func m(self: ref/Self) -> i32\n        if true\n            func pk(y: ref/u8 during s) -> i32 => 0\n            return N.need(pk)\n        return 0\n", "N.need(pk)")]
    [InlineData("x2fnoptarg", Need + "func pk<U>(y: Option<U>) -> i32 => 0\nfunc h() -> i32 => need(pk<(ref/u8) -> i32>)\n", null)]
    [InlineData("w3era", "struct S {source}\n    public let v: ref/u8 during source\n    public init(v: ref/u8 during source) => self.v = v\n    public contract C\n        func read(self: ref/Self, x: ref/u8 during source) -> i32\nfunc r<T>(x: ref/T, anchor: ref/u8 during a) -> i32\n    T is (S during a).C and Owned\n    let op: (ref/T, ref/u8 during a) -> i32 = T.read\n    return op(x, anchor)\n", "T.read")]
    [InlineData("w9eraneed", Need + ViewC2 + "func r<T>(x: ref/T, anchor: ref/u8 during a) -> i32\n    T is (S during a).C2 and Owned\n    let op: (ref/T) -> i32 = T.get\n    return need(op@move)\n", "T.get")]
    [InlineData("w12setform", ViewC2 + "func r<T>(x: ref/T, anchor: ref/u8 during a) -> i32\n    origin k.source == a\n    T is (S{k}).C2 and Owned\n    let op: (ref/T) -> i32 = T.get\n    return op(x)\n", "T.get")]
    public void AnItemsOwnedPartsAreItsBoundAndInheritedArguments(string id, string program, string? at)
        => AssertOwnedRecord(id, program + "public func main() -> () => ()\n", at);

    // SPEC 6.1.1, 15.3.4, 15.2.3 (PLAN G826, the review's N12): a root function lies in the execution scope with the top-level bindings, so
    // its Item inherits a top-level local's Origin that its signature names (z1toplocal was accepted); one with per-call Origins only is
    // Owned. The top-level code is the startup body, without a `main`.
    [Theory]
    [InlineData("z1toplocal", "func top(x: ref/u8 during r) -> i32 => 0\n", "need(top)")]
    [InlineData("z2toplocalok", "func top(x: ref/u8) -> i32 => 0\n", null)]
    public void ARootFunctionsItemInheritsTheExecutionScopesOrigins(string id, string function, string? at)
        => AssertOwnedRecord(id, Need + "var cell: u8 = 1\nlet r = cell@ref\n" + function + "let n = need(top)\n", at);

    // SPEC 6.1.3 ("even an unused one"), 15.2.3, 3.2.1, 7.7.4 (PLAN G826b; the owner's O-249 Q4 (a)): an Item of a function nested in a
    // body is Owned, and erased, only when every enclosing parameter is, used or not: each generic slot of the functions enclosing its body
    // and the nearest enclosing Container's generic and Origin slots, two levels and through a Closure body too. A function has no Origin
    // parameter list (SPEC 15.3), so an enclosing function's Origin counts only when named (k13fnorigin).
    [Theory]
    [InlineData("j09bunused", Need + "func h<T>(t: T) -> i32\n    if true\n        func pk() -> i32 => 0\n        return need(pk)\n    return 0\n", "need(pk)")]
    [InlineData("j14bunused", "func h<T>(t: T) -> i32\n    if true\n        func pk(v: i32) -> i32 => v\n        let e: (i32) -> i32 = pk\n        return e(0)\n    return 0\n", "pk\n        return e")]
    [InlineData("j18callableowned", "func callOwned<F>(f: F) -> i32\n    F is Callable<() -> i32>\n    F is Owned\n    return f()\nfunc h<T>(t: T) -> i32\n    if true\n        func pk() -> i32 => 0\n        return callOwned(pk)\n    return 0\n", "callOwned(pk)")]
    [InlineData("j21genmethod", GroupNeed + "struct B2<T>\n    public func m() -> i32\n        if true\n            func pk() -> i32 => 0\n            return N.need(pk)\n        return 0\n", "N.need(pk)")]
    [InlineData("k01own", Need + "func h<T>(t: T) -> i32\n    T is Owned\n    if true\n        func pk() -> i32 => 0\n        return need(pk)\n    return 0\n", null)]
    [InlineData("k02eraseown", "func h<T>(t: T) -> i32\n    T is Owned\n    if true\n        func pk(v: i32) -> i32 => v\n        let e: (i32) -> i32 = pk\n        return e(0)\n    return 0\n", null)]
    [InlineData("k06twoq", Need + "func h() -> i32\n    if true\n        func q<U>(u: U) -> i32\n            if true\n                func pk() -> i32 => 0\n                return need(pk)\n            return 0\n        return q(1)\n    return 0\n", "need(pk)")]
    [InlineData("k09genfnok", Need + "func h<T, U>(t: T, u: U) -> i32\n    T is Owned\n    U is Owned\n    if true\n        func pk(x: T) -> i32 => 0\n        return need(pk)\n    return 0\n", null)]
    [InlineData("k08genfn", Need + "func h<T, U>(t: T, u: U) -> i32\n    T is Owned\n    if true\n        func pk(x: T) -> i32 => 0\n        return need(pk)\n    return 0\n", "need(pk)")]
    [InlineData("k11slot", GroupNeed + "struct C2 {s}\n    public let r: ref/i32 during s\n    public init(r: ref/i32 during s) => self.r = r\n    public func m(self: ref/Self) -> i32\n        if true\n            func pk() -> i32 => 0\n            return N.need(pk)\n        return 0\n", "N.need(pk)")]
    [InlineData("k13fnorigin", Need + "func h(anchor0: ref/u8 during a) -> i32\n    if true\n        func pk() -> i32 => 0\n        return need(pk)\n    return 0\n", null)]
    [InlineData("k16eraslot", "struct C2 {s}\n    public let r: ref/i32 during s\n    public init(r: ref/i32 during s) => self.r = r\n    public func m(self: ref/Self) -> i32\n        if true\n            func pk(x: ref/i32 during s) -> i32 => 0\n            let e: (ref/i32 during s) -> i32 = pk\n            return 0\n        return 0\n", "pk\n            return 0")]
    [InlineData("k20deep", GroupNeed + "struct Outer<T>\n    public struct Inner\n        public let n: i32\n        public init(n: i32) => self.n = n\n        public func m(self: ref/Self) -> i32\n            if true\n                func pk() -> i32 => 0\n                return N.need(pk)\n            return 0\n", "N.need(pk)")]
    [InlineData("k23clobody", Need + "func h<T>(t: T) -> i32\n    let c = func () -> i32\n        if true\n            func pk() -> i32 => 0\n            return need(pk)\n        return 0\n    return c()\n", "need(pk)")]
    [InlineData("v12cloarg", Need + "func apply<F>(f: F) -> i32\n    F is Callable<() -> i32>\n    return f()\nfunc h() -> i32\n    let r = apply(func () -> i32\n        if true\n            func pk() -> i32 => 0\n            return need(pk)\n        return 0\n    )\n    return r\n", null)]
    [InlineData("v12g", "func apply<F>(f: F) -> i32\n    F is Callable<() -> i32>\n    return f()\nfunc h() -> i32\n    let r = apply(func () -> i32\n        if true\n            func pk(v: i32) -> i32 => v\n            let e: (i32) -> i32 = pk\n            return e(0)\n        return 0\n    )\n    return r\n", null)]
    [InlineData("v12i", "group N\n    public func need<T>(v: T) -> i32\n        T is Owned\n        return 0\n    public func apply<F>(f: F) -> i32\n        F is Callable<() -> i32>\n        return f()\nstruct P\n    public func m() -> i32\n        let r = N.apply(func () -> i32\n            if true\n                func pk() -> i32 => 0\n                return N.need(pk)\n            return 0\n        )\n        return r\n", null)]
    public void AnItemOfANestedFunctionInheritsEveryEnclosingParameter(string id, string program, string? at)
        => AssertOwnedRecord(id, program + "public func main() -> () => ()\n", at);

    // SPEC 15.2.3, 6.1.3 (PLAN G826b): an erasure's inherited parameter is named in the record's Note: a Type parameter as not proven
    // Owned, a Container's Origin slot as not static.
    [Theory]
    [InlineData("func h<T>(t: T) -> i32\n    if true\n        func pk(v: i32) -> i32 => v\n        let e: (i32) -> i32 = pk\n        return e(0)\n    return 0\n", "the inherited Type parameter `T` is not proven Owned")]
    [InlineData("struct C2 {s}\n    public let r: ref/i32 during s\n    public init(r: ref/i32 during s) => self.r = r\n    public func m(self: ref/Self) -> i32\n        if true\n            func pk(x: ref/i32 during s) -> i32 => 0\n            let e: (ref/i32 during s) -> i32 = pk\n            return 0\n        return 0\n", "the inherited Origin `s` is not static")]
    public void AnErasuresInheritedParameterIsNamed(string program, string note)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(program + "public func main() -> () => ()\n").Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnprovenConstraint_Kd), error.Code);
        Assert.Contains(note, error.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void CliAndLspExplainAnItemTypeMismatch()
    {
        const string Source = "func inc(value: i32) -> i32 => value + 1\nlet number: i32 = inc";
        var path = Path.GetFullPath("item-mismatch.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal("inc", Source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(["actual", "expected"], error.Reason!.Select(static x => x.Name));
        Assert.Equal(["function item inc", "i32"], error.Reason!.Select(static x => x.Value));
        Assert.Equal("expected i32, found function item inc", error.Label);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("let number: i32 = inc\n  |                   ^^^", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Code, sent.Code);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
        }
    }

    // SPEC 15.3.4, 15.6.5, 10.7, 7.7.4: the value of a function nested in a block, whose signature names the enclosing function's fixed
    // Origin, is compared with a required Function Type under the enclosing function's clauses: at a local, a result, an assignment and
    // an argument, after an open evidence region too (PLAN G827).
    [Theory]
    [InlineData("func h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    origin b outlives a\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        let f: (ref/u8 during b) -> i32 = pk\n        return f(other)\n    return 0")]
    [InlineData("func fallback(x: ref/u8 during q) -> i32 => 0\nfunc h(anchor0: ref/u8 during a, other: ref/u8 during b) -> (ref/u8 during b) -> i32\n    origin b outlives a\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return pk\n    return fallback")]
    [InlineData("func h(anchor0: ref/u8 during a, mid: ref/u8 during c, other: ref/u8 during b) -> i32\n    origin b outlives c\n    origin c outlives a\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        let f: (ref/u8 during b) -> i32 = pk\n        return f(other)\n    return 0")]
    [InlineData("func h(anchor0: ref/u8 during a, mid: ref/u8 during c, other: ref/u8 during b) -> i32\n    origin b outlives a\n    origin a outlives c\n    if true\n        func pk(x: ref/u8 during a) -> ref/u8 during a => x\n        let f: (ref/u8 during b) -> ref/u8 during c = pk\n        let r = f(other)\n        return 0\n    return 0")]
    [InlineData("func apply(g: (ref/u8 during q) -> i32, r: ref/u8 during q) -> i32 => 0\nfunc h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    origin b outlives a\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return apply(pk, other)\n    return 0")]
    [InlineData("func h<T>(x: ref/T during a, o: ref/T during b) -> i32\n    origin b outlives a\n    if true\n        func pk(y: ref/T during a) -> i32 => 0\n        let f: (ref/T during b) -> i32 = pk\n        return f(o)\n    return 0")]
    [InlineData("func h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    origin b outlives a\n    if true\n        func pk<U>(y: ref/U during a) -> i32 => 0\n        let f: (ref/u8 during b) -> i32 = pk\n        return f(other)\n    return 0")]
    [InlineData("func fb(x: ref/u8 during q) -> i32 => 0\nfunc h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    origin b outlives a\n    var f: (ref/u8 during b) -> i32 = fb\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        f = pk\n    return f(other)")]
    [InlineData("func both<T>(g: (T) -> i32, k: (T) -> i32, v: T) -> i32 => 0\nfunc h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    origin b outlives a\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return both(func (x: ref/u8) => 0, pk, other)\n    return 0")]
    [InlineData("func both<T>(v: T, g: (T) -> i32, k: (T) -> i32) -> i32 => 0\nfunc h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    origin b outlives a\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return both(other, func (x: ref/u8) => 0, pk)\n    return 0")]
    public void NestedItemsAreErasedUnderTheEnclosingFunctionsClauses(string body)
        => Assert.Equal(string.Empty, Records("public func main() => ()\n" + body));

    // The invalid twins: without the clause the parameter or the result needs, the record names that relation.
    [Theory]
    [InlineData("func h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    origin a outlives b\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        let f: (ref/u8 during b) -> i32 = pk\n        return f(other)\n    return 0", "UnprovenOriginContract_Kd pk: the 1st parameter requires b outlives a, which is not proven")]
    [InlineData("func h(anchor0: ref/u8 during a, mid: ref/u8 during c, other: ref/u8 during b) -> i32\n    origin b outlives a\n    if true\n        func pk(x: ref/u8 during a) -> ref/u8 during a => x\n        let f: (ref/u8 during b) -> ref/u8 during c = pk\n        let r = f(other)\n        return 0\n    return 0", "UnprovenOriginContract_Kd pk: the result requires a outlives c, which is not proven")]
    [InlineData("func h<T>(x: ref/T during a, o: ref/T during b) -> i32\n    if true\n        func pk(y: ref/T during a) -> i32 => 0\n        let f: (ref/T during b) -> i32 = pk\n        return f(o)\n    return 0", "UnprovenOriginContract_Kd pk: the 1st parameter requires b outlives a, which is not proven")]
    [InlineData("func apply(g: (ref/u8 during q) -> i32, r: ref/u8 during q) -> i32 => 0\nfunc h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return apply(pk, other)\n    return 0", "NoApplicableOverload_Kd apply(pk, other): none of 1 candidates applies")]
    public void NestedItemsAreNotErasedWithoutTheEnclosingFunctionsClauses(string body, string record)
    {
        var source = "public func main() => ()\n" + body;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(record, $"{error.Code} {source.Substring(error.Span!.Value.Start, error.Span.Value.Length)}: {error.Label}");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmItemBindingAnalysisAndEmissionAllocateNothing()
    {
        const string Source = "func inc(value: i32) -> i32 => value + 1\nlet f = inc\nlet g: (i32) -> i32 = f\nrequire f(1) == g(1) else => $abort(\"item\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }

    // The check's records as (id, code, start), against one UnprovenConstraint_Kd at `at` or none.
    private static void AssertOwnedRecord(string id, string source, string? at)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        (string, string, int)[] expected = at is null ? [] : [(id, nameof(DiagnosticCode.UnprovenConstraint_Kd), source.IndexOf(at, StringComparison.Ordinal))];
        Assert.Equal(expected, errors.Select(x => (id, x.Code, x.Span!.Value.Start)));
    }

    private static string Records(string source)
        => string.Join("; ", DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {(x.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty)}"));
}
