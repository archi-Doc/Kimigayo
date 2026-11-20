# The build identity of tatara.dll: one rule, dot-sourced by build.ps1 and by every script that compares a DLL with a checkout.
# SHA-256 (lower-case hex) over every .h, .cpp and .ps1 file under src/Tatara (hidden and system files included), recursively, in
# StringComparer.Ordinal order (UTF-16 code units) of the path relative to src/Tatara written with '/'; for each file the path's
# UTF-8 bytes, a NUL, then the file's bytes.
function Get-TataraBuildIdentity {
    $inputs = [Collections.Generic.SortedDictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File -Force | Where-Object { $_.Extension -cin '.h', '.cpp', '.ps1' }) {
        $inputs.Add([IO.Path]::GetRelativePath($PSScriptRoot, $file.FullName).Replace('\', '/'), $file.FullName)
    }
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    foreach ($entry in $inputs.GetEnumerator()) {
        $hash.AppendData([Text.Encoding]::UTF8.GetBytes($entry.Key + [char]0))
        $hash.AppendData([IO.File]::ReadAllBytes($entry.Value))
    }
    return [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
}
