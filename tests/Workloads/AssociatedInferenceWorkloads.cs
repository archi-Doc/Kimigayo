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
}
