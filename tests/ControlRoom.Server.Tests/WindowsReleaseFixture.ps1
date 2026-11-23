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
$factory = Join-Path $Fixture ("Factory Root & team's [local] %TEMP% " + [char]0x2019)
if ($Scenario -eq 'linked-jobs') { $factory = Join-Path $Fixture 'scan root' }
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
function Write-PrivateText($Name, $Value, $Folder = $Support) {
    if ($Scenario -eq 'swap' -and $Name -eq 'installed-release' -and $Value.Trim() -eq 'v1.3.0') {
        throw 'Fixture could not commit the new marker.'
    }
    & $writePrivate $Name $Value $Folder
}

function Expect($Actual, $Wanted) {
    if ([string]$Actual -cne [string]$Wanted) { throw "Expected [$Wanted], got [$Actual]." }
}
function Current-Code { [IO.File]::ReadAllText((Join-Path (Join-Path $Support 'server') $ServerName)) }
function Get-UpdateTime { return $clock }
function Wait-RoomPoll { }
function Get-FixtureReleases {
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

function New-FixtureResponse($Stream, $Status = 200, $Location = $null, $Length = -1) {
    $response = [pscustomobject]@{ Stream = $Stream; StatusCode = $Status; ContentLength = $Length; Headers = @{ Location = $Location } }
    Add-Member -InputObject $response -MemberType ScriptMethod -Name GetResponseStream -Value { return $this.Stream }
    Add-Member -InputObject $response -MemberType ScriptMethod -Name Dispose -Value { if ($this.Stream) { $this.Stream.Dispose() } }
    return $response
}

function Open-WebResponse([uri]$Uri, $TimeoutMs) {
    if ($Uri.Host -eq '127.0.0.1') {
        Expect $Uri.AbsoluteUri "http://127.0.0.1:$AppPort/health"
        $current = Current-Code
        $json = '{"version":"' + $current + '"}'
        if ($current -eq '1.3.0') {
            switch ($Scenario) {
                'health-array' { $json = '{"version":["1.3.0"]}' }
                'health-list' { $json = '[' + $json + ']' }
                'health-bytes' { $json += ' ' * 4KB }
            }
        }
        return New-FixtureResponse ([IO.MemoryStream]::new($Utf8.GetBytes($json)))
    }
    if ($Uri.Host -eq 'api.github.com') {
        if ($Uri.Query -cnotmatch '\A\?per_page=100&page=([0-9]+)\z') { throw 'Invalid release query.' }
        $page = $Matches[1]
        Expect $Uri.AbsoluteUri "https://api.github.com/repos/$Repository/releases?per_page=100&page=$page"
        $requests.Add("list $page")
        $releases = @(Get-FixtureReleases)
        if ($Scenario -eq 'release-pages') { $releases = @($releases[0]) * 100 }
        $json = ConvertTo-Json -InputObject $releases -Compress
        if ($Scenario -eq 'release-bytes') { $json += ' ' * 1MB }
        return New-FixtureResponse ([IO.MemoryStream]::new($Utf8.GetBytes($json)))
    }
    $requests.Add($Uri.AbsoluteUri)
    $tag = $Uri.Segments[-2].TrimEnd('/')
    $file = $Uri.Segments[-1]
    Expect $Uri.AbsoluteUri "https://github.com/$Repository/releases/download/$tag/$file"
    $name = "$ZipPrefix-$($tag.Substring(1))-win-x64.zip"
    if ($archiveProblem -eq 'redirect') { return New-FixtureResponse $null 302 'https://elsewhere.invalid/file' }
    if ($archiveProblem -eq 'repository') { return New-FixtureResponse $null 302 'https://github.com/elsewhere/releases/file' }
    if ($file -eq 'SHA256SUMS') {
        New-FixtureRelease $tag $name
        return New-FixtureResponse ([IO.File]::OpenRead((Join-Path $Fixture 'fixture.sums')))
    } else {
        Expect $file $name
        $length = if ($archiveProblem -eq 'zip') { 200MB + 1 } else { -1 }
        return New-FixtureResponse ([IO.File]::OpenRead((Join-Path $Fixture 'fixture.zip'))) 200 $null $length
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
    $received = [Environment]::ExpandEnvironmentVariables($serverDefinition.Action.Argument)
    if ($received -cnotmatch '\A--FactoryRootBase64 ([A-Za-z0-9+/=]+) --TailnetHost "desk\.example\.ts\.net"\z') { throw 'Invalid server action.' }
    [IO.File]::WriteAllLines((Join-Path $Fixture 'server-root.txt'), [string[]]($Matches[1], $factory))
    Expect ($Utf8.GetString([Convert]::FromBase64String($Matches[1]))) $factory
    Expect $serverDefinition.Principal.UserId $UserSid.Value
    Expect $serverDefinition.Principal.LogonType 'Interactive'
    Expect $serverDefinition.Principal.RunLevel 'Limited'
    Expect $serverDefinition.Trigger.AtLogOn $true
    Expect $tasks[$UpdaterTask].Trigger.RepetitionInterval.TotalMinutes 15
    Expect $tasks[$UpdaterTask].Principal.RunLevel 'Limited'
    # Run the registered updater arguments with an isolated entry point that only reports its decoded root.
    $updaterPath = Join-Path (Join-Path $Support 'server') 'updater.ps1'
    $entry = 'if ($MyInvocation.InvocationName -ne ''.'') {'
    $probe = [IO.File]::ReadAllText((Join-Path $inputs 'updater.ps1')).Replace($entry,
        'function Invoke-RoomUpdate($Root) { [IO.File]::WriteAllText((Join-Path $PSScriptRoot "received-root.txt"), $Root) }' + [char]10 + $entry)
    [IO.File]::WriteAllText($updaterPath, $probe)
    $updaterAction = $tasks[$UpdaterTask].Action
    $start = [Diagnostics.ProcessStartInfo]::new($updaterAction.Execute, [Environment]::ExpandEnvironmentVariables($updaterAction.Argument))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $probeProcess = [Diagnostics.Process]::Start($start)
    try {
        if (!$probeProcess.WaitForExit(30000)) { $probeProcess.Kill(); throw 'Updater action timed out.' }
        Expect $probeProcess.ExitCode 0
    } finally { $probeProcess.Dispose() }
    Expect ([IO.File]::ReadAllText((Join-Path (Join-Path $Support 'server') 'received-root.txt'))) $factory
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
    $stamp = Join-Path $Support 'last-check'
    $held = [IO.File]::Open($stamp, 'Open', 'Read', 'None')
    try {
        Expect (Invoke-RoomUpdate $factory) 'current'
        Expect (ListCount) (++$count)
    } finally { $held.Dispose() }
    [IO.File]::Delete($stamp)
    [void][IO.Directory]::CreateDirectory($stamp)
    Expect (Invoke-RoomUpdate $factory) 'current'
    Expect (ListCount) (++$count)
    [IO.Directory]::Delete($stamp)
    Expect ([IO.File]::Exists($resultPath)) $false
} elseif ($Scenario -eq 'linked-stamp') {
    $offered = $old
    [IO.File]::Delete((Join-Path $Support 'first-check'))
    Expect (Invoke-RoomUpdate $factory) 'current'
    Expect (ListCount) 1
    $stamp = Join-Path $Support 'last-check'
    $outside = Join-Path $Fixture 'stamp-target'
    Expect ([IO.File]::ReadAllText($outside)) ([string]$Epoch)
    Expect ([bool]([IO.File]::GetAttributes($stamp) -band [IO.FileAttributes]::ReparsePoint)) $true
    Expect ([IO.File]::Exists($resultPath)) $false
} elseif ($Scenario -in 'release-pages','release-bytes') {
    $refused = $false
    try { Invoke-RoomUpdate $factory | Out-Null } catch { $refused = $true }
    Expect $refused $true
    if ($Scenario -eq 'release-pages') { Expect (ListCount) 10 } else { Expect (ListCount) 1 }
    Expect (Marker) $old
    Expect ([IO.File]::Exists($resultPath)) $false
} elseif ($Scenario -eq 'job-scan') {
    $jobs = Join-Path $factory $JobsFolder
    $job = [IO.Directory]::CreateDirectory((Join-Path $jobs 'hidden'))
    $job.Attributes = $job.Attributes -bor [IO.FileAttributes]::Hidden
    $state = Join-Path $job.FullName $JobFile
    foreach ($json in '{"stage":"build"}', ('{"stage":"approve"}' + ' ' * 64KB), '{', '{}') {
        [IO.File]::WriteAllText($state, $json)
        Expect (Invoke-RoomUpdate $factory) 'job_running'
    }
    [IO.File]::WriteAllText($state, '{"stage":"approve"}')
    $held = [IO.File]::Open($state, 'Open', 'Read', 'None')
    try { Expect (Invoke-RoomUpdate $factory) 'job_running' } finally { $held.Dispose() }
    Expect (ListCount) 0
    Expect ([IO.File]::Exists($resultPath)) $false
} elseif ($Scenario -eq 'linked-jobs') {
    Expect (Invoke-RoomUpdate $factory) 'job_running'
    Expect ([IO.Directory]::GetFileSystemEntries((Join-Path $Fixture 'real job store')).Count) 0
    Expect (ListCount) 0
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
    foreach ($bad in 'digest','missing','malformed','duplicate','manifest','zip','entries','expanded','escape','link','reparse','redirect','repository') {
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
} elseif ($Scenario -eq 'invalid-pending') {
    $stage = New-ReleaseWork
    Write-PrivateText 'update-result.json' '{"prior":true}'
    $valid = '{"run":"20261123T120000Z","result":"update_failed","version":"1.3.0","reason":"check"}'
    foreach ($bad in '{}', ('[' + $valid + ']'), $valid.Replace('1.3.0','bad'), $valid.Replace('check','bad'),
        $valid.Replace('"reason"', '"Reason"'), $valid.Replace('"check"', '"swap","reason":"check"')) {
        $pending = $bad + [char]10
        Write-PrivateText 'pending-result.json' $pending $stage
        $refused = $false
        try { Invoke-RoomUpdate $factory | Out-Null } catch { $refused = $_.Exception.Message -ceq 'Invalid staged update result.' }
        Expect $refused $true
        Expect (Read-PrivateText 'pending-result.json' 4KB $stage) $pending
        Expect (Read-PrivateText 'update-result.json' 4KB) '{"prior":true}'
        Expect (Marker) $old
        Expect (ListCount) 0
    }
} elseif ($Scenario -in 'result-retry','result-before-marker','failed-result-retry') {
    Write-PrivateText 'update-result.json' '{"prior":true}'
    $held = [IO.File]::Open($resultPath, 'Open', 'Read', 'Read')
    if ($Scenario -eq 'failed-result-retry') { $archiveProblem = 'redirect' }
    $refused = $false
    try {
        try { Invoke-RoomUpdate $factory | Out-Null } catch [IO.IOException] { $refused = $true }
    } finally { $held.Dispose() }
    Expect $refused $true
    Expect (Read-PrivateText 'update-result.json' 4KB) '{"prior":true}'
    $stage = @(Get-ChildItem -LiteralPath $Support -Directory -Filter 'stage-*')
    Expect $stage.Count 1
    Expect ([IO.File]::Exists((Join-Path $stage[0].FullName 'pending-result.json'))) $true
    $pending = Read-PrivateText 'pending-result.json' 4KB $stage[0].FullName
    if ($Scenario -ne 'failed-result-retry') {
        Expect ([IO.File]::ReadAllText((Join-Path $stage[0].FullName 'previous/updater.ps1'))) 'updater v1.2.0'
    }
    if ($Scenario -eq 'result-before-marker') { Write-PrivateText 'installed-release' ($old + [char]10) }
    $clock = $clock.AddMinutes(1)
    Expect (Invoke-RoomUpdate $factory) 'not_due'
    Expect (ListCount) 1
    Expect (Read-PrivateText 'update-result.json' 4KB) $pending
    $expectedMarker = if ($Scenario -eq 'failed-result-retry') { $old } else { $offered }
    Expect (Marker) $expectedMarker
    Expect @(Get-ChildItem -LiteralPath $Support -Filter 'stage-*').Count 0
} else {
    $job = Join-Path (Join-Path $factory $JobsFolder) 'fixture'
    [void][IO.Directory]::CreateDirectory($job)
    [IO.File]::WriteAllText((Join-Path $job $JobFile), '{"stage":"approve"}', [Text.UTF8Encoding]::new($true))
    $badListener = $Scenario -in 'wrong-listener','rollback'
    $failRecovery = $Scenario -eq 'rollback'
    $failStart = $Scenario -eq 'start'
    if ($Scenario -eq 'develop') { Set-Options develop 30 }
    $target = if ($Scenario -eq 'develop') { '1.4.0-develop.12' } else { '1.3.0' }
    $expected = switch ($Scenario) {
        'wrong-listener' { 'no_answer' }
        'health-array' { 'no_answer' }
        'health-list' { 'no_answer' }
        'health-bytes' { 'no_answer' }
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
