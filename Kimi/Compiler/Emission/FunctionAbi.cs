// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Kimi.Compiler;

/// <summary>A physical signature shared by definitions and calls (SPEC 21.4.2). ccc is LLVM's default; identity is by reference.</summary>
internal sealed class FunctionAbi(string name, string result, AbiParameter[] parameters, bool noReturn = false, bool resultSlot = false)
{
    private string? internalDefinition;
    private string? exportedDefinition;

    internal string Name { get; } = name;

    internal string Result { get; } = result;

    internal AbiParameter[] Parameters { get; } = parameters;

    internal bool NoReturn { get; } = noReturn;

    internal bool ResultSlot { get; } = resultSlot;

    // Passing mode and attributes are fixed by the implemented representation in this profile.
    // The pool caches physical shapes; current call plans separately validate complete Types and Origins.
    internal static bool Supports(BoundType? type, AggregateLayoutPool? layouts = null) => type is not null && !ReferenceTypes.IsString(type) && GetValue(type, layouts) is not null;

    internal static bool SupportsParameter(BoundType? type, AggregateLayoutPool? layouts = null) => Supports(type, layouts) || ReferenceTypes.IsString(type);

    internal static ValueLowering? GetValue(BoundType type, AggregateLayoutPool? layouts) => type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Function || StructStorage.IsStruct(type) || EnumStorage.IsEnum(type)
        ? layouts?.Get(type)?.Value : ReferenceTypes.IsValue(type) || ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String) || ReferenceTypes.IsString(type)
            ? WindowsLowering.GetValue(type) : null;

    internal static bool HasResultSlot(BoundType type, AggregateLayoutPool? layouts) => SlotTypes.IsResult(type) && GetValue(type, layouts)?.Layout.Size > 0;

    internal static string? ResultType(BoundType type, AggregateLayoutPool? layouts = null) => ReferenceEquals(type, BoundType.Never)
        ? "void" : GetValue(type, layouts) is { } value ? SlotTypes.IsResult(type) ? "void" : value.ComputationType : null;

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

            text.Append(this.Parameters[i].Type).Append(this.Parameters[i].Attributes).Append(" %").Append(this.Parameters[i].Name);
        }

        return text.Append(this.NoReturn ? ") noreturn #0 {\n" : ") #0 {\n").ToString();
    }
}
