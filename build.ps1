$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
New-Item -ItemType Directory -Force (Join-Path $root 'bin') | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /out:"$root\bin\LaptopCare.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.Web.Extensions.dll "$root\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host "Built $root\bin\LaptopCare.exe"
