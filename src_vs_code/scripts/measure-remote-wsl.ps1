<#
.SYNOPSIS
  Cold-start measurement of the ConnectOtherAIs extension in a Remote-WSL window, on the owner's Windows machine.

.DESCRIPTION
  v2 extension.md section 2 "Measurement" and section 10 row 3. Linux and Windows CI measure a LOCAL extension host; this is the third place:
  an extension host that runs inside WSL while the window runs on Windows. It cannot run in CI, so it is run by hand and its result goes into
  research/RESULTS_v2_start.md (-AppendRow).

  What it does, for each sample (3 by default), and every sample is its own launch:
    1. makes a FRESH --user-data-dir (and --extensions-dir) under -OutDir, and a fresh workspace folder and COAI data folder inside the distro;
    2. installs the coai VSIX and the tiny driver VSIX (scripts/measure-driver) with `code --remote wsl+<distro> --install-extension`;
    3. launches `code --remote wsl+<distro> <fresh folder>` with COAI_MEASURE=1 (passed to the distro through WSLENV);
    4. the driver extension, running in the remote extension host, checks that the coai extension is NOT active, starts the clock, runs
       workbench.view.extension.coai, waits for a fresh `menuReady` (the real sidebar menu is shown and usable), reads the marks through `coai.__measure` and writes coai-measure-result.json
       in the workspace folder; the script reads it back over \\wsl.localhost\<distro>\...;
    5. closes the window, removes what it installed.
  Then scripts/coldstart-report.mjs (node) applies the same rule as CI: the MAXIMUM M2 at most 2000 ms, M1 at most 50 ms, a fresh ack in every sample.
  The result names the extension-host location: `location` is `wsl` when the extension host really ran in WSL (the script fails otherwise), and
  every sample also records the platform and the path the extension was loaded from.

  Needs: Windows with VS Code (`code` on PATH) and the "WSL" remote extension, the distro, node on PATH, and `npm ci` done in src_vs_code (the
  driver VSIX is packed with the repository's own vsce). NOT run by CI and never run by the agent that wrote it: read the notes below first.

  NOTES YOU MUST KNOW BEFORE THE FIRST RUN
  * `--extensions-dir` and `--user-data-dir` are the LOCAL (Windows) side. The WSL server keeps its own ~/.vscode-server/extensions, so the two VSIX
    are installed there (and uninstalled after; -KeepInstalled leaves them). The fresh user-data-dir is what makes the window start with no restored
    sidebar, and the fresh COAI_DATA_DIR inside the distro is what makes the extension start with no data of its own.
  * COAI_MEASURE=1 must be in the environment of the REMOTE VS Code server when it starts. A server that is already running (another window, even
    a closed one a few minutes ago) keeps its old environment, and then `coai.__measure` is not registered and the driver says so. -StopServers kills
    the VS Code servers in the distro first (it ends any VS Code window attached to that distro); without it, close those windows and wait, or run
    `wsl --terminate <distro>`.
  * Nothing is read from or written to anything outside -OutDir, the run's folder inside the distro, and the two extensions it installs.

.PARAMETER Distro
  The WSL distribution name, as `wsl -l -q` prints it (for example Ubuntu).
.PARAMETER Vsix
  The ConnectOtherAIs VSIX to measure (npm run package in src_vs_code, or the release asset).
.PARAMETER Samples
  Cold launches, each with fresh state. Default 3; the maximum counts.
.PARAMETER OutDir
  A folder the run may fill: the fresh profiles, the sample records and result.json go into a new subfolder made inside it. It is yours to clean; nothing is made in a system temp folder.
.PARAMETER Code
  The VS Code command. Default `code`.
.PARAMETER TimeoutSec
  How long one launch may take before it is closed and counted as failed. Default 180.
.PARAMETER StopServers
  Kill the VS Code servers running in the distro before every sample, so COAI_MEASURE=1 reaches the one that starts.
.PARAMETER AppendRow
  Append one row to research/RESULTS_v2_start.md (needs -Label).
.PARAMETER Label
  Free text of the row, for example "remote-wsl, Windows 11 + Ubuntu 24.04, 1.99.0".
.PARAMETER Commit
  The commit the VSIX was built from, for the row. Default: the commit of this checkout, which is only right when the VSIX was built from it.
.PARAMETER KeepInstalled
  Do not uninstall the VSIX from the distro afterwards.
.EXAMPLE
  .\scripts\measure-remote-wsl.ps1 -Distro Ubuntu -Vsix ..\connect-other-ais-0.90.0.vsix -OutDir C:\measure -StopServers -AppendRow -Label "remote-wsl, Windows 11, Ubuntu 24.04"
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)] [string] $Distro,
  [Parameter(Mandatory = $true)] [string] $Vsix,
  [int] $Samples = 3,
  [Parameter(Mandatory = $true)] [string] $OutDir,
  [string] $Code = 'code',
  [int] $TimeoutSec = 180,
  [switch] $StopServers,
  [switch] $AppendRow,
  [string] $Label = '',
  [string] $Commit = '',
  [switch] $KeepInstalled
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$CoaiId = 'remsoftdev.connect-other-ais'
$DriverId = 'coai-measure.coai-measure-driver'
$PackageRoot = Split-Path -Parent $PSScriptRoot
$DriverFolder = Join-Path $PSScriptRoot 'measure-driver'

function Invoke-Native {
  # Runs a native command and fails on a non-zero exit code, which `$ErrorActionPreference` does not do for native commands.
  param([string] $File, [string[]] $Arguments, [switch] $AllowFailure)
  & $File @Arguments
  if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
    throw "$File $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
  }
}

function Invoke-InDistro {
  # A command inside the distro, without a shell of ours in between: `--exec` passes the arguments as they are.
  param([string[]] $Arguments, [switch] $AllowFailure)
  Invoke-Native -File 'wsl.exe' -Arguments (@('-d', $Distro, '--exec') + $Arguments) -AllowFailure:$AllowFailure
}

function Stop-WindowOf {
  # Ends every process whose command line names this sample's user-data folder: the editor's main, renderer and helper processes.
  param([string] $UserData)
  Get-CimInstance Win32_Process |
    Where-Object { $_.CommandLine -and $_.CommandLine.Contains($UserData) } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

if ($Samples -lt 1) { throw '-Samples needs a whole number of at least 1' }
if ($AppendRow -and [string]::IsNullOrWhiteSpace($Label)) { throw '-AppendRow needs -Label: a row without its conditions is a number about something else' }
if (-not (Test-Path -LiteralPath $Vsix)) { throw "the VSIX $Vsix does not exist" }
$Vsix = (Resolve-Path -LiteralPath $Vsix).Path
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'node is not on PATH: scripts/coldstart-report.mjs needs it' }
if (-not (Get-Command $Code -ErrorAction SilentlyContinue)) { throw "$Code is not on PATH" }
$Vsce = Join-Path $PackageRoot 'node_modules\.bin\vsce.cmd'
if (-not (Test-Path -LiteralPath $Vsce)) { throw "$Vsce is missing: run npm ci in $PackageRoot first" }

# The distro must answer, or `--remote wsl+<distro>` would open a window that never connects.
Invoke-InDistro -Arguments @('true')
# The run's own folder inside the distro, under the user's home: the windows opened in it, the extension's data folders and the records live here,
# and it is removed at the end.
$distroHome = (& wsl.exe -d $Distro --exec printenv HOME).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($distroHome)) { throw "could not read the home folder of the user in $Distro" }
$runId = [guid]::NewGuid().ToString('N').Substring(0, 8)
$distroRun = "$distroHome/.coai-measure-$runId"
Invoke-InDistro -Arguments @('mkdir', '-p', $distroRun)
$distroRunWin = "\\wsl.localhost\$Distro" + ($distroRun -replace '/', '\')

$OutDir = Join-Path $OutDir "coai-measure-$runId"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$driverVsix = Join-Path $OutDir 'coai-measure-driver.vsix'
Push-Location $DriverFolder
try {
  Invoke-Native -File $Vsce -Arguments @('package', '--no-dependencies', '--allow-missing-repository', '--skip-license', '--out', $driverVsix)
} finally {
  Pop-Location
}

# COAI_MEASURE reaches the distro through WSLENV: a variable named there is passed on to every Linux process wsl.exe starts. `/u` is
# "only when WSL is started from Windows", which is how the VS Code remote starts its server.
$previousEnv = @{ COAI_MEASURE = $env:COAI_MEASURE; COAI_DATA_DIR = $env:COAI_DATA_DIR; WSLENV = $env:WSLENV }
$records = @()
$failures = @()
try {
  for ($sample = 1; $sample -le $Samples; $sample++) {
    $userData = Join-Path $OutDir "sample-$sample\user-data"
    $extensions = Join-Path $OutDir "sample-$sample\extensions"
    New-Item -ItemType Directory -Force -Path $userData, $extensions | Out-Null
    # The folder the window opens is named `sample-<n>`: the driver reads the sample number from it. Its parent, the COAI data folder
    # and the record all live inside the distro run folder, so the record is read back over \\wsl.localhost.
    $remoteFolder = "$distroRun/sample-$sample"
    $remoteData = "$distroRun/data-$sample"
    Invoke-InDistro -Arguments @('mkdir', '-p', $remoteFolder, $remoteData)
    $recordPath = "$distroRunWin\sample-$sample\coai-measure-result.json"

    if ($StopServers) {
      # The servers' own command line carries `.vscode-server/bin`; `[.]` keeps pkill from matching its own.
      Invoke-InDistro -Arguments @('pkill', '-f', '[.]vscode-server/bin') -AllowFailure
      Start-Sleep -Seconds 2
    }

    $env:COAI_MEASURE = '1'
    $env:COAI_DATA_DIR = $remoteData
    $env:WSLENV = (@($previousEnv.WSLENV, 'COAI_MEASURE/u', 'COAI_DATA_DIR/u') | Where-Object { $_ }) -join ':'
    $remote = "wsl+$Distro"
    $common = @('--user-data-dir', $userData, '--extensions-dir', $extensions)
    try {
      # Installed in the distro (see the notes): an extension of the same id left by an earlier run must not be the one measured.
      Invoke-Native -File $Code -Arguments ($common + @('--remote', $remote, '--uninstall-extension', $CoaiId)) -AllowFailure
      Invoke-Native -File $Code -Arguments ($common + @('--remote', $remote, '--install-extension', $Vsix, '--install-extension', $driverVsix, '--force'))

      Invoke-Native -File $Code -Arguments ($common + @('--new-window', '--remote', $remote, $remoteFolder))
      $deadline = (Get-Date).AddSeconds($TimeoutSec)
      while (-not (Test-Path -LiteralPath $recordPath) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
      if (-not (Test-Path -LiteralPath $recordPath)) {
        $failures += "sample ${sample}: no record within $TimeoutSec s (did the window connect to $Distro? is the WSL extension installed?)"
        continue
      }
      $saved = Join-Path $OutDir "sample-$sample.json"
      Copy-Item -LiteralPath $recordPath -Destination $saved -Force
      $records += $saved
    } finally {
      Stop-WindowOf -UserData $userData
    }
  }
} finally {
  foreach ($name in $previousEnv.Keys) {
    if ($null -eq $previousEnv[$name]) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue } else { Set-Item "Env:$name" $previousEnv[$name] }
  }
  if (-not $KeepInstalled) {
    $cleanup = Join-Path $OutDir 'cleanup'
    New-Item -ItemType Directory -Force -Path "$cleanup\user-data", "$cleanup\extensions" | Out-Null
    foreach ($id in @($CoaiId, $DriverId)) {
      & $Code --user-data-dir "$cleanup\user-data" --extensions-dir "$cleanup\extensions" --remote "wsl+$Distro" --uninstall-extension $id 2>&1 | Out-Null
    }
    Stop-WindowOf -UserData "$cleanup\user-data"
  }
  Invoke-InDistro -Arguments @('rm', '-rf', $distroRun) -AllowFailure
}

# The verdict is the repository's own rule (scripts/coldstart-lib.mjs), the same one the CI jobs apply.
$result = Join-Path $OutDir 'result.json'
$reportArgs = @((Join-Path $PSScriptRoot 'coldstart-report.mjs'), '--samples', "$Samples", '--json', $result)
if ($AppendRow) {
  $reportArgs += @('--row', '--label', $Label)
  if ($Commit) { $reportArgs += @('--commit', $Commit) }
}
$reportArgs += $records
& node @reportArgs
$code = $LASTEXITCODE
foreach ($failure in $failures) { Write-Error $failure -ErrorAction Continue }

# The extension host must really have been in WSL: a window that fell back to a local host measured something else.
if ($code -eq 0 -and (Test-Path -LiteralPath $result)) {
  $location = (Get-Content -LiteralPath $result -Raw | ConvertFrom-Json).location
  if ($location -ne 'wsl') {
    Write-Error "the extension host ran at '$location', not in WSL: this is not a Remote-WSL measurement (see $result)" -ErrorAction Continue
    $code = 1
  }
}
Write-Host "result: $result"
if ($failures.Count -gt 0) { $code = 1 }
exit $code
