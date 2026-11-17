$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:Microsoft.CSharp.dll',
    ('/out:' + (Join-Path $projectRoot 'ZHmaster.exe')),
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),
    ('/win32icon:' + (Join-Path $projectRoot 'ZHmaster.ico')))
$arguments += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output 'ZHmaster.exe built successfully.'
