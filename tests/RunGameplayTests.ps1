$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path ([IO.Path]::GetTempPath()) ('jj2-gameplay-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler /nologo "/out:$output" /r:System.Drawing.dll `
        (Join-Path $root 'JjBotBody.cs') (Join-Path $root 'JjLevelMap.cs') `
        (Join-Path $root 'Jj2Packets.cs') (Join-Path $PSScriptRoot 'GameplayTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Gameplay test compilation failed.' }
    & $output
    if ($LASTEXITCODE -ne 0) { throw 'Gameplay tests failed.' }
}
finally {
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output }
}
