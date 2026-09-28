[CmdletBinding()]
param([Parameter(Mandatory)] [string] $CompilerPath)
$ErrorActionPreference = 'Stop'
$compiler = [IO.Path]::GetFullPath($CompilerPath)
$managed = [IO.Path]::GetExtension($compiler) -eq '.dll'
$start = [Diagnostics.ProcessStartInfo]::new($(if ($managed) { (Get-Command dotnet).Source } else { $compiler }))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
if ($managed) { $start.ArgumentList.Add($compiler) }
$start.ArgumentList.Add('lsp')
$process = [Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
$messages = @(
    '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}',
    '{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///test.kimi","version":1,"text":"var x = 1"}}}',
    '{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///test.kimi","version":2},"contentChanges":[{"text":"var x = 2"}]}}',
    '{"jsonrpc":"2.0","method":"textDocument/didClose","params":{"textDocument":{"uri":"file:///test.kimi"}}}',
    '{"jsonrpc":"2.0","id":"unknown","method":"not-supported"}',
    '{"jsonrpc":"2.0","id":2,"method":"shutdown"}',
    '{"jsonrpc":"2.0","method":"exit"}'
)
foreach ($message in $messages) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($message)
    $header = [Text.Encoding]::ASCII.GetBytes("Content-Length: $($bytes.Length)`r`n`r`n")
    $process.StandardInput.BaseStream.Write($header)
    $process.StandardInput.BaseStream.Write($bytes)
}
$process.StandardInput.BaseStream.Flush()
if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'LSP timed out' }
$output = $stdout.GetAwaiter().GetResult()
$errors = $stderr.GetAwaiter().GetResult()
if ($process.ExitCode -ne 0 -or $errors) { throw "LSP failed: $errors" }
$responses = @()
while ($output.Length -gt 0) {
    $boundary = $output.IndexOf("`r`n`r`n")
    if ($boundary -lt 0 -or $output.Substring(0, $boundary) -notmatch '^Content-Length: (\d+)$') { throw "Invalid LSP header: $output" }
    $length = [int]$Matches[1]
    $responses += $output.Substring($boundary + 4, $length) | ConvertFrom-Json
    $output = $output.Substring($boundary + 4 + $length)
}
if ($responses.Count -ne 4 -or $responses[0].result.capabilities.textDocumentSync.change -ne 2 -or
    $responses[1].method -cne 'textDocument/publishDiagnostics' -or $responses[1].params.uri -cne 'file:///test.kimi' -or
    $responses[2].id -cne 'unknown' -or $responses[2].error.code -ne -32601 -or $responses[3].id -ne 2) { throw 'LSP response mismatch' }
$process.Dispose()
Write-Output 'LSP initialization, document notifications, error response and shutdown passed.'
