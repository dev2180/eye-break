# Builds dist\EyeBreak.msix for the Microsoft Store. The Store signs it on upload, so it isn't signed here.
# Run:   powershell -ExecutionPolicy Bypass -File build\build-msix.ps1
# Test locally (needs Developer Mode) before uploading:
#        powershell -ExecutionPolicy Bypass -File build\build-msix.ps1 -Install
param([switch]$Install)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app  = "$root\artifacts\msix\EyeBreak"
$dist = "$root\dist"

$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName | Select-Object -Last 1 -ExpandProperty FullName
if (-not $makeappx) { throw 'makeappx.exe not found: install the Windows 10/11 SDK' }

# The Store needs a four-part version ending in .0
$version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version + '.0'
$id = Get-Content "$PSScriptRoot\msix\store-identity.json" -Raw | ConvertFrom-Json

Remove-Item $app -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\src\EyeBreak\EyeBreak.csproj" -c Release -r win-x64 --self-contained true -o $app
if ($LASTEXITCODE) { throw 'Publishing Eye Break failed' }

# Tile and Store images, drawn by the app's own logo code
& "$app\EyeBreak.exe" --write-store-logos "$app\Images" | Out-Null
$deadline = (Get-Date).AddSeconds(30)
while (-not (Test-Path "$app\Images\Wide310x150Logo.png") -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
if (-not (Test-Path "$app\Images\Wide310x150Logo.png")) { throw 'Writing the Store images failed' }

(Get-Content "$PSScriptRoot\msix\AppxManifest.xml" -Raw).
    Replace('$NAME$', $id.Name).Replace('$PUBLISHER_DISPLAY_NAME$', $id.PublisherDisplayName).
    Replace('$PUBLISHER$', $id.Publisher).Replace('$VERSION$', $version) |
    Set-Content "$app\AppxManifest.xml" -Encoding UTF8

New-Item -ItemType Directory -Force $dist | Out-Null
$out = "$dist\EyeBreak.msix"
& $makeappx pack /d $app /p $out /o | Select-Object -Last 2
if ($LASTEXITCODE) { throw 'makeappx failed' }
"Built $out  ($([math]::Round((Get-Item $out).Length / 1MB, 1)) MB, version $version)"

if ($Install) {
    # Registers the unpacked folder, so no signing is needed (Developer Mode must be on)
    Get-AppxPackage -Name $id.Name | Remove-AppxPackage
    Add-AppxPackage -Register "$app\AppxManifest.xml"
    "Installed for testing: $((Get-AppxPackage -Name $id.Name).PackageFullName)"
}
