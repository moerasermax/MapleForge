param(
    [int]$Port = 9999,
    [UInt16]$Version = 113,
    [string]$Patch = "1",
    [byte[]]$RecvIv = @(0x46, 0x72, 0x7A, 0x01),
    [byte[]]$SendIv = @(0x52, 0x30, 0x78, 0x01),
    [byte]$Locale = 6,
    [int]$ReadTimeoutMs = 5000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Add-LeShort {
    param(
        [System.Collections.Generic.List[byte]]$Target,
        [int]$Value
    )

    $Target.Add([byte]($Value -band 0xFF))
    $Target.Add([byte](($Value -shr 8) -band 0xFF))
}

function New-HelloPacket {
    param(
        [UInt16]$Version,
        [string]$Patch,
        [byte[]]$RecvIv,
        [byte[]]$SendIv,
        [byte]$Locale
    )

    if ($RecvIv.Length -ne 4) { throw "RecvIv must be 4 bytes." }
    if ($SendIv.Length -ne 4) { throw "SendIv must be 4 bytes." }

    $patchBytes = [System.Text.Encoding]::ASCII.GetBytes($Patch)

    $payload = [System.Collections.Generic.List[byte]]::new()
    Add-LeShort -Target $payload -Value $Version
    Add-LeShort -Target $payload -Value $patchBytes.Length
    $payload.AddRange($patchBytes)
    $payload.AddRange($RecvIv)
    $payload.AddRange($SendIv)
    $payload.Add($Locale)

    $packet = [System.Collections.Generic.List[byte]]::new()
    Add-LeShort -Target $packet -Value $payload.Count
    $packet.AddRange($payload)

    return ,$packet.ToArray()
}

function To-Hex {
    param([byte[]]$Bytes)
    if ($Bytes.Length -eq 0) { return '' }
    return [BitConverter]::ToString($Bytes).Replace('-', ' ')
}

$hello = New-HelloPacket -Version $Version -Patch $Patch -RecvIv $RecvIv -SendIv $SendIv -Locale $Locale

Write-Host "[test-hello] listen on 0.0.0.0:$Port"
Write-Host "[test-hello] hello bytes ($($hello.Length)): $(To-Hex -Bytes $hello)"
Write-Host "[test-hello] format = [payloadLen:2][version:2][patchLen:2][patch:?][recvIv:4][sendIv:4][locale:1]"

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, $Port)
$listener.Start()

$client = $null
try {
    $client = $listener.AcceptTcpClient()
    Write-Host "[test-hello] client connected: $($client.Client.RemoteEndPoint)"

    $stream = $client.GetStream()
    $stream.ReadTimeout = $ReadTimeoutMs

    $stream.Write($hello, 0, $hello.Length)
    $stream.Flush()
    Write-Host "[test-hello] hello sent"

    $all = [System.Collections.Generic.List[byte]]::new()
    $buf = New-Object byte[] 4096

    while ($true) {
        try {
            $read = $stream.Read($buf, 0, $buf.Length)
        }
        catch [System.IO.IOException] {
            Write-Host "[test-hello] read timeout ($ReadTimeoutMs ms), stop capture"
            break
        }

        if ($read -le 0) {
            Write-Host "[test-hello] client closed connection"
            break
        }

        $chunk = New-Object byte[] $read
        [Array]::Copy($buf, 0, $chunk, 0, $read)
        $all.AddRange($chunk)
        Write-Host "[test-hello] recv $read bytes: $(To-Hex -Bytes $chunk)"
    }

    Write-Host "[test-hello] total recv $($all.Count) bytes"
    if ($all.Count -gt 0) {
        Write-Host "[test-hello] total hex: $(To-Hex -Bytes $all.ToArray())"
    }
}
finally {
    if ($client) { $client.Dispose() }
    $listener.Stop()
    Write-Host "[test-hello] stopped"
}
