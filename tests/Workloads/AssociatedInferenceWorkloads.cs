// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Verification;

internal static class AssociatedInferenceWorkloads
{
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
