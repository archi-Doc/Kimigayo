// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Verification;

// Linked into tests and benchmarks so their language inputs cannot drift apart.
internal static class VerificationWorkloads
{
    internal static string InheritedPlans(int depth, int width)
    {
        var source = new StringBuilder();
        for (var layer = 0; layer < depth; layer++)
        {
            source.Append("open struct Layer").Append(layer);
            if (layer != 0)
            {
                source.Append(": Layer").Append(layer - 1);
            }

            source.AppendLine();
            for (var field = 0; field < width; field++)
            {
                source.Append("    public var f").Append(layer).Append('_').Append(field).Append(": i32 = ").Append(field).AppendLine();
            }

            source.AppendLine("    public init() => ()");
            if (layer == 0)
            {
                source.AppendLine("    public func read(self: ref/Self) -> i32 => self.f0_0");
                source.AppendLine("    public computed peek: i32\n        get(self: ref/Self) -> i32 => self.f0_0");
            }
        }

        source.Append("var x = Layer").Append(depth - 1).AppendLine(".init()");
        for (var layer = 0; layer < depth; layer++)
        {
            source.Append("x.f").Append(layer).Append('_').Append(width - 1).AppendLine(" += 1");
            source.Append("require x.f").Append(layer).Append('_').Append(width - 1).Append(" == ").Append(width).AppendLine(" else => $abort(\"field\")");
        }

        source.Append("require x.read() == ").Append(width == 1 ? 1 : 0).Append(" and x.peek == ").Append(width == 1 ? 1 : 0).AppendLine(" else => $abort(\"call\")");
        return source.ToString();
    }

    internal const string ExclusiveViews = "var values: [2 of i32] = [1, 2]\nvar view = values.sliceUniq()\nview[^1] = 9\nrequire view[1..][0] == 9 else => $abort(\"value\")";

    internal const string SliceCopies = "let values: [3 of i32] = [1, 2, 3]\nlet copy = values.slice().toArray()\nrequire copy[1] == 2 else => $abort(\"copy\")";

    internal const string ContextualFunctionReference = "func choose(value: i32) -> i32 => value + 1\nfunc choose(value: bool) -> bool => value\nfunc apply<T, F>(value: T, action: ref/F) -> T\n    F is Callable<(T) -> T>\n    return action(value@move)\nlet result = apply(41, choose)";

    internal const string GenericFunctionReference = "func identity<T>(value: T) -> T => value@move\nfunc apply<T, F>(value: T, action: ref/F) -> T\n    F is Callable<(T) -> T>\n    return action(value@move)\nlet erased: (i32) -> i32 = identity\nlet result = apply(41, identity) + erased(1)";

    internal const string NestedInferenceAndUniversalErasure = "func constant() -> ref/i32 during s => $abort(\"never\")\nfunc make<T>(f: () -> T, other: T) -> T => other@move\n" +
        "func none<T>() -> Option<T> => .None\nfunc keep(o: Option<i64>) -> i32 => 1\nlet x: i32 = 3\nlet c = constant\n" +
        "require make(c, x@ref)@follow + keep(none()) == 4 else => $abort(\"warm\")";

    internal const string ReceiverDependentValueCall = "func pick(a: ref/i32, b: ref/i32) -> ref/i32 => a\nvar n = 7\nlet view = n@uniq\nlet k = 3\n" +
        "let f = func [k] (x: ref/i32) => pick(k@ref, x)\nlet m = 4\nlet r = f(m@ref)\nvar g = func [view] () => view\nlet s = g()\ns@follow = r@follow";

    internal const string EnvironmentBorrows = "var n = 7\nlet view = n@uniq\nlet m = 4\nvar f = func [view, var m] () -> i32\n    let r = m@ref\n" +
        "    let c = view@follow@uniq\n    c@follow = r@follow\n    m = 1\n    view@follow += m\n    return view@follow\nlet v = f()";

    internal const string FixedInputValueCall = "func use(x: ref/i32) -> i32\n    let c = func (pair: (ref/i32 during x, i32)) => pair.0@follow + pair.1\n" +
        "    let d = func (o: Option<ref/i32 during x>) -> i32\n        match o\n            .Some(let r) => return r@follow\n            .None => return 0\n" +
        "    let e = func (n: ref/i32 during x) => n@follow\n    return c((x, 1)) + d(.Some(x)) + e(x)\nlet n: i32 = 2\nlet v = use(n@ref)";

    internal const string InputDependentValueCall = "struct Box<T>\n    var item: T\n\n    public init(item: T)\n        self.item = item@move\n\n    public func get(self) -> ref/T during self => self.item@ref\nfunc bump(value: uniq/i32) -> uniq/i32 => value\nlet box = Box<i32>.init(item: 5)\nlet get: (ref/Box<i32>) -> ref/i32 = Box<i32>.get\nlet r = get(box@ref)\nvar k: i32 = 1\nlet b = bump\nlet d = b(k@uniq)\nlet e = b(d)\ne@follow = r@follow\nd@follow += 1";

    // Fixed scaling axes shared by functional/allocation checks and opt-in measurements.
    internal static string CallableRegions(string axis, int count)
    {
        if (axis.StartsWith("fixed-", StringComparison.Ordinal))
        {
            return FixedOriginLoans(axis, count);
        }

        var source = new StringBuilder();
        switch (axis)
        {
            case "candidates":
                source.AppendLine("func select<F>(action: ref/F) -> i32");
                for (var i = 1; i <= count; i++)
                {
                    source.AppendLine($"    F is Callable<([{i} of i32]) -> i32>");
                }

                source.AppendLine("    return action([42])");
                source.AppendLine("public func main() => ()");
                break;
            case "results":
                source.AppendLine("let action = func [] (flag: i32)");
                for (var i = 0; i < count - 1; i++)
                {
                    source.AppendLine($"    if flag == {i}\n        return {i}@i32");
                }

                source.AppendLine($"    return {count - 1}@i32");
                source.AppendLine("require action(0) == 0 else => $abort(\"result\")");
                break;
            case "regions":
                source.AppendLine("func check(flag: bool) -> i32");
                for (var i = 0; i < count; i++)
                {
                    source.AppendLine($"    var n{i} = {i}\n    var r{i} = n{i}@ref");
                }

                for (var i = 0; i < count; i++)
                {
                    source.AppendLine($"    if flag\n        r{i} = n{(i + 1) % count}@ref");
                }

                source.AppendLine("    var iteration = 0\n    while iteration < 2");
                for (var i = 0; i < count; i++)
                {
                    source.AppendLine($"        r{i} = n{(i + 2) % count}@ref");
                }

                source.AppendLine("        iteration += 1\n    var sum = 0");
                for (var i = 0; i < count; i++)
                {
                    source.AppendLine($"    sum += r{i}@follow");
                }

                for (var i = 0; i < count; i++)
                {
                    source.AppendLine($"    n{i} = 42");
                }

                source.AppendLine($"    return sum\nrequire check(true) == {count * (count - 1) / 2} else => $abort(\"region\")");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(axis));
        }

        return source.ToString();
    }

    // Generic default contexts (PLAN G82): `siblings` expands one omitted default `count` times in one body, alternating an
    // owner and a Copy Type; `depth` nests `count` omitted defaults over that one, each making the inner level's sample.
    internal static string GenericDefaults(string axis, int count)
    {
        const string Prelude = "contract Maker\n    func make() -> Self\n" +
            "struct Box\n    Self is Maker\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func make() -> Self => Box.init(5)\n    drop => Console.writeLine(\"drop\")\n" +
            "struct Pt\n    Self is Copy and Maker\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func make() -> Self => Pt.init(5)\n" +
            "group Helpers\n    public func evaluate<T>(sample: T, marker: i32 = label result: do\n        let pending: Option<T> = .Some(T.make())\n" +
            "        match pending@move\n            .Some(let value) => exit to result 1\n            .None => exit to result 0\n    ) -> i32\n        T is Maker\n        return marker\n";
        var source = new StringBuilder(Prelude);
        switch (axis)
        {
            case "siblings":
                for (var i = 0; i < count; i++)
                {
                    source.AppendLine($"require Helpers.evaluate({(i % 2 == 0 ? "Box" : "Pt")}.init({i})) == 1 else => $abort(\"sibling\")");
                }

                break;
            case "depth":
                for (var level = 1; level <= count; level++)
                {
                    source.AppendLine($"func level{level}<T>(sample: T, total: i32 = {(level == 1 ? "Helpers.evaluate" : $"level{level - 1}")}(T.make())) -> i32\n    T is Maker\n    return total");
                }

                source.AppendLine($"require level{count}(Box.init(1)) == 1 and level{count}(Pt.init(2)) == 1 else => $abort(\"depth\")");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(axis));
        }

        return source.ToString();
    }

    internal static string FixedOriginLoans(string axis, int count)
    {
        if (axis == "fixed-control")
        {
            return FixedInputValueCall;
        }

        var source = new StringBuilder();
        if (axis == "fixed-roots")
        {
            source.Append("func check(x: ref/i32");
            for (var i = 0; i < count; i++)
            {
                source.Append($", z{i}: uniq/i32");
            }

            source.AppendLine(") -> i32");
            for (var i = 0; i < count; i++)
            {
                source.AppendLine($"    origin z{i} outlives x");
            }

            source.AppendLine("    let pass = func (value: ref/i32 during x) -> ref/i32 during x => value");
            for (var i = 0; i < count; i++)
            {
                source.AppendLine($"    let r{i} = pass(z{i}@follow@ref)");
            }

            source.AppendLine("    var sum = 0");
            for (var i = 0; i < count; i++)
            {
                source.AppendLine($"    sum += r{i}@follow");
            }

            source.AppendLine("    return sum\nlet x: i32 = 0");
            for (var i = 0; i < count; i++)
            {
                source.AppendLine($"var z{i}: i32 = {i}");
            }

            source.Append("require check(x@ref");
            for (var i = 0; i < count; i++)
            {
                source.Append($", z{i}@uniq");
            }

            source.AppendLine($") == {count * (count - 1) / 2} else => $abort(\"roots\")");
        }
        else if (axis is "fixed-calls" or "fixed-joins")
        {
            source.AppendLine("func check(z: uniq/i32, x: ref/i32, flag: bool) -> i32\n    origin z outlives x\n    let pass = func (value: uniq/i32 during x) -> uniq/i32 during x => value");
            for (var i = 0; i < count; i++)
            {
                var call = axis == "fixed-joins" ? "if flag => pass(z) else => pass(z)" : "pass(z)";
                source.AppendLine($"    let r{i} = {call}\n    r{i}@follow += 1");
            }

            source.AppendLine($"    return z@follow\nvar z: i32 = 0\nlet x: i32 = 0\nrequire check(z@uniq, x@ref, true) == {count} else => $abort(\"calls\")");
            source.AppendLine($"z = 0\nrequire check(z@uniq, x@ref, false) == {count} else => $abort(\"joins\")");
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(axis));
        }

        return source.ToString();
    }

    internal static string FunctionReferenceRanking(bool borrowed)
    {
        const string Source = "func fail(value: i32) -> Never => $abort(\"not called\")\nfunc choose(value: (i32) -> i32) -> i32 => 0\nfunc choose(value: (i32) -> Never) -> i32 => 42\nlet action: ((i32) -> Never) -> i32 = choose\nlet value: (i32) -> Never = fail\nlet result = action(value@move)";
        return borrowed ? BorrowFunctionReferenceInputs(Source) : Source;
    }

    internal static string BorrowFunctionReferenceInputs(string source)
        => source.Replace("func choose(value: (i32) -> i32)", "func choose(value: ref/((i32) -> i32))", StringComparison.Ordinal)
            .Replace("func choose(value: (i32) -> Never)", "func choose(value: ref/((i32) -> Never))", StringComparison.Ordinal)
            .Replace("((i32) -> Never) -> i32", "(ref/((i32) -> Never)) -> i32", StringComparison.Ordinal)
            .Replace("action(value@move)", "action(value@ref)", StringComparison.Ordinal);

    internal const string ObjectItem = "struct Item\n    public let id: i32 = 7\n    public func read(self: ref/Self) -> i32 => self.id\n    drop => Console.writeLine(\"drop\")\n";

    internal const string RcClone = "struct Item\n    public let value: i32\n    public init(value: i32) => self.value = value\n    drop => Console.writeLine(\"drop\")\n" +
        "let first = Kimi.Intrinsics.makeRc(Item.init(7))\nlet second = Kimi.Intrinsics.clone(first@ref)\nrequire second.value == 7 else => $abort(\"clone\")";

    internal static string SharedClone(bool atomic)
        => atomic ? RcClone.Replace("makeRc(", "makeArc(", StringComparison.Ordinal) : RcClone;

    internal static string ObjectView(bool stored)
        => ObjectItem + "let owner = Kimi.Intrinsics.makeObj(Item.init())\n" +
            (stored ? "let stored = (owner@move, 1)\nrequire stored.0.id == 7" : "require owner.id == 7") + " else => $abort(\"read\")";

    internal static string VirtualPlans(int depth, int slots, int aliases = 0, string mode = "obj", bool factory = false)
    {
        var source = new StringBuilder();
        for (var layer = 0; layer < depth; layer++)
        {
            source.Append("open struct Layer").Append(layer).Append("<T>");
            if (layer > 0)
            {
                source.Append(" : Layer").Append(layer - 1).Append("<T>");
            }

            source.Append("\n    public init() => ()\n");
            if (layer == 0)
            {
                source.Append("    public func ordinary(self: objref/Self) -> i32 => 7\n");
            }

            for (var slot = 0; slot < slots; slot++)
            {
                source.Append(layer == 0 ? "    public virtual func f" : "    override func f").Append(slot).Append("(self: objref/Self) -> i32");
                if (layer == depth - 1 && slot == 0 && aliases > 0)
                {
                    source.Append("\n        let a0 = self\n");
                    for (var alias = 1; alias < aliases; alias++)
                    {
                        source.Append("        let a").Append(alias).Append(" = a").Append(alias - 1).Append('\n');
                    }

                    source.Append("        return a").Append(aliases - 1).Append(".ordinary() + ");
                }
                else
                {
                    source.Append(" => ");
                }

                if (layer == 0)
                {
                    source.Append(slot);
                }
                else
                {
                    source.Append("base.f").Append(slot).Append("() + 1");
                }

                source.Append('\n');
            }
        }

        source.Append("let instance = ");
        if (factory)
        {
            source.Append("Kimi.Intrinsics.").Append(mode == "obj" ? "makeObj(" : mode == "rc" ? "makeRc(" : "makeArc(");
        }

        source.Append("Layer").Append(depth - 1).Append("<i32>.init()");
        source.Append(factory ? ")" : "@" + mode).Append('\n');
        for (var slot = 0; slot < slots; slot++)
        {
            source.Append("require instance.f").Append(slot).Append("() == ").Append(slot + depth - 1 + (slot == 0 && aliases > 0 ? 7 : 0));
            source.Append(" else => $abort(\"virtual workload\")\n");
        }

        return source.ToString();
    }

    internal static string InspectionLoans(int count, bool stored = false)
    {
        var source = new StringBuilder("func inspect(value: ref/string) -> bool => value == \"x\"\nfunc check()\n");
        for (var i = 0; i < count; i++)
        {
            source.Append("    var value").Append(i).Append(" = \"x\"\n");
            if (stored)
            {
                source.Append("    let loan").Append(i).Append(" = value").Append(i).Append("@ref\n");
            }

            source.Append("    let read").Append(i).Append(" = inspect(").Append(stored ? "loan" : "value").Append(i).Append(")\n");
            source.Append("    require read").Append(i).Append(" else => $abort(\"inspection\")\n");
        }

        return source.Append("check()").ToString();
    }

    internal static string GuardHistories(int count)
    {
        var arms = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            arms.Append("true if check(x = 2, c) => return\n                ");
        }

        return "func check(effect: (), value: bool) -> bool => value\nfunc stop() -> Never => $abort(\"stop\")\nfunc f(c: bool)\n    var x = 1\n    do\n        loop\n            if c => return else => exit\n            match c\n                " + arms + "_ => x = 3\n            x = 4\n        stop()\n    let y = x\nf(true)";
    }

    internal static string LivePartLoans(int count, bool checking)
    {
        var source = new StringBuilder("struct Counter\n    public var value: i32 = 1\nfunc relay(p: uniq/Counter) -> uniq/Counter during p => p\nfunc check()\n");
        if (checking)
        {
            source.Append("    return\n");
        }

        for (var i = 0; i < count; i++)
        {
            source.Append("    var p").Append(i).Append(" = (Counter.init(), Counter.init())\n    let a").Append(i).Append(" = relay(p").Append(i).Append(".0@uniq)\n    let m").Append(i).Append(" = p").Append(i).Append(".1@move\n");
        }

        for (var i = 0; i < count; i++)
        {
            source.Append("    a").Append(i).Append(".value += m").Append(i).Append(".value\n    require p").Append(i).Append(".0.value == 2 else => $abort(\"value\")\n");
        }

        source.Append("check()");
        return source.ToString();
    }

    /// <summary>Gets the Semantics-case workload (SPEC 8.10; PLAN G75): a one-binder definition whose uniq case Reborrows a local, stores
    /// it through an exclusive reference, captures it and passes it bare, and a two-binder definition with four cases; both valid in every
    /// case and called with owner arguments. Shared by <c>GenericCaseAllocationTest</c> and <c>Benchmark --pair-cases</c>.</summary>
    internal static string PairCaseFamilies =>
        "func keep<F>(f: F) -> F => f@move\n" +
        "func f<s/T>(value: s/T, other: s/T, x: T) -> T\n    s is owner or uniq\n    T is Copy\n    var v = value@move\n    var slot = other@move\n" +
        "    let sref = slot@uniq\n    let child = v\n    sref@follow = child@move\n    _ = slot@follow\n    v@follow = x\n" +
        "    var h = func [v] () -> T\n        return v@follow\n    let r = h()\n    let kept = keep(v)\n    _ = kept@follow\n    return r\n" +
        "func g<s/T, t/U>(a: s/T, b: t/U, x: T) -> ()\n    s is owner or uniq\n    t is owner or ref\n    T is Copy\n    U is Copy\n" +
        "    var v = a@move\n    let local = v\n    _ = local@follow\n    v@follow = x\n    _ = b\n" +
        "public func main() -> ()\n    var n: i32 = 1\n    var m: i32 = 2\n    _ = f(n, m, 9)\n    g(n, m, 9)\n";
}
