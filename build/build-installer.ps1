# Builds dist\EyeBreak-Setup.exe: the setup app with the whole Eye Break app (and its .NET runtime) attached.
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File build\build-installer.ps1
$ErrorActionPreference = 'Stop'
$root  = Split-Path $PSScriptRoot -Parent
$app   = "$root\src\EyeBreak\EyeBreak.csproj"
$setup = "$root\src\EyeBreak.Setup\EyeBreak.Setup.csproj"
$work  = "$root\artifacts"
$pub   = "$work\payload"
$zip   = "$work\payload.zip"
$dist  = "$root\dist"

Remove-Item $pub, $zip -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $work, $dist | Out-Null

# Self-contained: works on PCs without .NET 9 installed.
dotnet publish $app -c Release -r win-x64 --self-contained true -o $pub
if ($LASTEXITCODE) { throw 'Publishing Eye Break failed' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($pub, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

dotnet build $setup -c Release
if ($LASTEXITCODE) { throw 'Building the setup failed' }

# Setup file layout: [setup exe][app zip][zip length: 8 bytes][magic "EYEBRK01"]
$out = "$dist\EyeBreak-Setup.exe"
$fs = [IO.File]::Create($out)
try {
    foreach ($f in "$root\src\EyeBreak.Setup\bin\Release\net48\EyeBreakSetup.exe", $zip) {
        $in = [IO.File]::OpenRead($f); $in.CopyTo($fs); $in.Close()
    }
    $fs.Write([BitConverter]::GetBytes([long](Get-Item $zip).Length), 0, 8)
    $fs.Write([Text.Encoding]::ASCII.GetBytes('EYEBRK01'), 0, 8)
} finally { $fs.Close() }

"Built $out  ($([math]::Round((Get-Item $out).Length / 1MB, 1)) MB)"
