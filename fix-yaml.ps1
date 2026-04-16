$file = "d:\Automerge.Windows\.github\workflows\release.yml"
$content = [System.IO.File]::ReadAllText($file)
$before = ($content.ToCharArray() | Where-Object { [int]$_ -gt 127 }).Count
Write-Host "Non-ASCII before: $before"

# Replace box-drawing and other Unicode with ASCII equivalents
$fixed = $content
# Box drawing light horizontal U+2500
$fixed = $fixed.Replace([string][char]0x2500, '-')
# Em dash U+2014
$fixed = $fixed.Replace([string][char]0x2014, '-')
# Rightwards arrow U+2192
$fixed = $fixed.Replace([string][char]0x2192, '->')
# Left/right single/double quotes (might be in strings)
$fixed = $fixed.Replace([string][char]0x2018, "'")
$fixed = $fixed.Replace([string][char]0x2019, "'")
$fixed = $fixed.Replace([string][char]0x201C, '"')
$fixed = $fixed.Replace([string][char]0x201D, '"')

$after = ($fixed.ToCharArray() | Where-Object { [int]$_ -gt 127 }).Count
Write-Host "Non-ASCII after: $after"

[System.IO.File]::WriteAllText($file, $fixed, [System.Text.Encoding]::UTF8)
Write-Host "Done"
