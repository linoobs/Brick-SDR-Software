# Builds BrickSDR-Setup_<version>.exe (in the folder above) from "Project Files/bin/x64/Release".
# Run it after building the solution.
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$rel  = Join-Path $root 'Project Files\bin\x64\Release'
$work = Join-Path $env:TEMP 'bricksdr_setup_build'
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $work | Out-Null
$stage = Join-Path $work 'stage'
robocopy $rel $stage /E /XF *.pdb *.old.exe | Out-Null
$ver = "1.3.5"
Set-Content (Join-Path $work 'version.txt') $ver -Encoding ASCII
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $work 'payload.zip'
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $root ("BrickSDR-Setup_" + $ver + ".exe")
$ico = Join-Path $root 'Project Files\Source\Console\Resources\Brick-SDR-icon.ico'
$inst = Join-Path $root 'Installer'
& $csc /nologo /target:winexe /optimize+ "/out:$out" "/win32icon:$ico" "/win32manifest:$inst\installer.manifest" `
  "/resource:$zip,payload.zip" "/resource:$work\version.txt,version.txt" `
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Microsoft.CSharp.dll /r:System.Core.dll `
  "$inst\Installer.cs"
Get-Item $out | Select-Object Name, Length
