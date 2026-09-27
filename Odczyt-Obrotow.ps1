# Odczyt kanalu adaptacji 1 (obroty jalowe) sterownika silnika Audi 100 C4 2.6 ABC
# Wyciaga z bloku odpowiedzi adaptacji trojke pomiarowa (SensorID=1 -> 0.2*A*B rpm)
# Uzycie:  .\Odczyt-Obrotow.ps1 [-Powtorzen 8] [-Przerwa 8]

param(
    [int]$Powtorzen = 1,
    [int]$Przerwa   = 8
)

$env:KW1281_R6 = 0
$exe = Join-Path $PSScriptRoot 'kw1281test-FIXED.exe'

function Odczyt {
    $out = & $exe COM1 4800 1 AdaptationRead 1 2>$null
    $m = $out | Select-String -Pattern 'Adaptation Response" block:\s*(.+)$'
    if (-not $m) {
        if ($out | Select-String -Pattern 'NAK') { return [pscustomobject]@{ Ok=$false; Info='NAK' } }
        return [pscustomobject]@{ Ok=$false; Info='brak odpowiedzi' }
    }

    $bajty = ($m.Matches[0].Groups[1].Value.Trim() -split '\s+') | ForEach-Object { [Convert]::ToByte($_, 16) }
    # Body: [0]=kanal  [1..2]=wartosc kanalu  [3]=marker  [4..6]=(SensorID, A, B)
    $wartosc = $bajty[1] * 256 + $bajty[2]
    $rpm = $null
    if ($bajty.Count -ge 7 -and $bajty[4] -eq 1) {
        $rpm = 0.2 * $bajty[5] * $bajty[6]
    }

    [pscustomobject]@{
        Ok      = $true
        Kanal   = $bajty[0]
        Wartosc = $wartosc
        Rpm     = $rpm
        Hex     = ($bajty | ForEach-Object { $_.ToString('X2') }) -join ' '
    }
}

for ($i = 1; $i -le $Powtorzen; $i++) {
    $r = Odczyt
    $t = (Get-Date).ToString('HH:mm:ss')
    if ($r.Ok) {
        $rpmTxt = if ($null -ne $r.Rpm) { '{0,6:N1} obr/min' -f $r.Rpm } else { '   brak pomiaru' }
        Write-Output ('{0}  kanal {1}  wartosc {2,5}  {3}   [{4}]' -f $t, $r.Kanal, $r.Wartosc, $rpmTxt, $r.Hex)
    } else {
        Write-Output ('{0}  BLAD: {1}' -f $t, $r.Info)
    }
    if ($i -lt $Powtorzen) { Start-Sleep -Seconds $Przerwa }
}
