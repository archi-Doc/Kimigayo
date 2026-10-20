# Hand-authored C1 discriminator inputs and independent expected results.
# This writes data only; it does not invoke either evaluator or infer an oracle.
$ErrorActionPreference = 'Stop'
$unit = Split-Path $PSScriptRoot
$cases = [System.Collections.Generic.List[object]]::new()
$expected = [System.Collections.Generic.List[object]]::new()
function New-Input($id, $roots, $queries) {
    [ordered]@{
        Id=$id; Scope=$id; Oracle='ClosedForward'; Environment='Valid'
        Sources=@(@{Id='ok';State='Available';Provenance='synthetic'},
            @{Id='off';State='Unavailable';Provenance='synthetic'},
            @{Id='wait';State='Pending';Provenance='synthetic'},
            @{Id='limit';State='Resource';Provenance='synthetic'})
        Roots=@($roots); Rules=@(); Operands=@(); Leaves=@(); Queries=@($queries)
        Correspondence='C1 synthetic discriminator; no real source validity or production correspondence'
    }
}
function Root($id, $proposition, $source='ok') { @{Id=$id;Proposition=$proposition;Source=$source} }
function Query($id, $proposition, $kind='Proof') { @{Id=$id;Proposition=$proposition;Kind=$kind;Purpose='Ordinary'} }
function Expected($id, $query, $completion, $value, $positive, $negative) {
    [ordered]@{Case=$id;Query=$query;Completion=$completion;Value=$value;Positive=$positive;Negative=$negative}
}
$equation = Root 'identity' 'eq(T,U)'
$positive = Root 'positive' 'is(T,C)'
foreach($orientation in @('forward','reversed')) {
    $id = 'alias-invalid-' + $orientation
    $eq = if($orientation -eq 'forward'){$equation}else{Root 'identity' 'eq(U,T)'}
    $row = New-Input $id @($eq,$positive) @(
        (Query 'alias' 'is(U,C)'), (Query 'original' 'is(T,C)'),
        (Query 'negative' 'not(is(U,C))'),
        (Query 'candidates' 'is(U,$contract)' 'Candidates'))
    $row.Operands=@(@{Proposition='is(T,C)';State='Invalid'})
    $cases.Add($row)
    foreach($q in @('alias','original','negative','candidates')) {
        $e=Expected $id $q 'Complete' 'Error' ($q -in @('alias','original')) ($q -eq 'negative')
        $e.Closed=$true; $e.Candidates=@(); $e.Errors=@('is(T,C)')
        $e.Supports=@('root:identity','root:positive')
        if($q -eq 'alias') {
            $e.Errors+= 'is(U,C)'
            $e.Required=@('equivalent:is(U,C)<-is(T,C)')
        }
        if($q -eq 'candidates') { $e.UsableCandidates=$false; $e.PrivateCandidates=@('is(T,C)') }
        $expected.Add($e)
    }
}
$row=New-Input 'alias-valid' @($equation,$positive) @((Query 'alias' 'is(U,C)'),(Query 'negative' 'not(is(U,C))'))
$cases.Add($row)
$e=Expected $row.Id 'alias' 'Complete' 'Proven' $true $false; $e.ExactErrors=@();$expected.Add($e)
$e=Expected $row.Id 'negative' 'Complete' 'Refuted' $false $true; $e.ExactErrors=@();$expected.Add($e)

$row=New-Input 'alias-negative-error' @($equation,(Root 'negative' 'not(is(T,C))')) @((Query 'q' 'not(is(U,C))'))
$row.Operands=@(@{Proposition='is(T,C)';State='Invalid'});$cases.Add($row)
$e=Expected $row.Id 'q' 'Complete' 'Error' $true $false
$e.Errors=@('is(T,C)','is(U,C)');$e.Supports=@('root:negative');$expected.Add($e)

$row=New-Input 'alias-downstream-error' @($equation,$positive,(Root 'independent' 'is(U,D)')) @((Query 'q' 'is(U,D)'))
$row.Operands=@(@{Proposition='is(T,C)';State='Invalid'})
$row.Rules=@(@{Id='downstream';Antecedent='is(U,C)';Head='is(U,D)';Source='ok'});$cases.Add($row)
$e=Expected $row.Id 'q' 'Complete' 'Error' $true $false
$e.Errors=@('is(T,C)','is(U,C)','is(U,D)')
$e.Supports=@('root:positive','root:independent','rule:downstream@is(U,C)=>is(U,D)')
$e.Required=@('equivalent:is(U,C)<-is(T,C)','rule:downstream@is(U,C)=>is(U,D)')
$expected.Add($e)

foreach($state in @('Pending','Resource','Unsupported')) {
    $row=New-Input ('alias-' + $state.ToLowerInvariant()) @($equation,$positive) @((Query 'q' 'is(U,C)'))
    $row.Operands=@(@{Proposition='is(T,C)';State=$state});$cases.Add($row)
    if($state -eq 'Pending') {
        $e=Expected $row.Id 'q' 'Pending' $null $true $false
        $e.Pending=@('operand:is(T,C)')
    } else {
        $e=[ordered]@{Case=$row.Id;Query='q';Completion='Failed';Value=$null;Failure=$state;FailureOperation='operand:is(T,C)'}
    }
    $e.Closed=$false;$e.Candidates=@();$expected.Add($e)
}

foreach($state in @('Valid','Invalid','Pending','Resource','Unsupported')) {
    $row=New-Input ('empty-' + $state.ToLowerInvariant()) @() @((Query 'q' 'is(T,$contract)' 'Candidates'))
    $row.Environment=$state;$row.Sources=@();$cases.Add($row)
    $completion=switch($state){'Pending'{'Pending'} 'Resource'{'Failed'} 'Unsupported'{'Failed'} default{'Complete'}}
    $value=if($state -eq 'Invalid'){'Error'}else{$null}
    $e=Expected $row.Id 'q' $completion $value $false $false
    $e.Closed=$completion -eq 'Complete';$e.UsableCandidates=$state -eq 'Valid'
    $e.Candidates=@();$e.PrivateCandidates=@()
    $address='environment:' + $row.Scope
    $e.ExactErrors=@(if($state -eq 'Invalid'){$address})
    $e.Pending=@(if($state -eq 'Pending'){$address})
    if($state -in @('Resource','Unsupported')){$e.Failure=$state;$e.FailureOperation=$address}
    $expected.Add($e)
}

$row=New-Input 'nonempty-invalid-environment' @($positive) @((Query 'q' 'is(T,$contract)' 'Candidates'))
$row.Environment='Invalid';$cases.Add($row)
$e=Expected $row.Id 'q' 'Complete' 'Error' $false $false
$e.Closed=$true;$e.UsableCandidates=$false;$e.Candidates=@();$e.PrivateCandidates=@('is(T,C)')
$e.ExactErrors=@('environment:nonempty-invalid-environment','is(T,C)')
$e.Supports=@('root:positive');$e.Required=@('environment:is(T,C)');$expected.Add($e)

$row=New-Input 'candidate-operand-error' @($positive) @((Query 'q' 'is(T,$contract)' 'Candidates'))
$row.Operands=@(@{Proposition='is(T,C)';State='Invalid'});$cases.Add($row)
$e=Expected $row.Id 'q' 'Complete' 'Error' $false $false
$e.Closed=$true;$e.UsableCandidates=$false;$e.Candidates=@();$e.PrivateCandidates=@('is(T,C)')
$e.ExactErrors=@('is(T,C)');$e.Supports=@('root:positive');$expected.Add($e)

foreach($kind in @('unavailable','pending','required-error')) {
    $row=New-Input ('alternative-' + $kind) @($equation,$positive) @((Query 'q' 'is(U,C)'))
    $source=switch($kind){'unavailable'{'off'} 'pending'{'wait'} default{'ok'}}
    $row.Rules=@(@{Id='alternative';Antecedent='is(T,A)';Head='is(U,C)';Source=$source})
    if($kind -ne 'pending'){$row.Operands=@(@{Proposition='is(T,A)';State='Invalid'})}
    $cases.Add($row)
    $e=Expected $row.Id 'q' $(if($kind -eq 'pending'){'Pending'}else{'Complete'}) $(switch($kind){'unavailable'{'Proven'} 'pending'{$null} default{'Error'}}) $true $false
    $e.Supports=@('root:positive');$e.AbsentSupports=@('rule:alternative@is(T,A)=>is(U,C)')
    if($kind -eq 'unavailable'){$e.ExactErrors=@()}
    if($kind -eq 'pending'){$e.Pending=@('source:wait')}
    if($kind -eq 'required-error'){$e.Errors=@('is(T,A)','is(U,C)');$e.Required=@('rule:alternative@is(T,A)=>is(U,C)')}
    $expected.Add($e)
}
foreach($kind in @('pending','resource')) {
    $source=if($kind -eq 'pending'){'wait'}else{'limit'}
    $row=New-Input ('invalid-entry-' + $kind) @((Root 'dependent' 'is(T,C)' $source)) @((Query 'q' 'is(T,$contract)' 'Candidates'))
    $row.Environment='Invalid';$cases.Add($row)
    $e=[ordered]@{Case=$row.Id;Query='q';Completion=$(if($kind -eq 'pending'){'Pending'}else{'Failed'});Value=$null;Closed=$false;UsableCandidates=$false;Candidates=@()}
    $e.Errors=@('environment:' + $row.Scope)
    if($kind -eq 'pending'){$e.Pending=@('source:wait')}else{$e.Failure='Resource';$e.FailureOperation='source:limit'}
    $expected.Add($e)
}
$utf8=[System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText((Join-Path $unit 'Host/additional-inputs.json'),(ConvertTo-Json -InputObject $cases.ToArray() -Depth 30) + "`n",$utf8)
[System.IO.File]::WriteAllText((Join-Path $unit 'Host/additional-expectations.json'),(ConvertTo-Json -InputObject $expected.ToArray() -Depth 30) + "`n",$utf8)
Write-Output "Authored $($cases.Count) inputs and $($expected.Count) expectations; no evaluator invoked."
