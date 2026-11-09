# Print the last N bytes of a file as raw bytes (session transcripts can be tens of MB).
param([string]$Path, [int]$Bytes = 800000)
$fs = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
try {
    $start = [Math]::Max([long]0, $fs.Length - $Bytes)
    $fs.Seek($start, 'Begin') | Out-Null
    $buf = New-Object byte[] ([int]($fs.Length - $start))
    $n = 0
    while ($n -lt $buf.Length) {
        $r = $fs.Read($buf, $n, $buf.Length - $n)
        if ($r -le 0) { break }
        $n += $r
    }
} finally { $fs.Close() }
$out = [Console]::OpenStandardOutput()
$out.Write($buf, 0, $n)
$out.Flush()
