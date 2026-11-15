# Test-KimiExpectedOutput owns the rule "a program's output equals its expectation": ordinal and byte-exact.
# Responsibility: equality of a whole captured stream, of a stream's suffix (the caller passes the last N captured bytes,
# as [Linq.Enumerable]::TakeLast gives them) or of a written file with its expected bytes.
# Inputs: two byte[] values. Expected bytes are a sidecar's or a file's raw bytes or the UTF-8 bytes of an expected text;
# actual bytes are a capture (BaseStream.CopyToAsync) or a file's ReadAllBytes.
# Output: $true exactly when both arrays have the same length and the same bytes, else $false.
# Invariants: every such equality check calls this function; any argument that is not a byte[], $null included, is
# rejected with an ArgumentException (no implicit conversion). Presence checks in output mixed with build lines (ordinal
# Contains, -match) are a different rule and stay outside.
# Prohibitions: no comparison of decoded text (-ceq/-cne/-cin and String.EndsWith(string) are culture-sensitive: they
# ignore a NUL and equate normalization forms), no decoding that drops or replaces bytes or a BOM, no Trim.
function Test-KimiExpectedOutput($Expected, $Actual) {
    if ($Expected -isnot [byte[]] -or $Actual -isnot [byte[]]) {
        throw [ArgumentException]::new('Test-KimiExpectedOutput compares two byte[] values.')
    }
    [Linq.Enumerable]::SequenceEqual([byte[]]$Expected, [byte[]]$Actual)
}
