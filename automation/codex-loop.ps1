[CmdletBinding()]
param(
    # Repository root
    [string]$ProjectRoot = (Get-Location).Path,

    # Maximum number of Implementation Codex runs
    [int]$MaxImplementationRuns = 1,

    # Run Plan Audit after this many implementation runs
    [int]$AuditInterval = 4,

    # Run Plan Audit after this many consecutive Codex failures
    [int]$FailureAuditThreshold = 2,

    # Prompt files
    [string]$ImplementationPrompt = "automation/implementation-prompt.md",
    [string]$PlanAuditPrompt = "automation/plan-audit-prompt.md",
    [string]$CompletionAuditPrompt = "automation/completion-audit-prompt.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ============================================================
# Initialization
# ============================================================

$ProjectRoot = (Resolve-Path $ProjectRoot).Path

$StateDir = Join-Path $ProjectRoot ".codex-loop"
$SchemaDir = Join-Path $StateDir "schemas"
$LogDir = Join-Path $StateDir "logs"

New-Item -ItemType Directory -Force -Path $StateDir | Out-Null
New-Item -ItemType Directory -Force -Path $SchemaDir | Out-Null
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null


function Resolve-ProjectPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $ProjectRoot $Path
}


$ImplementationPrompt = Resolve-ProjectPath $ImplementationPrompt
$PlanAuditPrompt = Resolve-ProjectPath $PlanAuditPrompt
$CompletionAuditPrompt = Resolve-ProjectPath $CompletionAuditPrompt


# ============================================================
# Prerequisite checks
# ============================================================

if (-not (Get-Command "codex" -ErrorAction SilentlyContinue)) {
    throw "codex command was not found in PATH."
}

foreach ($file in @(
    $ImplementationPrompt,
    $PlanAuditPrompt,
    $CompletionAuditPrompt
)) {
    if (-not (Test-Path $file)) {
        throw "Required prompt file not found: $file"
    }
}


# ============================================================
# JSON Schemas
# ============================================================

$ImplementationSchema = Join-Path $SchemaDir "implementation.schema.json"
$PlanAuditSchema = Join-Path $SchemaDir "plan-audit.schema.json"
$CompletionAuditSchema = Join-Path $SchemaDir "completion-audit.schema.json"


@'
{
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "status": {
      "type": "string",
      "enum": [
        "continue",
        "complete",
        "blocked"
      ]
    },
    "milestone_completed": {
      "type": "boolean"
    },
    "milestone": {
      "type": ["string", "null"]
    },
    "summary": {
      "type": "string"
    },
    "next_task": {
      "type": ["string", "null"]
    },
    "needs_plan_audit": {
      "type": "boolean"
    }
  },
  "required": [
    "status",
    "milestone_completed",
    "milestone",
    "summary",
    "next_task",
    "needs_plan_audit"
  ]
}
'@ | Set-Content -Encoding UTF8 $ImplementationSchema


@'
{
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "status": {
      "type": "string",
      "enum": [
        "plan_ok",
        "plan_updated",
        "blocked"
      ]
    },
    "summary": {
      "type": "string"
    }
  },
  "required": [
    "status",
    "summary"
  ]
}
'@ | Set-Content -Encoding UTF8 $PlanAuditSchema


@'
{
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "status": {
      "type": "string",
      "enum": [
        "verified_complete",
        "not_complete",
        "blocked"
      ]
    },
    "summary": {
      "type": "string"
    },
    "plan_updated": {
      "type": "boolean"
    }
  },
  "required": [
    "status",
    "summary",
    "plan_updated"
  ]
}
'@ | Set-Content -Encoding UTF8 $CompletionAuditSchema


# ============================================================
# Codex execution
# ============================================================

function Invoke-CodexRun {
    param(
        [Parameter(Mandatory)]
        [string]$Kind,

        [Parameter(Mandatory)]
        [string]$PromptPath,

        [Parameter(Mandatory)]
        [string]$SchemaPath,

        [Parameter(Mandatory)]
        [int]$Sequence,

        [string]$ExtraContext = ""
    )

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

    $outputPath = Join-Path `
        $LogDir `
        ("{0}-{1:D3}-{2}.json" -f $Kind, $Sequence, $timestamp)

    $prompt = Get-Content $PromptPath -Raw

    $prompt += @"


# Automation context

This is an automated Codex execution.

Project root:
$ProjectRoot

Execution type:
$Kind

Execution number:
$Sequence

Persistent project state must be stored in the repository,
especially IMPLEMENTATION_PLAN.md and STATUS.md.

Do not rely on previous Codex conversation state.
Inspect the current repository state yourself.

The automation state directory is:

.codex-loop/

If .codex-loop/verification.log exists, inspect it when relevant.

$ExtraContext
"@

    Write-Host ""
    Write-Host "============================================================"
    Write-Host " Codex: $Kind #$Sequence"
    Write-Host "============================================================"
    Write-Host ""

    $codexArgs = @(
        "exec",
        "--ephemeral",
        "--approve-for-me",
        "--sandbox", "workspace-write",
        "--color", "never",
        "-C", $ProjectRoot,
        "--output-schema", $SchemaPath,
        "-o", $outputPath,
        "-"
    )

    # Prompt is supplied through stdin.
    $prompt | & codex @codexArgs

    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        Write-Warning "Codex exited with code $exitCode."

        return [PSCustomObject]@{
            Success = $false
            ExitCode = $exitCode
            OutputPath = $outputPath
            Data = $null
        }
    }

    if (-not (Test-Path $outputPath)) {
        Write-Warning "Codex did not create an output file."

        return [PSCustomObject]@{
            Success = $false
            ExitCode = $exitCode
            OutputPath = $outputPath
            Data = $null
        }
    }

    try {
        $json = Get-Content $outputPath -Raw
        $data = $json | ConvertFrom-Json
    }
    catch {
        Write-Warning "Could not parse Codex output as JSON."
        Write-Warning $_

        return [PSCustomObject]@{
            Success = $false
            ExitCode = $exitCode
            OutputPath = $outputPath
            Data = $null
        }
    }

    Write-Host ""
    Write-Host "Result:"
    $data | ConvertTo-Json -Depth 10 | Write-Host

    return [PSCustomObject]@{
        Success = $true
        ExitCode = 0
        OutputPath = $outputPath
        Data = $data
    }
}


# ============================================================
# Plan Audit
# ============================================================

function Invoke-PlanAudit {
    param(
        [Parameter(Mandatory)]
        [int]$Sequence,

        [Parameter(Mandatory)]
        [string]$Reason
    )

    Write-Host ""
    Write-Host ">>> PLAN AUDIT"
    Write-Host "Reason: $Reason"

    return Invoke-CodexRun `
        -Kind "plan-audit" `
        -PromptPath $PlanAuditPrompt `
        -SchemaPath $PlanAuditSchema `
        -Sequence $Sequence `
        -ExtraContext @"
Plan Audit was triggered for the following reason:

$Reason
"@
}


# ============================================================
# Completion Audit
# ============================================================

function Invoke-CompletionAudit {
    param(
        [Parameter(Mandatory)]
        [int]$Sequence
    )

    Write-Host ""
    Write-Host ">>> COMPLETION AUDIT"

    return Invoke-CodexRun `
        -Kind "completion-audit" `
        -PromptPath $CompletionAuditPrompt `
        -SchemaPath $CompletionAuditSchema `
        -Sequence $Sequence `
        -ExtraContext @"
The Implementation Codex has claimed that the project is COMPLETE.

Do not trust that claim.

Act as an independent reviewer and actively try to prove that
the project is still incomplete.
"@
}


# ============================================================
# External verification
# ============================================================

function Invoke-ExternalVerification {

    $verificationLog = Join-Path $StateDir "verification.log"

    $log = @()

    Write-Host ""
    Write-Host "============================================================"
    Write-Host " EXTERNAL VERIFICATION"
    Write-Host "============================================================"
    Write-Host ""

    Push-Location $ProjectRoot

    try {
        # ----------------------------------------------------
        # Build
        # ----------------------------------------------------

        Write-Host ">>> dotnet build"

        $buildOutput = & dotnet build 2>&1
        $buildExitCode = $LASTEXITCODE

        $buildOutput | Write-Host

        $log += "===== dotnet build ====="
        $log += $buildOutput
        $log += ""
        $log += "Exit code: $buildExitCode"
        $log += ""

        if ($buildExitCode -ne 0) {
            $log | Set-Content -Encoding UTF8 $verificationLog

            Write-Warning "External build verification failed."
            return $false
        }


        # ----------------------------------------------------
        # Tests
        # ----------------------------------------------------

        Write-Host ""
        Write-Host ">>> dotnet test --no-build"

        $testOutput = & dotnet test --no-build 2>&1
        $testExitCode = $LASTEXITCODE

        $testOutput | Write-Host

        $log += "===== dotnet test --no-build ====="
        $log += $testOutput
        $log += ""
        $log += "Exit code: $testExitCode"
        $log += ""

        if ($testExitCode -ne 0) {
            $log | Set-Content -Encoding UTF8 $verificationLog

            Write-Warning "External test verification failed."
            return $false
        }


        # ----------------------------------------------------
        # Add Kimigayo-specific E2E verification here.
        # ----------------------------------------------------

        # Example:
        #
        # & dotnet run `
        #     --project ./src/Kimigayo.Compiler `
        #     -- ./tests/e2e/hello.kimi
        #
        # if ($LASTEXITCODE -ne 0) {
        #     ...
        # }


        $log += "===== RESULT ====="
        $log += "VERIFIED"

        $log | Set-Content -Encoding UTF8 $verificationLog

        Write-Host ""
        Write-Host "External verification succeeded."

        return $true
    }
    finally {
        Pop-Location
    }
}


# ============================================================
# Main loop
# ============================================================

$implementationRun = 0
$planAuditRun = 0
$completionAuditRun = 0

$consecutiveFailures = 0


while ($implementationRun -lt $MaxImplementationRuns) {

    $implementationRun++

    Write-Host ""
    Write-Host ""
    Write-Host "############################################################"
    Write-Host " IMPLEMENTATION RUN $implementationRun / $MaxImplementationRuns"
    Write-Host "############################################################"


    # --------------------------------------------------------
    # Implementation
    # --------------------------------------------------------

    $result = Invoke-CodexRun `
        -Kind "implementation" `
        -PromptPath $ImplementationPrompt `
        -SchemaPath $ImplementationSchema `
        -Sequence $implementationRun


    # --------------------------------------------------------
    # Codex execution failure
    # --------------------------------------------------------

    if (-not $result.Success) {

        $consecutiveFailures++

        Write-Warning `
            "Consecutive Codex failures: $consecutiveFailures"

        if ($consecutiveFailures -ge $FailureAuditThreshold) {

            $planAuditRun++

            $audit = Invoke-PlanAudit `
                -Sequence $planAuditRun `
                -Reason "$consecutiveFailures consecutive Codex execution failures."

            $consecutiveFailures = 0
        }

        continue
    }

    $consecutiveFailures = 0

    $data = $result.Data


    # --------------------------------------------------------
    # Blocked
    # --------------------------------------------------------

    if ($data.status -eq "blocked") {

        Write-Warning "Implementation Codex reported BLOCKED."

        $planAuditRun++

        $audit = Invoke-PlanAudit `
            -Sequence $planAuditRun `
            -Reason "Implementation Codex reported BLOCKED."

        continue
    }


    # --------------------------------------------------------
    # COMPLETE claimed
    # --------------------------------------------------------

    if ($data.status -eq "complete") {

        Write-Host ""
        Write-Host "Implementation Codex claims COMPLETE."
        Write-Host "Running independent Completion Audit..."

        $completionAuditRun++

        $completion = Invoke-CompletionAudit `
            -Sequence $completionAuditRun

        if (-not $completion.Success) {
            Write-Warning "Completion Audit failed to execute."
            continue
        }


        # Completion Audit found missing work
        if ($completion.Data.status -eq "not_complete") {

            Write-Warning "Completion Audit rejected COMPLETE."
            Write-Host $completion.Data.summary

            continue
        }


        if ($completion.Data.status -eq "blocked") {

            Write-Warning "Completion Audit was blocked."

            $planAuditRun++

            $audit = Invoke-PlanAudit `
                -Sequence $planAuditRun `
                -Reason "Completion Audit was blocked."

            continue
        }


        # ----------------------------------------------------
        # Independent audit says complete.
        # Verify from the outer orchestrator.
        # ----------------------------------------------------

        if ($completion.Data.status -eq "verified_complete") {

            Write-Host ""
            Write-Host "Completion Audit reports VERIFIED_COMPLETE."
            Write-Host "Running external verification..."

            $verified = Invoke-ExternalVerification

            if ($verified) {

                Write-Host ""
                Write-Host "############################################################"
                Write-Host " PROJECT VERIFIED COMPLETE"
                Write-Host "############################################################"
                Write-Host ""

                exit 0
            }


            # External verification disproved completion
            Write-Warning `
                "External verification disproved project completion."

            $planAuditRun++

            $audit = Invoke-PlanAudit `
                -Sequence $planAuditRun `
                -Reason @"
Completion Audit reported VERIFIED_COMPLETE,
but external build/test verification failed.

Inspect:

.codex-loop/verification.log

Update IMPLEMENTATION_PLAN.md accordingly.
"@

            continue
        }
    }


    # --------------------------------------------------------
    # Decide whether Plan Audit is needed
    # --------------------------------------------------------

    $auditReasons = @()


    # Periodic audit
    if (
        $AuditInterval -gt 0 -and
        ($implementationRun % $AuditInterval) -eq 0
    ) {
        $auditReasons += `
            "Periodic audit after $implementationRun implementation runs."
    }


    # Milestone completion audit
    if ($data.milestone_completed -eq $true) {

        $milestoneName = $data.milestone

        if ([string]::IsNullOrWhiteSpace($milestoneName)) {
            $milestoneName = "(unnamed milestone)"
        }

        $auditReasons += `
            "Milestone completed: $milestoneName"
    }


    # Codex-requested audit
    if ($data.needs_plan_audit -eq $true) {
        $auditReasons += `
            "Implementation Codex requested a Plan Audit."
    }


    # --------------------------------------------------------
    # Run one audit even if several conditions fired
    # --------------------------------------------------------

    if ($auditReasons.Count -gt 0) {

        $planAuditRun++

        $reason = $auditReasons -join "`n"

        $audit = Invoke-PlanAudit `
            -Sequence $planAuditRun `
            -Reason $reason

        if (-not $audit.Success) {
            Write-Warning "Plan Audit execution failed."
        }
    }
}


# ============================================================
# Maximum iterations reached
# ============================================================

Write-Host ""
Write-Warning `
    "Maximum implementation run count ($MaxImplementationRuns) reached."

exit 2