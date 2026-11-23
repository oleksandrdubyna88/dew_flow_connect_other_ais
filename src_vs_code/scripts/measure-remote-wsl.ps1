<#
.SYNOPSIS
  Cold-start measurement of the ConnectOtherAIs extension in a Remote-WSL window, on the owner's Windows machine.

.DESCRIPTION
  v2 extension.md section 2 "Measurement" and section 10 row 3. Linux and Windows CI measure a LOCAL extension host; this is the third place:
  an extension host that runs inside WSL while the window runs on Windows. It cannot run in CI, so it is run by hand and its result goes into
  research/RESULTS_v2_start.md (-AppendRow).

  What it does, for each sample (3 by default), and every sample is its own launch:
    1. makes a FRESH --user-data-dir (and --extensions-dir) under -OutDir, and a fresh workspace folder and COAI data folder inside the distro;
    2. installs the coai VSIX and the tiny driver VSIX (scripts/measure-driver) with `code --remote wsl+<distro> --install-extension` into an ISOLATED
       extensions folder inside the distro's run folder (see the notes), never into the person's own ~/.vscode-server/extensions;
    3. launches `code --remote wsl+<distro> <fresh folder>` with COAI_MEASURE=1 (passed to the distro through WSLENV);
    4. the driver extension, running in the remote extension host, checks that the coai extension is NOT active, starts the clock, runs
       workbench.view.extension.coai, waits for a fresh `menuReady` (the real sidebar menu is shown and usable), reads the marks through `coai.__measure` and writes coai-measure-result.json
       in the workspace folder; the script reads it back over \\wsl.localhost\<distro>\...;
    5. closes the window; at the end it removes its run folder in the distro (which is where everything it installed lives).
  Then scripts/coldstart-report.mjs (node) applies the same rule as CI: the MAXIMUM M2 at most 2000 ms, M1 at most 100 ms, a fresh ack in every sample.
  The M2 limit follows the platform the records name (owner 2026-11-23: 3000 ms on Windows, 2000 ms on Linux). Remote-WSL runs the extension host on
  LINUX, so the records say `linux` and the limit here is 2000 ms, not the 3000 ms of the Windows CI job, although this script runs on Windows.
  The result names the extension-host location: `location` is `wsl` when the extension host really ran in WSL (the script fails otherwise), and
  every sample also records the platform and the path the extension was loaded from.

  Needs: Windows with VS Code (`code` on PATH) and the "WSL" remote extension, the distro, node on PATH, and `npm ci` done in src_vs_code (the
  driver VSIX is packed with the repository's own vsce). NOT run by CI and never run by the agent that wrote it: read the notes below first.

  NOTES YOU MUST KNOW BEFORE THE FIRST RUN
  * `--extensions-dir` and `--user-data-dir` are the LOCAL (Windows) side. The WSL server has its own folders, and the script NEVER touches the person's:
    it does not uninstall, install, enable, disable or replace any extension in ~/.vscode-server. The remote side is isolated with two environment variables
    that the VS Code server reads when it starts (read in the server's own code: `extensionsPath` takes `--extensions-dir`, then `VSCODE_EXTENSIONS`; its data
    folder takes `--server-data-dir`, then `VSCODE_AGENT_FOLDER`): VSCODE_EXTENSIONS=<run folder>/extensions and VSCODE_AGENT_FOLDER=<run folder>/server,
    both passed to the distro through WSLENV like COAI_MEASURE. The two VSIX are installed there, and the person's own ConnectOtherAIs (and its storage and
    enabled state) stays as it is. The server's program files (~/.vscode-server/bin) are only read: they are not extensions.
    The fresh user-data-dir is what makes the window start with no restored sidebar, and the fresh COAI_DATA_DIR inside the distro is what makes the
    extension start with no data of its own.
  * THREE GUARDS, because the isolation works only for a server that STARTS with those variables: (1) a VS Code server that already runs in the distro keeps its
    old environment, so without -StopServers the script refuses to start (it would install into the person's folder); (2) after the install it checks that the
    two extensions are in the isolated folder and stops before opening a window if they are not; (3) it reads the person's extensions folder (names and the
    sha256 of extensions.json) before the first sample and after the last, and fails the run, loudly, if they differ. It restores nothing, because it
    changes nothing.
  * This script cannot run in CI or on a machine without Windows + WSL + VS Code. The isolation was checked against the server's source and the WSL
    extension's scripts and the file was parse-checked; a first real run on the owner's machine is still the proof, and guards (2) and (3) are what say so if
    the isolation did not hold.
  * COAI_MEASURE=1 must be in the environment of the REMOTE VS Code server when it starts. A server that is already running (another window, even
    a closed one a few minutes ago) keeps its old environment, and then `coai.__measure` is not registered and the driver says so. -StopServers kills
    the VS Code servers in the distro first (it ends any VS Code window attached to that distro, the person's too: say so before you use it); without it, close
    those windows and wait, or run `wsl --terminate <distro>`; the script refuses to start while a server runs and -StopServers is not given.
  * Nothing is written outside -OutDir and the run's folder inside the distro. The one thing read outside them is the listing of ~/.vscode-server/extensions
    (guard 3).

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
  Do not remove the run's folder in the distro afterwards (it holds the isolated extensions folder with the two VSIX, the server data and the records). It is the run's, never the person's; the script prints its path.
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

function Read-InDistro {
  # The output of a command inside the distro, one string; empty when the command fails or prints nothing (a missing folder is an answer, not an error).
  param([string[]] $Arguments)
  $text = & wsl.exe -d $Distro --exec @Arguments 2>$null
  if ($LASTEXITCODE -ne 0 -or $null -eq $text) { return '' }
  return (($text | ForEach-Object { "$_".Trim() } | Where-Object { $_ }) -join "`n")
}

function Get-OwnerExtensionsSnapshot {
  # What the person's own extensions folder in the distro looks like: the names in it and the hash of extensions.json. Read only. Compared before and after.
  $folder = "$distroHome/.vscode-server/extensions"
  $names = Read-InDistro -Arguments @('ls', '-1', $folder)
  $hash = Read-InDistro -Arguments @('sha256sum', "$folder/extensions.json")
  return "names:`n$names`nextensions.json:`n$hash"
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
# The isolated remote side: where the VS Code server started for this run keeps its extensions and its data (see the notes). Both are inside the run folder.
$remoteExtensions = "$distroRun/extensions"
$remoteServerData = "$distroRun/server"
Invoke-InDistro -Arguments @('mkdir', '-p', $remoteExtensions, $remoteServerData)
# Guard 1: a server that already runs has the old environment, and an install through it would land in the person's own folder.
if (-not $StopServers) {
  & wsl.exe -d $Distro --exec pgrep -f '[.]vscode-server/bin' 2>&1 | Out-Null
  if ($LASTEXITCODE -eq 0) {
    Invoke-InDistro -Arguments @('rm', '-rf', $distroRun) -AllowFailure
    throw "a VS Code server is already running in ${Distro}: it keeps its old environment, so the isolated extensions folder would not be used and an install would reach YOUR extensions. Nothing was changed. Close the VS Code windows attached to $Distro and wait a few minutes, run 'wsl --terminate $Distro', or pass -StopServers (it ends those windows)."
  }
}
# Guard 3 (before): the person's extensions folder, read only.
$ownerBefore = Get-OwnerExtensionsSnapshot

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
$previousEnv = @{ COAI_MEASURE = $env:COAI_MEASURE; COAI_DATA_DIR = $env:COAI_DATA_DIR; VSCODE_EXTENSIONS = $env:VSCODE_EXTENSIONS; VSCODE_AGENT_FOLDER = $env:VSCODE_AGENT_FOLDER; WSLENV = $env:WSLENV }
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
    $env:VSCODE_EXTENSIONS = $remoteExtensions
    $env:VSCODE_AGENT_FOLDER = $remoteServerData
    $env:WSLENV = (@($previousEnv.WSLENV, 'COAI_MEASURE/u', 'COAI_DATA_DIR/u', 'VSCODE_EXTENSIONS/u', 'VSCODE_AGENT_FOLDER/u') | Where-Object { $_ }) -join ':'
    $remote = "wsl+$Distro"
    $common = @('--user-data-dir', $userData, '--extensions-dir', $extensions)
    try {
      # Installed in the isolated folder (see the notes). Nothing is uninstalled: the folder is new, so no extension of the same id can be in it, and the
      # person's own copy is not in it at all.
      Invoke-Native -File $Code -Arguments ($common + @('--remote', $remote, '--install-extension', $Vsix, '--install-extension', $driverVsix, '--force'))
      # Guard 2: both extensions must be in the isolated folder, or the isolation did not hold and no window is opened.
      $installed = Read-InDistro -Arguments @('ls', '-1', $remoteExtensions)
      if (-not ($installed -match [regex]::Escape($CoaiId)) -or -not ($installed -match [regex]::Escape($DriverId))) {
        throw "the install did not land in the isolated folder $remoteExtensions (it holds: '$installed'): the VS Code server did not read VSCODE_EXTENSIONS. No window was opened; check that your own extensions are untouched."
      }

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
  # Nothing of the person's was installed, so nothing is uninstalled: the run folder holds everything this run put in the distro.
  if ($KeepInstalled) {
    Write-Host "kept: $distroRun (in $Distro) holds the isolated extensions and server data of this run"
  } else {
    Invoke-InDistro -Arguments @('rm', '-rf', $distroRun) -AllowFailure
  }
  # Guard 3 (after): the person's extensions folder must read exactly as it did. It is here, in the finally, so it runs when a sample or an install failed too,
  # which is when it matters; the message is written at once because a throw in flight would hide a failure added to the list below.
  $ownerAfter = Get-OwnerExtensionsSnapshot
  if ($ownerAfter -ne $ownerBefore) {
    $ownerChanged = "THE EXTENSIONS FOLDER OF THE PERSON CHANGED during the run (~/.vscode-server/extensions in $Distro). Before:`n$ownerBefore`nAfter:`n$ownerAfter"
    Write-Error $ownerChanged -ErrorAction Continue
    $failures += $ownerChanged
  }
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
