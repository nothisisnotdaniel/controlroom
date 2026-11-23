# Run on the Windows release runner, with the version computed from this checkout.
$ErrorActionPreference = 'Stop'
$release = $env:RELEASE_VERSION
$publish = Join-Path $env:RUNNER_TEMP 'windows-publish'
dotnet publish src/ControlRoom.Server -c Release -r win-x64 --self-contained "-p:Version=$release" -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }

[void][IO.Directory]::CreateDirectory("$PWD/dist")
$zip = "$PWD/dist/controlroom-$release-win-x64.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip)
$extracted = Join-Path $env:RUNNER_TEMP 'extracted release'
[IO.Compression.ZipFile]::ExtractToDirectory($zip, $extracted)
foreach ($file in 'controlroom.exe','launch-agent.ps1','updater.ps1','wwwroot/index.html','wwwroot/app.css') {
    if (![IO.File]::Exists((Join-Path $extracted $file))) { throw "Missing release file $file" }
}
$exe = Join-Path $extracted 'controlroom.exe'
if ((& $exe version) -cne "ControlRoom $release" -or $LASTEXITCODE -ne 0) { throw 'Incorrect executable version.' }

$root = Join-Path $env:RUNNER_TEMP 'Factory Root'
[void][IO.Directory]::CreateDirectory($root)
$start = [Diagnostics.ProcessStartInfo]::new($exe)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
foreach ($argValue in '--FactoryRoot',$root,'--TokenFile',(Join-Path $env:RUNNER_TEMP 'smoke-private/access-token')) {
    $start.ArgumentList.Add($argValue)
}
$server = [Diagnostics.Process]::Start($start)
$client = [Net.Http.HttpClient]::new()
$client.BaseAddress = [uri]'http://127.0.0.1:8100/'
$client.Timeout = [timespan]::FromSeconds(2)
try {
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($server.HasExited) { throw "Extracted server exited with $($server.ExitCode)." }
        try { $health = $client.GetStringAsync('health').GetAwaiter().GetResult() | ConvertFrom-Json }
        catch { $health = $null }
        if ($health.version -ceq $release) { break }
        Start-Sleep -Milliseconds 200
    } while ($deadline.Elapsed.TotalSeconds -lt 30)
    if ($health.version -cne $release) { throw 'Release health check failed.' }
    $listener = @(Get-NetTCPConnection -LocalAddress '127.0.0.1' -LocalPort 8100 -State Listen)
    if ($listener.Count -ne 1 -or $listener[0].OwningProcess -ne $server.Id) { throw 'Unexpected smoke-test listener.' }

    foreach ($asset in @(@('/', 'index.html'), @('/app.css', 'app.css'))) {
        $response = $client.GetAsync($asset[0]).GetAwaiter().GetResult()
        try {
            $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            $expected = [IO.File]::ReadAllBytes((Join-Path $extracted "wwwroot/$($asset[1])"))
            if ([int]$response.StatusCode -ne 200 -or ![Collections.StructuralComparisons]::StructuralEqualityComparer.Equals($bytes, $expected)) {
                throw "Wrong response for $($asset[0])"
            }
        } finally { $response.Dispose() }
    }
    if ($server.HasExited) { throw 'Extracted server exited during the asset check.' }
} finally {
    $client.Dispose()
    if (!$server.HasExited) { $server.Kill(); $server.WaitForExit() }
    $server.Dispose()
}
