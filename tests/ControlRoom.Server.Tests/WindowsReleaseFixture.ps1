# Each invocation receives its own temporary directory and a fixed clock from the test.
param(
    [Parameter(Mandatory)][string]$Fixture,
    [Parameter(Mandatory)][string]$Scripts,
    [Parameter(Mandatory)][string]$Scenario,
    [Parameter(Mandatory)][long]$Epoch
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$inputs = Join-Path $Fixture 'script copies'
[void][IO.Directory]::CreateDirectory($inputs)
foreach ($scriptFile in 'launch-agent.ps1','updater.ps1') {
    $text = [IO.File]::ReadAllText((Join-Path $Scripts $scriptFile))
    if ($Scenario -eq 'sample' -and $scriptFile -eq 'updater.ps1') {
        $block = @'
# roomkit:app-begin
$DataFolder = 'SampleRoom'
$TaskPrefix = 'SampleRoom'
$ServerName = 'sampleroom.exe'
$ZipPrefix = 'sampleroom'
$AppPort = 8201
$Repository = 'example/sampleroom'
$JobsFolder = 'work/jobs'
$JobFile = 'job.json'
# roomkit:app-end
'@
        $text = [regex]::Replace($text, '(?s)# roomkit:app-begin.*?# roomkit:app-end', { $block })
    }
    [IO.File]::WriteAllText((Join-Path $inputs $scriptFile), $text)
}
. (Join-Path $inputs 'launch-agent.ps1')

$Support = Join-Path $Fixture $DataFolder
$factory = Join-Path $Fixture "Factory Root & team's [local]"
[void][IO.Directory]::CreateDirectory($factory)
$clock = [DateTimeOffset]::FromUnixTimeSeconds($Epoch)
$tasks = @{}
$history = [Collections.Generic.List[string]]::new()
$requests = [Collections.Generic.List[string]]::new()
$archiveProblem = ''
$offered = 'v1.3.0'
$badListener = $false
$failRecovery = $false
$failStart = $false
$running = $false
$writePrivate = (Get-Item function:Write-PrivateText).ScriptBlock
function Write-PrivateText($Name, $Value) {
    if ($Scenario -eq 'swap' -and $Name -eq 'installed-release' -and $Value.Trim() -eq 'v1.3.0') {
        throw 'Fixture could not commit the new marker.'
    }
    & $writePrivate $Name $Value
}

function Expect($Actual, $Wanted) {
    if ([string]$Actual -cne [string]$Wanted) { throw "Expected [$Wanted], got [$Actual]." }
}
function Current-Code { [IO.File]::ReadAllText((Join-Path (Join-Path $Support 'server') $ServerName)) }
function Get-UpdateTime { return $clock }
function Wait-RoomPoll { }
function Get-ReleasePage($Page) {
    $requests.Add("list $Page")
    foreach ($name in @('v1.2.0', $offered, 'v1.4.0-develop.3', 'v1.4.0-rc.1', 'v9.0.0-rc.1-develop.3', 'v1.4.0-develop.12')) {
        @{ tag_name = $name; draft = $false; prerelease = $name.Contains('-') }
    }
    @{ tag_name = 'v22.0.0'; draft = $true; prerelease = $false }
}

# These fakes consume the actual action, principal, trigger and settings passed by the installer.
function New-ScheduledTaskAction($Execute, $Argument, $WorkingDirectory) { return $PSBoundParameters }
function New-ScheduledTaskPrincipal($UserId, $LogonType, $RunLevel) { return $PSBoundParameters }
function New-ScheduledTaskSettingsSet($MultipleInstances, $ExecutionTimeLimit, [switch]$AllowStartIfOnBatteries, [switch]$DontStopIfGoingOnBatteries) { return $PSBoundParameters }
function New-ScheduledTaskTrigger([switch]$AtLogOn, $User, [switch]$Once, $At, $RepetitionInterval) { return $PSBoundParameters }
function Register-ScheduledTask($TaskPath, $TaskName, $Action, $Trigger, $Principal, $Settings, [switch]$Force, $Xml) {
    $tasks[$TaskName] = @{ State = 'Ready'; Name = $TaskName; Action = $Action; Trigger = $Trigger; Principal = $Principal }
}
function Get-ScheduledTask($TaskPath, $TaskName, $ErrorAction) { return $tasks[$TaskName] }
function Export-ScheduledTask($TaskPath, $TaskName) { return '<fixture/>' }
function Unregister-ScheduledTask($TaskPath, $TaskName, [switch]$Confirm) { $tasks.Remove($TaskName) }
function Start-ScheduledTask($TaskPath, $TaskName) {
    $version = Current-Code
    $history.Add("start $version")
    if (($failStart -and $version -ceq '1.3.0') -or ($failRecovery -and $version -ceq '1.2.0')) { throw 'Fixture start failure.' }
    $script:running = $true
    $tasks[$TaskName].State = 'Running'
}
function Stop-ScheduledTask($InputObject) {
    $history.Add("stop $(Current-Code)")
    $script:running = $false
    $InputObject.State = 'Ready'
}
function Get-Process($Id, $ErrorAction) {
    Expect $Id 975
    $fake = [pscustomobject]@{ Handle = 1 }
    Add-Member -InputObject $fake -MemberType ScriptMethod -Name WaitForExit -Value { $history.Add("exit $(Current-Code)"); return !$running }
    Add-Member -InputObject $fake -MemberType ScriptMethod -Name Dispose -Value { }
    return $fake
}
function Get-ServerProcessId { if ($running) { return 975 } }
function Get-ListenerPid { if ($badListener -and (Current-Code) -ceq '1.3.0') { return 501 }; return 975 }
function Get-HealthVersion { return (Current-Code) }

function Add-ZipText($Zip, $Name, $Value) {
    $entry = $Zip.CreateEntry($Name)
    $writer = [IO.StreamWriter]::new($entry.Open())
    try { $writer.Write($Value) } finally { $writer.Dispose() }
    return $entry
}

function New-FixtureRelease($Tag, $Name) {
    $zipPath = Join-Path $Fixture 'fixture.zip'
    [IO.File]::Delete($zipPath)
    $zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        [void](Add-ZipText $zip $ServerName $Tag.Substring(1))
        [void](Add-ZipText $zip 'updater.ps1' "updater $Tag")
        [void](Add-ZipText $zip 'launch-agent.ps1' "installer $Tag")
        [void](Add-ZipText $zip 'wwwroot/index.html' 'home')
        [void](Add-ZipText $zip 'wwwroot/app.css' 'css')
        switch ($archiveProblem) {
            'escape' { [void](Add-ZipText $zip '../outside' 'refused') }
            'link' { (Add-ZipText $zip 'linked' 'refused').ExternalAttributes = -1577058304 }
            'reparse' { (Add-ZipText $zip 'linked' 'refused').ExternalAttributes = 1024 }
            'entries' { for ($i = 0; $i -lt 9996; $i++) { [void]$zip.CreateEntry("file$i") } }
        }
    } finally { $zip.Dispose() }
    if ($archiveProblem -eq 'expanded') {
        # Inflate the advertised uncompressed size in the first central-directory record.
        $bytes = [IO.File]::ReadAllBytes($zipPath)
        $offset = [BitConverter]::ToInt32($bytes, $bytes.Length - 6)
        [BitConverter]::GetBytes([int](512MB + 1)).CopyTo($bytes, $offset + 24)
        [IO.File]::WriteAllBytes($zipPath, $bytes)
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $digest = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($zipPath))).Replace('-', '') }
    finally { $sha.Dispose() }
    $line = "$digest  $Name"
    $sum = switch ($archiveProblem) {
        'digest' { ('0' * 64) + "  $Name" }
        'malformed' { "broken  $Name" }
        'missing' { "$digest  elsewhere.zip" }
        'duplicate' { $line + [char]10 + $line }
        'manifest' { $line + [char]10 + ('x' * 4096) }
        default { $line }
    }
    [IO.File]::WriteAllText((Join-Path $Fixture 'fixture.sums'), $sum)
}

function Save-ReleaseAsset([uri]$Uri, $Path, [long]$Limit) {
    $requests.Add($Uri.AbsoluteUri)
    $tag = $Uri.Segments[-2].TrimEnd('/')
    $file = $Uri.Segments[-1]
    Expect $Uri.AbsoluteUri "https://github.com/$Repository/releases/download/$tag/$file"
    $name = "$ZipPrefix-$($tag.Substring(1))-win-x64.zip"
    if ($file -eq 'SHA256SUMS') {
        Expect $Limit 4096
        New-FixtureRelease $tag $name
        [IO.File]::Copy((Join-Path $Fixture 'fixture.sums'), $Path)
    } else {
        Expect $file $name
        Expect $Limit (200MB)
        [IO.File]::Copy((Join-Path $Fixture 'fixture.zip'), $Path)
        if ($archiveProblem -eq 'zip') {
            $stream = [IO.File]::OpenWrite($Path)
            try { $stream.SetLength(200MB + 1) } finally { $stream.Dispose() }
        }
    }
}

function Set-Options($Channel, $Interval) {
    Write-PrivateText 'settings.json' ('{"autoUpdate":true,"channel":"' + $Channel + '","updateInterval":' + $Interval + '}')
}
function Marker { return (Read-PrivateText 'installed-release' 200).Trim() }
function ListCount { return @($requests | Where-Object { $_ -like 'list *' }).Count }

Expect (Install-Room $factory 'v1.2.0' 'desk.example.ts.net') 'Installed v1.2.0. Server running.'
$old = 'v1.2.0'
Set-Options stable 30
$resultPath = Join-Path $Support 'update-result.json'
if ([IO.File]::Exists($resultPath)) { throw 'Installer wrote an updater result.' }
if ($Scenario -in 'lifecycle','sample') {
    [IO.File]::WriteAllText((Join-Path $Support 'access-token'), 'fixture sentinel')
    Expect (Install-Room $factory $old 'desk.example.ts.net') 'Installed v1.2.0. Server running.'
    Expect $tasks.Count 2
    $serverDefinition = $tasks[$ServerTask]
    Expect $serverDefinition.Action.Execute (Join-Path (Join-Path $Support 'server') $ServerName)
    Expect $serverDefinition.Action.Argument ('--FactoryRoot "' + $factory + '" --TailnetHost "desk.example.ts.net"')
    Expect $serverDefinition.Principal.UserId $UserSid.Value
    Expect $serverDefinition.Principal.LogonType 'Interactive'
    Expect $serverDefinition.Principal.RunLevel 'Limited'
    Expect $serverDefinition.Trigger.AtLogOn $true
    Expect $tasks[$UpdaterTask].Trigger.RepetitionInterval.TotalMinutes 15
    Expect $tasks[$UpdaterTask].Principal.RunLevel 'Limited'
    Expect $tasks[$UpdaterTask].Action.Argument ('-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File "' + (Join-Path (Join-Path $Support 'server') 'updater.ps1') + '" -FactoryRoot "' + $factory + '"')
    Expect (Get-RoomStatus) 'Installed=True Running=True'
    if ($Scenario -eq 'sample') {
        Expect $DataFolder 'SampleRoom'
        Expect $ServerTask "SampleRoom-server-$($UserSid.Value)"
        Expect $ServerName 'sampleroom.exe'
        Expect $AppPort 8201
        Expect $Repository 'example/sampleroom'
        Expect $requests[1] 'https://github.com/example/sampleroom/releases/download/v1.2.0/sampleroom-1.2.0-win-x64.zip'
    }
    Expect (Uninstall-Room) 'Uninstalled.'
    Expect (Get-RoomStatus) 'Installed=False Running=False'
    Expect $tasks.Count 0
    Expect ([IO.Directory]::Exists((Join-Path $Support 'server'))) $false
    Expect ([IO.File]::ReadAllText((Join-Path $Support 'access-token'))) 'fixture sentinel'
    Expect (Get-UpdateSettings).Enabled $true
} elseif ($Scenario -eq 'interval') {
    $offered = $old
    Write-PrivateText 'last-check' "$Epoch"
    Expect (Invoke-RoomUpdate $factory) 'current'
    Expect (ListCount) 1
    Expect (Read-PrivateText 'last-check' 32) ("$Epoch" + [char]10)
    $clock = $clock.AddSeconds(1739)
    Expect (Invoke-RoomUpdate $factory) 'not_due'
    Expect (ListCount) 1
    Expect (Read-PrivateText 'last-check' 32) ("$Epoch" + [char]10)
    $clock = $clock.AddSeconds(1)
    Expect (Invoke-RoomUpdate $factory) 'current'
    Expect (ListCount) 2
    Expect (Read-PrivateText 'last-check' 32) ([string]($Epoch + 1740) + [char]10)
    Set-Options stable 60
    $clock = $clock.AddMinutes(30)
    Expect (Invoke-RoomUpdate $factory) 'not_due'
    $count = 2
    foreach ($bad in 'missing','broken',([string]($clock.ToUnixTimeSeconds() + 1))) {
        if ($bad -eq 'missing') { [IO.File]::Delete((Join-Path $Support 'last-check')) }
        else { Write-PrivateText 'last-check' $bad }
        Expect (Invoke-RoomUpdate $factory) 'current'
        Expect (ListCount) (++$count)
        Expect (Read-PrivateText 'last-check' 32) ([string]$clock.ToUnixTimeSeconds() + [char]10)
    }
    Expect ([IO.File]::Exists($resultPath)) $false
} elseif ($Scenario -eq 'defer') {
    foreach ($keepResult in $false,$true) {
        if ($keepResult) { [IO.File]::WriteAllBytes($resultPath, [byte[]](0,127,255)) }
        foreach ($stage in 'spike','spec','build','review') {
            $job = Join-Path (Join-Path $factory $JobsFolder) 'fixture'
            [void][IO.Directory]::CreateDirectory($job)
            [IO.File]::WriteAllText((Join-Path $job $JobFile), ('{"stage":"' + $stage + '"}'))
            Expect (Invoke-RoomUpdate $factory) 'job_running'
        }
        $held = Get-UpdateLock
        try { Expect (Invoke-RoomUpdate $factory) 'busy' } finally { $held.Dispose() }
        Write-PrivateText 'settings.json' '{"autoUpdate":false}'
        Expect (Invoke-RoomUpdate $factory) 'off'
        Set-Options stable 30
        Expect (ListCount) 0
        Expect (Marker) $old
        Expect ([IO.File]::Exists($resultPath)) $keepResult
        if ($keepResult) { Expect ([Convert]::ToBase64String([IO.File]::ReadAllBytes($resultPath))) 'AH//' }
    }
} elseif ($Scenario -eq 'archive') {
    foreach ($bad in 'digest','missing','malformed','duplicate','manifest','zip','entries','expanded','escape','link','reparse') {
        $archiveProblem = $bad
        $failed = $false
        try { Install-Room $factory 'v1.3.0' '' | Out-Null } catch [IO.InvalidDataException] { $failed = $true }
        Expect $failed $true
        Write-PrivateText 'first-check' '1'
        Expect (Invoke-RoomUpdate $factory) 'check'
        Expect (Current-Code) '1.2.0'
        Expect (Marker) $old
        Expect ([IO.File]::ReadAllText((Join-Path $Support 'server/updater.ps1'))) 'updater v1.2.0'
        Expect $history.Count 1
    }
} else {
    $job = Join-Path (Join-Path $factory $JobsFolder) 'fixture'
    [void][IO.Directory]::CreateDirectory($job)
    [IO.File]::WriteAllText((Join-Path $job $JobFile), '{"stage":"approve"}')
    $badListener = $Scenario -in 'wrong-listener','rollback'
    $failRecovery = $Scenario -eq 'rollback'
    $failStart = $Scenario -eq 'start'
    if ($Scenario -eq 'develop') { Set-Options develop 30 }
    $target = if ($Scenario -eq 'develop') { '1.4.0-develop.12' } else { '1.3.0' }
    $expected = switch ($Scenario) {
        'wrong-listener' { 'no_answer' }
        'rollback' { 'rollback_failed' }
        'start' { 'bootstrap_failed' }
        'swap' { 'swap' }
        'stable' { 'updated' }
        'develop' { 'updated' }
        default { throw 'Unknown fixture scenario.' }
    }
    Expect (Invoke-RoomUpdate $factory) $expected
    Expect $history[1] 'stop 1.2.0'
    Expect $history[2] 'exit 1.2.0'
    Expect $history[3] "start $target"
    if ($expected -eq 'updated') {
        Expect (Marker) "v$target"
        Expect (Current-Code) $target
        Expect ([IO.File]::ReadAllText((Join-Path $Support 'server/updater.ps1'))) "updater v$target"
    } else {
        Expect (Marker) $old
        Expect (Current-Code) '1.2.0'
        Expect $history[$history.Count - 1] 'start 1.2.0'
        Expect $running (!$failRecovery)
        Expect ([IO.File]::ReadAllText((Join-Path $Support 'server/updater.ps1'))) 'updater v1.2.0'
    }
}
