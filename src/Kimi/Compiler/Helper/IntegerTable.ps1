# The write half of the constant integer tables (IntegerTable.cs beside this file; DECISIONS R5-M179): the one writer of the byte form
# IntegerTable<T> reads, dot-sourced by each generator of such a table (GenerateNameUnicode.ps1, GenerateIdentifierUnicode.ps1,
# ../../Documentation/GenerateMarkdownUnicode.ps1). Input: the builder of the C# class body, the table's name, its values in order and
# the element width in bits (32 for IntegerTable<int>, 64 for <ulong>); each value is in the element type's range and typed at least
# that wide (int or UInt64), since PowerShell masks a shift count to its operand's width. Output: the property
# `internal static ReadOnlySpan<byte> <name> =>` and its collection expression of each value's bytes, little-endian, in decimal, 24 a
# line, each followed by a comma; lines end in the builder's terminator. The file's header, its class, the space between tables and its
# line endings stay each generator's.
function Add-IntegerTable([Text.StringBuilder]$builder, [string]$name, $values, [int]$width) {
    $bytes = foreach ($value in $values) { for ($shift = 0; $shift -lt $width; $shift += 8) { ($value -shr $shift) -band 0xFF } }
    [void]$builder.AppendLine("    internal static ReadOnlySpan<byte> $name =>")
    [void]$builder.AppendLine('    [')
    for ($i = 0; $i -lt $bytes.Count; $i += 24) {
        $entries = for ($j = $i; $j -lt [Math]::Min($i + 24, $bytes.Count); $j++) { $bytes[$j] }
        [void]$builder.AppendLine('        ' + ($entries -join ', ') + ',')
    }
    [void]$builder.AppendLine('    ];')
}
