// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Kimi.Compiler;

/// <summary>A physical signature shared by definitions and calls (SPEC 21.4.2). ccc is LLVM's default; identity is by reference.</summary>
internal sealed class FunctionAbi(string name, string result, AbiParameter[] parameters, bool noReturn = false)
{
    private string? internalDefinition;
    private string? exportedDefinition;

    internal string Name { get; } = name;

    internal string Result { get; } = result;

    internal AbiParameter[] Parameters { get; } = parameters;

    internal bool NoReturn { get; } = noReturn;

    /// <summary>Gets the cached <c>define ... {</c> line, including the profile attribute group.</summary>
    /// <param name="exported">Whether the definition has external linkage instead of internal.</param>
    /// <returns>The definition header text.</returns>
    internal string GetDefinition(bool exported)
        => exported ? this.exportedDefinition ??= this.CreateDefinition(true) : this.internalDefinition ??= this.CreateDefinition(false);

    private string CreateDefinition(bool exported)
    {
        var text = new StringBuilder();
        text.Append(exported ? "define " : "define internal ").Append(this.Result).Append(" @").Append(this.Name).Append('(');
        for (var i = 0; i < this.Parameters.Length; i++)
        {
            if (i != 0)
            {
                text.Append(", ");
            }

            text.Append(this.Parameters[i].Type).Append(" %").Append(this.Parameters[i].Name);
        }

        return text.Append(this.NoReturn ? ") noreturn #0 {\n" : ") #0 {\n").ToString();
    }
}
