param([string]$OutputName = 'LaptopCare.exe')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
New-Item -ItemType Directory -Force (Join-Path $root 'bin') | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$iconArgs = @()
if (Test-Path -LiteralPath "$root\assets\LaptopCare.ico") { $iconArgs = @("/win32icon:$root\assets\LaptopCare.ico") }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /out:"$root\bin\$OutputName" @iconArgs /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.Web.Extensions.dll "$root\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host "Built $root\bin\$OutputName"
