$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path ([IO.Path]::GetTempPath()) ('jj2-connection-' + [Guid]::NewGuid().ToString('N') + '.exe')
$sources = @('Jj2Models.cs', 'Jj2Packets.cs', 'jj2tcpClient.cs', 'jj2udpClient.cs',
    'JjAssetDownload.cs', 'JjBotBody.cs', 'JjLevelMap.cs', 'MapForm.cs', 'Theme.cs',
    'ServerConnectionForm.cs', 'ServerConnectionForm.Designer.cs', 'Jj2ServerList.cs') |
    ForEach-Object { Join-Path $root $_ }
try {
    & $compiler /nologo "/out:$output" /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        $sources (Join-Path $PSScriptRoot 'ConnectionRegressionTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Connection regression test compilation failed.' }
    & $output
    if ($LASTEXITCODE -ne 0) { throw 'Connection regression tests failed.' }
}
finally {
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output }
}
