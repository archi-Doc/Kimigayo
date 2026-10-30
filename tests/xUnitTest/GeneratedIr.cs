// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Xunit;

namespace XunitTest;

/// <summary>A generated module's LLVM text read for generated-code requirement tests (R5 E15; DECISIONS R5-M12 class R), the one helper
/// through which a requirement holds for any correct code generation. A test selects a function by its signature, its
/// startup role or a runtime contract symbol, a block by position, terminator or successor, a call by its callee and an instruction by its
/// <see cref="IrValue.Shape"/>, in which every value, label and symbol is abstracted, and relates a value to its definition: it never names a
/// generated function, a value, a label or a generator's helper.</summary>
internal sealed partial class GeneratedIr
{
    private readonly HashSet<string> types = []; // Named types, which a shape keeps (runtime layouts such as %kimi.string).
    private readonly Dictionary<string, List<string>> groups = []; // Attribute groups by #N.
    private readonly Dictionary<string, IrFunction> symbols = [];
    private readonly List<string> metadata = [];
    private readonly List<(IrValue Call, string Symbol)> calls = [];
    private readonly Dictionary<string, IrValue> values = []; // Of the function being read, as are blocks and uses.
    private readonly Dictionary<string, IrBlock> blocks = [];
    private readonly List<(IrBlock Block, IrValue Value, string[] Names)> uses = [];

    private GeneratedIr()
    {
    }

    /// <summary>Gets the functions, defined and declared, in text order.</summary>
    public List<IrFunction> Functions { get; } = [];

    /// <summary>Gets the startup body: the function <c>__kimi_start</c> calls first.</summary>
    public IrFunction Startup => Assert.IsType<IrFunction>(this.Function("__kimi_start").Instructions.First(static x => x.Opcode == "call").Callee);

    /// <summary>Reads a module's text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The module.</returns>
    public static GeneratedIr Parse(string text)
    {
        var ir = new GeneratedIr();
        var lines = text.Split('\n');
        foreach (var line in lines)
        {
            ir.Declare(line);
        }

        IrFunction? function = null;
        var pending = string.Empty; // An instruction continued on the next line (a switch's cases).
        foreach (var raw in lines)
        {
            var comment = raw.IndexOf(';');
            var line = (comment < 0 ? raw : raw[..comment]).Trim();
            if (function is null)
            {
                function = ir.Header(line);
            }
            else if (line == "}")
            {
                ir.Resolve(function);
                function = null;
            }
            else if (line.Length != 0)
            {
                pending = pending.Length == 0 ? line : pending + " " + line;
                if (pending.Count(static x => x == '[') == pending.Count(static x => x == ']'))
                {
                    ir.Read(function, pending);
                    pending = string.Empty;
                }
            }
        }

        foreach (var (call, symbol) in ir.calls)
        {
            call.Callee = ir.Function(symbol);
        }

        return ir;
    }

    /// <summary>Gets the function of a runtime contract symbol: <c>__kimi_start</c>, <c>__kimi_exit</c>, an import or an LLVM intrinsic,
    /// never a generated function or a generator's helper.</summary>
    /// <param name="symbol">The symbol without <c>@</c>.</param>
    /// <returns>The function.</returns>
    public IrFunction Function(string symbol) => Assert.Contains(symbol, this.symbols);

    /// <summary>Gets the one function with a signature.</summary>
    /// <param name="signature">The <see cref="IrFunction.Signature"/>.</param>
    /// <returns>The function.</returns>
    public IrFunction Single(string signature) => Assert.Single(this.Functions, x => x.Signature == signature);

    /// <summary>Asserts that nothing claims termination (IMPL 21.3.1: optimization never changes nontermination; LLVM_OPTIMIZATION D03 and
    /// D04): no <c>mustprogress</c> or <c>willreturn</c> on a function or a call, attribute groups resolved, and no
    /// <c>llvm.loop.mustprogress</c>.</summary>
    public void AssertNoTerminationClaim()
    {
        foreach (var function in this.Functions)
        {
            Assert.DoesNotContain(function.Attributes, IsTerminationClaim);
            foreach (var instruction in function.Instructions)
            {
                Assert.DoesNotContain(instruction.Attributes, IsTerminationClaim);
            }
        }

        Assert.DoesNotContain(this.metadata, static x => x.Contains("llvm.loop.mustprogress", StringComparison.Ordinal));
    }

    private static bool IsTerminationClaim(string attribute) => attribute is "mustprogress" or "willreturn";

    // The parts of a list at depth 0: outside quotes, parentheses, brackets and braces.
    private static List<string> Split(string text, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var quoted = false;
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            var x = i < text.Length ? text[i] : separator;
            quoted ^= x == '"';
            depth += quoted ? 0 : x is '(' or '[' or '{' ? 1 : x is ')' or ']' or '}' ? -1 : 0;
            if (!quoted && depth == 0 && x == separator)
            {
                if (text[start..i].Trim() is { Length: > 0 } part)
                {
                    parts.Add(part);
                }

                start = i + 1;
            }
        }

        return parts;
    }

    // The index of the parenthesis that closes the one at open.
    private static int Close(string text, int open)
    {
        for (int i = open, depth = 0; i < text.Length; i++)
        {
            depth += text[i] == '(' ? 1 : text[i] == ')' ? -1 : 0;
            if (depth == 0)
            {
                return i;
            }
        }

        throw new FormatException("Unbalanced parentheses: " + text);
    }

    [GeneratedRegex(@"[%@][-\w$.]+")]
    private static partial Regex Name();

    [GeneratedRegex(@"^(%[-\w$.]+) = ")]
    private static partial Regex Result();

    [GeneratedRegex(@"^([-\w$.]+):$")]
    private static partial Regex Label();

    [GeneratedRegex(@"([@%][-\w$.]+)\(")]
    private static partial Regex Callee();

    [GeneratedRegex(@"\s(%[-\w$.]+)$")]
    private static partial Regex ParameterName();

    [GeneratedRegex(@"^(%[-\w$.]+) = type ")]
    private static partial Regex NamedType();

    [GeneratedRegex(@"^attributes (#\d+) = \{(.*)\}$")]
    private static partial Regex Group();

    // A module-level fact a function line needs first: a named type, an attribute group or metadata.
    private void Declare(string line)
    {
        if (NamedType().Match(line) is { Success: true } type)
        {
            this.types.Add(type.Groups[1].Value);
        }
        else if (Group().Match(line) is { Success: true } group)
        {
            this.groups.Add(group.Groups[1].Value, Split(group.Groups[2].Value, ' '));
        }
        else if (line.StartsWith('!'))
        {
            this.metadata.Add(line);
        }
    }

    // A define or declare line, or null for any other; the function, whose body follows, for a define.
    private IrFunction? Header(string line)
    {
        var defined = line.StartsWith("define ", StringComparison.Ordinal);
        if (!defined && !line.StartsWith("declare ", StringComparison.Ordinal))
        {
            return null;
        }

        this.values.Clear();
        this.blocks.Clear();
        this.uses.Clear();
        var at = line.IndexOf('@');
        var open = line.IndexOf('(', at);
        var close = Close(line, open);
        var parameters = new List<IrValue>();
        foreach (var parameter in Split(line[(open + 1)..close], ','))
        {
            var name = ParameterName().Match(parameter);
            parameters.Add(new("argument", name.Success ? parameter[..name.Index] : parameter));
            if (name.Success)
            {
                this.values.Add(name.Groups[1].Value, parameters[^1]);
            }
        }

        var signature = $"{line[(line.IndexOf(' ') + 1)..at].Trim()} ({string.Join(", ", parameters)})";
        var function = new IrFunction(signature, this.Attributes(line[(close + 1)..].TrimEnd('{', ' ')), parameters);
        this.Functions.Add(function);
        this.symbols.Add(line[(at + 1)..open], function);
        return defined ? function : null;
    }

    // A label or an instruction of the function.
    private void Read(IrFunction function, string line)
    {
        if (Label().Match(line) is { Success: true } label)
        {
            function.Blocks.Add(this.blocks['%' + label.Groups[1].Value] = new(label.Groups[1].Value));
            return;
        }

        Assert.True(function.Blocks.Count != 0, "Code generation labels every block: " + line);
        var result = Result().Match(line);
        var text = line[result.Length..];
        var words = text.Split(' ', 3);
        var opcode = words[0] is "tail" or "musttail" or "notail" ? words[1] : words[0];
        var callee = opcode == "call" ? Callee().Match(text) : Match.Empty;
        Assert.True(callee.Success || opcode != "call", text);
        var end = callee.Success ? Close(text, callee.Index + callee.Length - 1) + 1 : text.Length; // A call's attributes follow its shape.
        var value = new IrValue(opcode, Name().Replace(text[..end], x => x.Value[0] == '%' && this.types.Contains(x.Value) ? x.Value : x.Value[..1]));
        value.Attributes.AddRange(this.Attributes(text[end..]));
        function.Blocks[^1].Instructions.Add(value);
        function.Instructions.Add(value);
        if (result.Success)
        {
            this.values.Add(result.Groups[1].Value, value);
        }

        var names = Name().Matches(text[..end]).Select(static x => x.Value).Where(x => x[0] == '%' && !this.types.Contains(x));
        this.uses.Add((function.Blocks[^1], value, [.. names]));
        if (callee.Success && callee.Value[0] == '@')
        {
            this.calls.Add((value, callee.Groups[1].Value[1..]));
        }
    }

    // Each read name is a value's definition, a label (a successor when the terminator reads it) or undefined, which fails.
    private void Resolve(IrFunction function)
    {
        foreach (var (block, value, names) in this.uses)
        {
            foreach (var name in names)
            {
                if (this.values.TryGetValue(name, out var definition))
                {
                    value.Operands.Add(definition);
                }
                else if (!this.blocks.TryGetValue(name, out var target))
                {
                    Assert.Fail($"{name} is not defined in {function}: {value}");
                }
                else if (value == block.Terminator)
                {
                    block.Successors.Add(target);
                }
            }
        }
    }

    private List<string> Attributes(string text)
    {
        var attributes = new List<string>();
        foreach (var token in Split(text, ' '))
        {
            if (this.groups.TryGetValue(token, out var group))
            {
                attributes.AddRange(group);
            }
            else if (token[0] == '#')
            {
                Assert.Fail($"Attribute group {token} is not defined.");
            }
            else
            {
                attributes.Add(token);
            }
        }

        return attributes;
    }
}

/// <summary>A function of a <see cref="GeneratedIr"/>, defined or declared.</summary>
/// <param name="signature">Its signature.</param>
/// <param name="attributes">Its attributes.</param>
/// <param name="parameters">Its parameters.</param>
internal sealed class IrFunction(string signature, List<string> attributes, List<IrValue> parameters)
{
    /// <summary>Gets the signature: linkage, return type and parameter types with their attributes, names dropped (for example
    /// <c>internal i1 (i1, ptr noundef nonnull align 8 dereferenceable(24))</c>).</summary>
    public string Signature => signature;

    /// <summary>Gets the function attributes, attribute groups resolved.</summary>
    public List<string> Attributes => attributes;

    /// <summary>Gets the parameters, whose <see cref="IrValue.Shape"/> is a type and its attributes.</summary>
    public List<IrValue> Parameters => parameters;

    /// <summary>Gets the blocks in text order, the entry first; a declaration has none.</summary>
    public List<IrBlock> Blocks { get; } = [];

    /// <summary>Gets the instructions of every block in text order.</summary>
    public List<IrValue> Instructions { get; } = [];

    /// <inheritdoc/>
    public override string ToString() => signature;
}

/// <summary>A basic block of an <see cref="IrFunction"/>.</summary>
/// <param name="label">Its label.</param>
internal sealed class IrBlock(string label)
{
    /// <summary>Gets the label as written, which only a generator's own contract test reads (FunctionCodegen I3 labels MIR block k
    /// <c>b&lt;k&gt;</c>); a requirement test relates blocks by <see cref="Successors"/>.</summary>
    public string Label => label;

    /// <summary>Gets the instructions, the terminator last.</summary>
    public List<IrValue> Instructions { get; } = [];

    /// <summary>Gets the terminator.</summary>
    public IrValue Terminator => this.Instructions[^1];

    /// <summary>Gets the blocks the terminator branches to, in operand order.</summary>
    public List<IrBlock> Successors { get; } = [];
}

/// <summary>A parameter or an instruction of an <see cref="IrFunction"/>.</summary>
/// <param name="opcode">Its opcode.</param>
/// <param name="shape">Its shape.</param>
internal sealed class IrValue(string opcode, string shape)
{
    /// <summary>Gets the opcode: <c>argument</c> for a parameter, <c>call</c> for a tail call too.</summary>
    public string Opcode => opcode;

    /// <summary>Gets the text with every value and label abstracted to <c>%</c> and every symbol to <c>@</c>, named types kept (for example
    /// <c>store i8 -1, ptr %, align 1</c>), a call's without its <see cref="Attributes"/>; a parameter's is its type and attributes.</summary>
    public string Shape => shape;

    /// <summary>Gets the definitions of the values it reads, instructions or parameters, in operand order (labels excluded).</summary>
    public List<IrValue> Operands { get; } = [];

    /// <summary>Gets or sets the function a direct call calls.</summary>
    public IrFunction? Callee { get; set; }

    /// <summary>Gets a call's function attributes, attribute groups resolved.</summary>
    public List<string> Attributes { get; } = [];

    /// <inheritdoc/>
    public override string ToString() => shape;
}
