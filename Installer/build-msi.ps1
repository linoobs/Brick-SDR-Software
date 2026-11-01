# Builds BrickSDR-Setup_<version>.msi (in the folder above) from "Project Files/bin/x64/Release" with WiX 5.
# Needs: dotnet tool install --global wix --version 5.0.2, then: wix extension add -g WixToolset.Firewall.wixext/5.0.2
# The version is taken from BrickBranding.cs, so the exe, the .exe installer and the .msi always agree.
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$rel  = Join-Path $root 'Project Files\bin\x64\Release'
$work = Join-Path $env:TEMP 'bricksdr_msi_build'
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $work | Out-Null
$stage = Join-Path $work 'stage'
robocopy $rel $stage /E /XF *.pdb *.old.exe | Out-Null

$brand = Get-Content (Join-Path $root 'Project Files\Source\Console\BrickBranding.cs') -Raw
$ver = [regex]::Match($brand, 'Version\s*=\s*"([^"]+)"').Groups[1].Value
$vm = [regex]::Match($ver, '^(\d+)\.(\d+)(?:\.(\d+))?')
$build = if ($vm.Groups[3].Success) { $vm.Groups[3].Value } else { '0' }
$msiVer = $vm.Groups[1].Value + '.' + $vm.Groups[2].Value + '.' + $build      # the MSI version is numeric only: 1.4_hl2 becomes 1.4.0

$out = Join-Path $root ("BrickSDR-Setup_" + $ver + ".msi")
$ico = Join-Path $root 'Project Files\Source\Console\Resources\Brick-SDR-icon.ico'
$wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe'
& $wix build (Join-Path $root 'Installer\Product.wxs') -arch x64 -ext WixToolset.Firewall.wixext `
    -d "Version=$msiVer" -d "StageDir=$stage" -d "IconFile=$ico" `
    -pdbtype none -o $out
Get-Item $out | Select-Object Name, Length
