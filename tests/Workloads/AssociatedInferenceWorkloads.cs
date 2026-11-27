// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Verification;

internal static class AssociatedInferenceWorkloads
{
    // R7-C4 (PLAN G172): a one-value chain of n definitions (valid by SPEC 8.5.5: every X is i32), n requests in sequence.
    internal static string OneValueChain(int n)
        => "contract CC\n    associate X\n    func a(self: ref/Self) -> Self.X\n" +
            string.Concat(Enumerable.Range(0, n).Select(static i => $"struct C{i}\n    Self is CC\n    public func a(self: ref/Self) -> C{i + 1}.(CC).X => $abort(\"a\")\n")) +
            $"struct C{n}\n    Self is CC\n    public func a(self: ref/Self) -> i32 => 0\n()";

    // R7-C4 (PLAN G172): a two-value ring of n definitions (valid by SPEC 8.3.3 Finality: every X and Y is i32), one component.
    internal static string TwoValueRing(int n)
        => "contract RC\n    associate X\n    associate Y\n    func a(self: ref/Self) -> Self.X\n    func b(self: ref/Self) -> Self.X\n" +
            "    func c(self: ref/Self) -> Self.Y\n    func d(self: ref/Self) -> Self.Y\n" +
            string.Concat(Enumerable.Range(0, n).Select(i => $"struct R{i}\n    Self is RC\n" +
                $"    public func a(self: ref/Self) -> R{(i + 1) % n}.(RC).X => $abort(\"a\")\n" +
                $"    public func b(self: ref/Self) -> R{(i + 1) % n}.(RC).Y => $abort(\"b\")\n" +
                "    public func c(self: ref/Self) -> i32 => 0\n" +
                $"    public func d(self: ref/Self) -> R{(i + 1) % n}.(RC).X => $abort(\"d\")\n")) + "()";

    // R7-C4 (C4 ruling 11): a two-value chain of n definitions (valid by SPEC 8.5.5: every X and Y is i32), the model's `two_chain`.
    internal static string TwoValueChain(int n)
        => "contract TC\n    associate X\n    associate Y\n    func a(self: ref/Self) -> Self.X\n    func b(self: ref/Self) -> Self.X\n" +
            "    func c(self: ref/Self) -> Self.Y\n    func d(self: ref/Self) -> Self.Y\n" +
            string.Concat(Enumerable.Range(0, n).Select(static i => $"struct C{i}\n    Self is TC\n" +
                $"    public func a(self: ref/Self) -> C{i + 1}.(TC).X => $abort(\"a\")\n" +
                $"    public func b(self: ref/Self) -> C{i + 1}.(TC).Y => $abort(\"b\")\n" +
                $"    public func c(self: ref/Self) -> C{i + 1}.(TC).Y => $abort(\"c\")\n" +
                "    public func d(self: ref/Self) -> i32 => 0\n")) +
            $"struct C{n}\n    Self is TC\n    public func a(self: ref/Self) -> i32 => 0\n    public func b(self: ref/Self) -> i32 => 0\n" +
            "    public func c(self: ref/Self) -> i32 => 0\n    public func d(self: ref/Self) -> i32 => 0\n()";

    // R7-C4 (C4 ruling 17 C2; design §6's scaling rows, the model's shapes): each valid by SPEC 8.5.5 / 8.3.3 Finality, every value i32.
    // `over`: the re-read's family (ov12 at 12), C_i.Y reading K.(KC).W.(CC).X with K.W = Z; acyclic in its real reads.
    internal static string Over(int n)
        => "contract CC\n    associate X\n    associate Y\n    func a(self: ref/Self) -> Self.X\n    func b(self: ref/Self) -> Self.X\n" +
            "    func c(self: ref/Self) -> Self.Y\n    func d(self: ref/Self) -> Self.Y\ncontract KC\n    associate W is CC\n    func w(self: ref/Self) -> Self.W\n" +
            "struct Z\n    Self is CC\n" + Constants("abcd") + "struct K\n    Self is KC\n    public func w(self: ref/Self) -> Z => $abort(\"w\")\n" +
            string.Concat(Enumerable.Range(0, n).Select(static i => $"struct C{i}\n    Self is CC\n" +
                $"    public func a(self: ref/Self) -> C{i + 1}.(CC).X => $abort(\"a\")\n" +
                $"    public func b(self: ref/Self) -> C{i + 1}.(CC).Y => $abort(\"b\")\n" +
                $"    public func c(self: ref/Self) -> C{i + 1}.(CC).Y => $abort(\"c\")\n" +
                "    public func d(self: ref/Self) -> K.(KC).W.(CC).X => $abort(\"d\")\n")) +
            $"struct C{n}\n    Self is CC\n" + Constants("abcd") + "()";

    // A diamond ladder of n rungs (Q20 B1 (1)'s shared shape): L_i.X from A_i.X and B_i.X, each from L_{i+1}.X; L_n's b returns `last`:
    // i32 (acyclic: 3n + 1 one-binding components) or L0.(LC).X (the back edge: one component).
    internal static string Ladder(int n, string last)
        => "contract LC\n    associate X\n    func a(self: ref/Self) -> Self.X\n    func b(self: ref/Self) -> Self.X\n" + Rungs(n) +
            $"struct L{n}\n    Self is LC\n    public func a(self: ref/Self) -> i32 => 0\n    public func b(self: ref/Self) -> {last} => $abort(\"b\")\n()";

    // The same ladder read by a requirement (R7-C2e2 e2-5b 3b3a-2, gen.py's L1 and L2): LC's `associate X is M`, R is M, and L_n's a and
    // b both return `last`, so each L_i is LC's requirement evidence reading its X through A_i's and B_i's, 2^n paths as a tree.
    internal static string RequirementLadder(int n, string last)
        => "contract M\nstruct R\n    Self is M\ncontract LC\n    associate X is M\n    func a(self: ref/Self) -> Self.X\n    func b(self: ref/Self) -> Self.X\n" +
            Rungs(n) + $"struct L{n}\n    Self is LC\n    public func a(self: ref/Self) -> {last} => $abort(\"a\")\n    public func b(self: ref/Self) -> {last} => $abort(\"b\")\n()";

    // A two-value chain of n whose last Y also reads C0.X (the model's `two_chain(n, back=True)`): one component.
    internal static string TwoValueChainBackEdge(int n)
        => TwoValueChain(n)[..^"    public func d(self: ref/Self) -> i32 => 0\n()".Length] + "    public func d(self: ref/Self) -> C0.(TC).X => $abort(\"d\")\n()";

    // The review's ring-9 probes (labels in `C4\review-rev3.md` §1): a third value Z of every Ri from two constant sources e and f (R0's
    // f returns `r0`: i64 a conflict), Z declared first or last in the contract.
    internal static string RingWithZ(bool zFirst, string r0)
        => "contract RC\n" + (zFirst ? "    associate Z\n" : string.Empty) + "    associate X\n    associate Y\n" + (zFirst ? string.Empty : "    associate Z\n") +
            "    func a(self: ref/Self) -> Self.X\n    func b(self: ref/Self) -> Self.X\n    func c(self: ref/Self) -> Self.Y\n    func d(self: ref/Self) -> Self.Y\n" +
            "    func e(self: ref/Self) -> Self.Z\n    func f(self: ref/Self) -> Self.Z\n" +
            string.Concat(Enumerable.Range(0, 9).Select(i => RingMember($"R{i}", $"R{(i + 1) % 9}", string.Empty) +
                $"    public func e(self: ref/Self) -> i32 => 0\n    public func f(self: ref/Self) -> {(i == 0 ? r0 : "i32")} => 0\n")) + "()";

    // A ring of 9 whose R5.X has two more constant sources, e: i32 and f: i64 (a conflict reading nothing in the component), e first or not.
    internal static string RingWithConflict(bool eFirst)
        => "contract RC\n    associate X\n    associate Y\n    func a(self: ref/Self) -> Self.X\n    func b(self: ref/Self) -> Self.X\n" +
            "    func c(self: ref/Self) -> Self.Y\n    func d(self: ref/Self) -> Self.Y\n    func e(self: ref/Self) -> Self.X\n    func f(self: ref/Self) -> Self.X\n" +
            string.Concat(Enumerable.Range(0, 9).Select(i => RingMember($"R{i}", $"R{(i + 1) % 9}", string.Empty) + (eFirst
                ? $"    public func e(self: ref/Self) -> i32 => 0\n    public func f(self: ref/Self) -> {(i == 5 ? "i64" : "i32")} => 0\n"
                : $"    public func f(self: ref/Self) -> {(i == 5 ? "i64" : "i32")} => 0\n    public func e(self: ref/Self) -> i32 => 0\n"))) + "()";

    // A ring of 9 whose two least simple names tie: GA.R0 -> GB.R0 -> R2 -> ... -> R8 -> GA.R0, GA or GB declared first.
    internal static string NestedRing(bool gaFirst)
    {
        var ga = "struct GA\n" + RingMember("R0", "GB.R0", "    ").Replace("    struct R0", "    public struct R0", StringComparison.Ordinal);
        var gb = "struct GB\n" + RingMember("R0", "R2", "    ").Replace("    struct R0", "    public struct R0", StringComparison.Ordinal);
        return "contract RC\n    associate X\n    associate Y\n    func a(self: ref/Self) -> Self.X\n    func b(self: ref/Self) -> Self.X\n" +
            "    func c(self: ref/Self) -> Self.Y\n    func d(self: ref/Self) -> Self.Y\n" + (gaFirst ? ga + gb : gb + ga) +
            string.Concat(Enumerable.Range(2, 7).Select(static i => RingMember($"R{i}", i == 8 ? "GA.R0" : $"R{i + 1}", string.Empty))) + "()";
    }

    internal static string Create(string axis, int size, bool explicitBinding)
    {
        var text = new StringBuilder();
        if (axis == "refinement")
        {
            text.Append("contract P\n    associate Item\n");
            for (var i = 0; i < size; i++)
            {
                text.Append($"contract C{i}: P\n    func read{i}() -> Self.Item\n");
            }

            text.Append("struct S\n    Self is P\n");
            for (var i = 0; i < size; i++)
            {
                text.Append($"    Self is C{i}\n");
            }

            if (explicitBinding)
            {
                text.Append("    associate P.Item is i32\n");
            }

            for (var i = 0; i < size; i++)
            {
                text.Append($"    public func read{i}() -> i32 => 42\n");
            }

            return text.ToString();
        }

        if (axis is "depth" or "shared")
        {
            var required = "Self.Item";
            var actual = "i32";
            for (var i = 0; i < size; i++)
            {
                required = axis == "depth" ? $"Box<{required}>" : $"({required}, {required})";
                actual = axis == "depth" ? $"Box<{actual}>" : $"({actual}, {actual})";
            }

            return "struct Box<T>\ncontract C\n    associate Item\n    func read() -> " + required + "\nstruct S\n    Self is C\n" +
                (explicitBinding ? "    associate C.Item is i32\n" : string.Empty) + "    public func read() -> " + actual + " => $abort(\"unused\")\n";
        }

        if (axis == "candidates")
        {
            for (var i = 0; i < size; i++)
            {
                text.Append($"struct Tag{i}\n");
            }

            text.Append("contract C\n    associate Item\n    func read(value: Tag0) -> Self.Item\nstruct S\n    Self is C\n");
            if (explicitBinding)
            {
                text.Append("    associate C.Item is i32\n");
            }

            for (var i = 0; i < size; i++)
            {
                text.Append($"    public func read(value: Tag{i}) -> i32 => 42\n");
            }

            return text.ToString();
        }

        text.Append("contract C\n    associate Item\n");
        var requirements = axis == "requirements" ? size : 1;
        for (var i = 0; i < requirements; i++)
        {
            text.Append($"    func read{i}() -> Self.Item\n");
        }

        var count = axis is "declarations" or "external" or "unrelated" ? size : 1;
        for (var n = count - 1; n >= 0; n--)
        {
            text.Append($"struct S{n}\n    Self is C\n");
            if (explicitBinding)
            {
                text.Append("    associate C.Item is i32\n");
            }

            var result = axis == "external" && n != 0 ? $"S{n - 1}.(C).Item" : "i32";
            for (var i = 0; i < requirements; i++)
            {
                text.Append($"    public func read{i}() -> {result} => 42\n");
            }
        }

        return text.ToString();
    }

    private static string Constants(string names)
        => string.Concat(names.Select(static x => $"    public func {x}(self: ref/Self) -> i32 => 0\n"));

    // A ladder's rungs L_0 .. L_{n-1}: L_i.X from A_i.X and B_i.X, each from L_{i+1}.X.
    private static string Rungs(int n)
        => string.Concat(Enumerable.Range(0, n).Select(static i => $"struct L{i}\n    Self is LC\n" +
            $"    public func a(self: ref/Self) -> A{i}.(LC).X => $abort(\"a\")\n    public func b(self: ref/Self) -> B{i}.(LC).X => $abort(\"b\")\n" +
            $"struct A{i}\n    Self is LC\n" + Reads($"L{i + 1}") + $"struct B{i}\n    Self is LC\n" + Reads($"L{i + 1}")));

    private static string Reads(string next)
        => $"    public func a(self: ref/Self) -> {next}.(LC).X => $abort(\"a\")\n    public func b(self: ref/Self) -> {next}.(LC).X => $abort(\"b\")\n";

    private static string RingMember(string name, string next, string indent)
        => $"{indent}struct {name}\n{indent}    Self is RC\n" +
            $"{indent}    public func a(self: ref/Self) -> {next}.(RC).X => $abort(\"a\")\n" +
            $"{indent}    public func b(self: ref/Self) -> {next}.(RC).Y => $abort(\"b\")\n" +
            $"{indent}    public func c(self: ref/Self) -> i32 => 0\n" +
            $"{indent}    public func d(self: ref/Self) -> {next}.(RC).X => $abort(\"d\")\n";
}
