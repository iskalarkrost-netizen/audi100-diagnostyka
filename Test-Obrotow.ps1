# Testuje wartosc kanalu adaptacji 1 BEZ ZAPISU i podaje statystyki obrotow.
# Wykorzystuje komende AdaptationProbe (wlasna lata w kw1281test), ktora utrzymuje
# wartosc testowa aktywna i przy kazdej odpowiedzi zwraca aktualne obroty.
#
# Uzycie:  .\Test-Obrotow.ps1 -Wartosc 4 [-Sekund 20]

param(
    [Parameter(Mandatory = $true)][int]$Wartosc,
    [int]$Sekund = 20
)

$env:KW1281_R6 = 0
$exe = "C:\Users\iskal\OneDrive\Pulpit\kw1281test-src\kw1281test-master\bin\Release\net10.0\kw1281test.exe"

if (-not (Test-Path $exe)) { throw "Brak zbudowanego kw1281test.exe: $exe" }

$out = & $exe COM1 4800 1 AdaptationProbe 1 $Wartosc $Sekund 2>$null

$rpmy = @()
$wartosciKanalu = @()

foreach ($m in ($out | Select-String -Pattern 'Adaptation Response" block:\s*(.+)$')) {
    $bajty = ($m.Matches[0].Groups[1].Value.Trim() -split '\s+') | ForEach-Object { [Convert]::ToByte($_, 16) }
    if ($bajty.Count -ge 7 -and $bajty[4] -eq 1) {
        $wartosciKanalu += ($bajty[1] * 256 + $bajty[2])
        $rpmy += (0.2 * $bajty[5] * $bajty[6])
    }
}

if ($rpmy.Count -eq 0) {
    Write-Output "BRAK POMIAROW - sprawdz, czy silnik pracuje i czy kabel jest podlaczony"
    $out | Select-String -Pattern 'NAK|Error|Exception' | ForEach-Object { Write-Output $_.Line }
    return
}

$st = $rpmy | Measure-Object -Average -Minimum -Maximum
$kanalUnikalne = ($wartosciKanalu | Sort-Object -Unique) -join ','

Write-Output ''
Write-Output ("  wartosc kanalu potwierdzona przez ECU : {0}" -f $kanalUnikalne)
Write-Output ("  probek                                : {0}" -f $st.Count)
Write-Output ("  obroty srednio                        : {0:N1} obr/min" -f $st.Average)
Write-Output ("  obroty min / max                      : {0:N1} / {1:N1} obr/min" -f $st.Minimum, $st.Maximum)
Write-Output ''
