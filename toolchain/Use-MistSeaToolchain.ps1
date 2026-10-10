$mistInstalled = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installed.windows-x64.json') -Raw | ConvertFrom-Json
$env:DOTNET_ROOT = $mistInstalled.dotnet_root
$env:DOTNET_ROOT_X64 = $mistInstalled.dotnet_root
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = $mistInstalled.nuget_packages
$env:PATH = $mistInstalled.dotnet_root + [IO.Path]::PathSeparator + $env:PATH
$script:mistGodotConsole = $mistInstalled.godot
function godot { & $script:mistGodotConsole @args }
