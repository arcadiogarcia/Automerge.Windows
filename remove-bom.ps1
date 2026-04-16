$file = "d:\Automerge.Windows\.github\workflows\release.yml"
$bytes = [System.IO.File]::ReadAllBytes($file)
# Strip UTF-8 BOM (EF BB BF)
if ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
    $bytes = $bytes[3..($bytes.Length - 1)]
    [System.IO.File]::WriteAllBytes($file, $bytes)
    Write-Host "BOM removed. New size: $($bytes.Length) bytes"
} else {
    Write-Host "No BOM found"
}
