param(
    [Parameter(Mandatory = $false)]
    [string]$PublishDir = (Join-Path $PSScriptRoot "..\artifacts\win"),

    [Parameter(Mandatory = $false)]
    [string[]]$Keep = @('zh-CN','en-US')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PublishDir)) {
    throw "Publish directory not found: $PublishDir"
}

# Normalize Keep to an actual array.
# Handles:
# -Keep zh-CN,en-US
# -Keep "zh-CN","en-US"
# -Keep 'zh-CN','en-US'
$keepNormalized = @()
foreach ($k in $Keep) {
    if ([string]::IsNullOrWhiteSpace($k)) { continue }

    $keepNormalized += ($k -split ',' | ForEach-Object {
        $v = $_.Trim()
        # strip surrounding single/double quotes if present
        if ($v.Length -ge 2 -and (($v.StartsWith('"') -and $v.EndsWith('"')) -or ($v.StartsWith("'") -and $v.EndsWith("'")))) {
            $v = $v.Substring(1, $v.Length - 2)
        }
        $v
    } | Where-Object { $_ })
}

Write-Host "PublishDir: $PublishDir"
Write-Host "Keep cultures: $($keepNormalized -join ', ')"

$keepSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($k in $keepNormalized) { [void]$keepSet.Add($k) }

# Be tolerant to casing variants in output folders.
if ($keepSet.Contains('en-US')) { [void]$keepSet.Add('en-us') }
if ($keepSet.Contains('zh-CN')) { [void]$keepSet.Add('zh-cn') }

# Culture directory name pattern:
# - language: 2 letters
# - optional script: 4 letters (e.g., zh-Hans)
# - optional region: 2 letters or 3 digits (e.g., en-US, es-419)
# - optional extra variant segments (e.g., ca-Es-VALENCIA)
$pattern = '^[a-zA-Z]{2}(?:-[a-zA-Z]{4})?(?:-[a-zA-Z]{2}|-\d{3})?(?:-[a-zA-Z0-9]{2,8})*$'

Get-ChildItem -LiteralPath $PublishDir -Directory | ForEach-Object {
    $name = $_.Name

    # Heuristic: treat folders containing WinUI .mui as culture folders even if name is odd.
    $looksLikeMuiCulture = Test-Path -LiteralPath (Join-Path $_.FullName 'Microsoft.ui.xaml.dll.mui') -PathType Leaf -ErrorAction SilentlyContinue

    if (($name -match $pattern) -or $looksLikeMuiCulture) {
        if (-not $keepSet.Contains($name)) {
            Write-Host "Removing culture folder: $name"
            Remove-Item -LiteralPath $_.FullName -Recurse -Force
        }
        else {
            Write-Host "Keeping culture folder: $name"
        }
    }
}

Write-Host "Done."