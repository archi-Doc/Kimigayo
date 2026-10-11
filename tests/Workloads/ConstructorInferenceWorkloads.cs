// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Verification;

// Shared, deterministic input generation; repetition for timing lives only in Benchmark.
internal static class ConstructorInferenceWorkloads
{
    internal static string Create(string axis, int size, bool explicitType = false, bool reverse = false)
    {
        if (axis == "candidates")
        {
            var text = new StringBuilder();
            for (var i = 0; i < size; i++)
            {
                text.Append($"struct Tag{i}\n    public init() => ()\n");
            }

            text.Append("struct Box<T>\n");
            for (var i = 0; i < size; i++)
            {
                var tag = reverse ? size - i - 1 : i;
                text.Append($"    public init(value: T, tag: Tag{tag}) => ()\n");
            }

            text.Append("let n: i32 = 42\nlet tag = Tag0.init()\nlet value = Box");
            text.Append(explicitType ? "<i32>" : string.Empty);
            return text.Append(".init(n, tag@move)\n").ToString();
        }

        if (axis == "slots")
        {
            var slots = string.Join(", ", Enumerable.Range(0, size).Select(i => $"T{i}"));
            var parameters = string.Join(", ", Enumerable.Range(0, size).Select(i => $"p{i}: T{i}"));
            var arguments = string.Join(", ", Enumerable.Repeat("n", size));
            var types = explicitType ? "<" + string.Join(", ", Enumerable.Repeat("i32", size)) + ">" : string.Empty;
            return $"struct Box<{slots}>\n    public init({parameters}) => ()\nlet n: i32 = 42\nlet result = Box{types}.init({arguments})";
        }

        const string box = "struct Box<T>\n    public init(value: T) => ()\n";
        if (axis == "depth")
        {
            var type = "i32";
            var value = "n";
            for (var i = 0; i < size; i++)
            {
                type = "(" + type + ", bool)";
                value = "(" + value + ", true)";
            }

            return box + "let n: i32 = 42\nlet input = " + value + "\nlet result = Box" + (explicitType ? "<" + type + ">" : string.Empty) + ".init(input)";
        }

        if (axis == "calls")
        {
            return box + "let n: i32 = 42\n" + string.Concat(Enumerable.Range(0, size).Select(i => $"let v{i} = Box{(explicitType ? "<i32>" : string.Empty)}.init(n)\n"));
        }

        return axis switch
        {
            "ambiguous" => "struct Box<T>\n    public init(value: T, extra: i32 = 0) => ()\n    public init(value: T, flag: bool = false) => ()\nlet b = Box.init(42)",
            "unbound" => "struct Box<T>\n    public init() => ()\nlet b = Box.init()",
            "conflict" => "struct Box<T>\n    public init(first: T, second: T) => ()\nlet n: i32 = 1\nlet b = Box.init(n, true)",
            "correlation" => "struct C<T>\n    public init(value: (T, i32), code: i32) => ()\n    public init(value: ref/(i64, T), name: string) => ()\nlet x: (i64, i32) = (1, 2)\nlet c = C.init(x, 0)",
            "selection" => "struct C<T>\n    public init(x: ref/i32, y: ref/T) => ()\n    public init(x: ref/T, y: ref/T) => ()\nlet n: i32 = 42\nlet r = n@ref\nlet c = C.init(r@ref, n@ref)",
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };
    }
}
