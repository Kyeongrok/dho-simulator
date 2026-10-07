# gvdb 도시 쪽을 하나 받는다(차례는 queue.txt). 마지막으로 받은 지 12분이 안 됐으면 받지 않는다.
Set-Location C:\Users\ocean\git\dho
$dir='data\extracted\gvdb'; $q=(Get-Content "$dir\queue.txt" -Raw).Trim() -split '\s+' | Where-Object { $_ }
$last=(Get-ChildItem "$dir\town_*.html" | Sort-Object LastWriteTime | Select-Object -Last 1).LastWriteTime
if (((Get-Date)-$last).TotalMinutes -lt 12) { "WAIT last $($last.ToString('HH:mm'))"; return }
if ($q.Count -eq 0) { 'EMPTY'; return }
$id=$q[0]
try {
  $r = Invoke-WebRequest "http://gvdb.mydns.jp/db/module/TradeDB/action/TownShow?id=$id" -OutFile "$dir\town_$id.html" -TimeoutSec 40 -UserAgent 'Mozilla/5.0' -PassThru
  Set-Content "$dir\queue.txt" (($q | Select-Object -Skip 1) -join ' ') -Encoding ascii
  "GOT $id $((Get-Item "$dir\town_$id.html").Length) left $($q.Count-1)"
  python tools\gvo\gvdb_trade.py --write | Select-Object -Last 1
} catch { "FAIL $id $($_.Exception.Message)" }
