# A program's output equals its expectation only when their bytes are equal: ordinal and byte-exact.
# Inputs: the expected bytes (a sidecar's raw bytes or the UTF-8 bytes of an expected text) and the captured bytes.
# Invariant: every check of captured program output, or of a written file, against its expectation calls this function.
# Prohibitions: no comparison of decoded text (PowerShell -ceq/-cne/-cin and String.EndsWith are culture-sensitive: they
# ignore a NUL and equate normalization forms), no decoding that drops or replaces bytes or a BOM, no Trim.
function Test-KimiExpectedOutput([byte[]] $Expected, [byte[]] $Actual) {
    [Linq.Enumerable]::SequenceEqual($Expected, $Actual)
}
