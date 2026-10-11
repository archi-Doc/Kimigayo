// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Verification;

// Identical recurrence and input values; only the arithmetic/call representation changes.
internal static class ArithmeticWorkloads
{
    internal const int NativeIterations = 16_777_216;
    internal static readonly string[] Names = ["NumericOperator", "NumericWitness", "UserOperator", "UserNamed", "RequirementItem", "FunctionItem", "RequirementCallable", "FunctionCallable", "RequirementErased", "FunctionErased"];

    internal static string Create(string name, int iterations = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
        if (!Names.Contains(name, StringComparer.Ordinal))
        {
            throw new ArgumentException("Unknown arithmetic workload.", nameof(name));
        }

        const string Numeric = "Wrapping<u32>";
        var user = name.StartsWith("User", StringComparison.Ordinal);
        var requirement = name.StartsWith("Requirement", StringComparison.Ordinal) || name == "NumericWitness";
        var operand = requirement ? "T" : Numeric;
        var declarations = user ? "struct Value\n    Self is Addable<Self>\n    associate Output is Self\n    public var value: Wrapping<u32>\n    public init(value: Wrapping<u32>) => self.value = value\n    public func added(self: ref/Self, right: ref/Self) -> Self => Self.init(self.value + right.value)\n    drop => ()\n" : string.Empty;
        var operation = user ? name == "UserOperator" ? "state + delta" : "state.added(delta)" : "state + delta";
        if (!user && name != "NumericOperator")
        {
            if (!requirement)
            {
                declarations += "func named(left: ref/" + Numeric + ", right: ref/" + Numeric + ") -> " + Numeric + " => left + right\n";
            }

            var callable = name.EndsWith("Callable", StringComparison.Ordinal);
            if (callable)
            {
                declarations += "func apply<F, T>(operation: ref/F, left: ref/T, right: ref/T) -> T\n    F is Callable<(ref/T, ref/T) -> T>\n    return operation(left, right)\n";
            }

            declarations += "func step" + (requirement ? "<T>" : string.Empty) + "(left: ref/" + operand + ", right: ref/" + operand + ") -> " + operand + "\n";
            if (requirement)
            {
                declarations += "    T is Addable<T> and Owned\n    T.(Addable<T>).Output is T\n";
            }

            if (name == "NumericWitness")
            {
                declarations += "    return left.added(right)\n";
            }
            else
            {
                var annotation = name.EndsWith("Erased", StringComparison.Ordinal) ? ": (ref/" + operand + ", ref/" + operand + ") -> " + operand : string.Empty;
                declarations += "    let operation" + annotation + " = " + (requirement ? "T.added" : "named") + "\n    return " + (callable ? "apply(operation, left, right)" : "operation(left, right)") + "\n";
            }

            operation = "step(state, delta)";
        }

        var state = user ? "state.value" : "state";
        var initializer = user ? "Value.init(1@Wrapping<u32>)" : "1@Wrapping<u32>";
        var delta = user ? "Value.init(17@Wrapping<u32>)" : "17@Wrapping<u32>";
        return declarations + "var state = " + initializer + "\nlet delta = " + delta + "\nvar index = 0\nwhile index < " + iterations + "\n    state = " + operation +
            "\n    " + state + " = (" + state + " << 5) ^ (" + state + " >> 3)\n    index += 1\nrequire " + state + "@u32 == " + Expected(iterations) + " else => $abort(\"checksum\")\nConsole.writeLine(\"ok\")\n";
    }

    private static uint Expected(int iterations)
    {
        uint state = 1;
        for (var i = 0; i < iterations; i++)
        {
            state = unchecked(state + 17);
            state = (state << 5) ^ (state >> 3);
        }

        return state;
    }
}
