param(
    [string]$AssetDirectory,
    [string]$NativeExecutable
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDirectory = Join-Path $root 'bin\HostingTests'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$output = Join-Path $testDirectory ('jj2-hosting-' + [Guid]::NewGuid().ToString('N') + '.exe')
$sources = @(Get-ChildItem -LiteralPath $root -Filter '*.cs' |
    Where-Object { $_.Name -notin 'Program.cs', 'Jj2Proxy.cs' } |
    ForEach-Object { $_.FullName })
$sources += @(Get-ChildItem -LiteralPath (Join-Path $root 'Properties') -Filter '*.cs' |
    ForEach-Object { $_.FullName })
try {
    & $compiler /nologo "/out:$output" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Configuration.dll `
        $sources (Join-Path $PSScriptRoot 'StandaloneServerTests.cs') (Join-Path $PSScriptRoot 'StandaloneInteropTests.cs') `
        (Join-Path $PSScriptRoot 'HotPotatoTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Hosting test compilation failed.' }
    $testArguments = @()
    if ($AssetDirectory) { $testArguments += $AssetDirectory }
    if ($NativeExecutable) {
        if (-not $AssetDirectory) { throw 'NativeExecutable requires AssetDirectory.' }
        $testArguments += $NativeExecutable
    }
    & $output @testArguments
    if ($LASTEXITCODE -ne 0) { throw 'Hosting tests failed.' }
}
finally {
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output }
}
