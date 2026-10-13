param([switch]$Restore)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $project
$root = 'Assets/AK Studio Art/Modular Horror Corridor'
$backup = 'MaterialBackups/Corridor-Before-URP'
if ($Restore) {
    if (!(Test-Path "$backup/manifest.json")) { throw 'Backup manifest missing.' }
    $manifest = Get-Content "$backup/manifest.json" -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.materials) {
        Copy-Item -LiteralPath "$backup/$($entry.path)" -Destination $entry.path -Force
    }
    Write-Output 'Original corridor materials restored. Refresh Unity assets.'
    exit
}
if (Test-Path "$backup/manifest.json") { throw 'Backup already exists; refusing to replace original backup or repeat repair.' }
$extract = 'Temp/CorridorURPInspection'
New-Item -ItemType Directory -Path $extract -Force | Out-Null
& tar -xzf "$root/URP Extract Me.unitypackage" -C $extract
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect publisher URP archive.' }
$candidates = @()
foreach ($pathname in Get-ChildItem $extract -Filter pathname -Recurse) {
    $path = (Get-Content -LiteralPath $pathname.FullName -Raw).Trim()
    if (!$path.EndsWith('.mat')) { continue }
    if (!$path.StartsWith("$root/Materials/")) { throw "Unexpected material path: $path" }
    $source = Join-Path $pathname.Directory.FullName 'asset'
    $old = Get-Content -LiteralPath $path -Raw
    $new = Get-Content -LiteralPath $source -Raw
    if ($new -notmatch 'm_Shader: \{fileID: 4800000, guid: 933532a4fcc9baf4fa0491de14d08ed7, type: 3\}') { throw "Unexpected shader: $path" }
    $meta = Get-Content -LiteralPath "$path.meta" -Raw
    if ($meta -notmatch "guid: $($pathname.Directory.Name)") { throw "Material GUID mismatch: $path" }
    # Verify every original texture slot (including UV scale/offset), plus color and
    # key appearance controls, agrees with the publisher's URP variant before copying.
    foreach ($slot in [regex]::Matches($old, '(?m)^    - (\w+):\r?\n        m_Texture: [^\r\n]+\r?\n        m_Scale: [^\r\n]+\r?\n        m_Offset: [^\r\n]+')) {
        $expected = $slot.Value.Replace('_MainTex:', '_BaseMap:').Replace("`r`n", "`n")
        if (!$new.Replace("`r`n", "`n").Contains($expected)) { throw "Texture or UV mismatch: $path $($slot.Groups[1].Value)" }
    }
    foreach ($property in @('_Color','_EmissionColor','_BumpScale','_Metallic','_Cutoff','_OcclusionStrength','_SmoothnessTextureChannel')) {
        $line = [regex]::Match($old, "(?m)^    - ${property}: [^\r\n]+").Value
        if ($line -and !$new.Contains($line.Replace('_Color:', '_BaseColor:'))) { throw "Property mismatch: $path $property" }
    }
    $candidates += [pscustomobject]@{ path = $path; source = $source; sha256 = (Get-FileHash -LiteralPath $path).Hash }
}
if ($candidates.Count -ne (Get-ChildItem "$root/Materials" -Recurse -Filter '*.mat').Count) { throw 'Archive does not cover all corridor materials.' }
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$protected = @(Get-ChildItem Assets,ProjectSettings,Packages -Recurse -File | Where-Object { $_.Extension -ne '.mat' } | ForEach-Object {
    [pscustomobject]@{ path = [IO.Path]::GetRelativePath($project, $_.FullName).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
foreach ($entry in $candidates) {
    $destination = Join-Path $backup $entry.path
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $entry.path -Destination $destination
    Copy-Item -LiteralPath "$($entry.path).meta" -Destination "$destination.meta"
}
[pscustomobject]@{ created = (Get-Date -Format o); materials = $candidates; protected = $protected } | ConvertTo-Json -Depth 5 | Set-Content "$backup/manifest.json"
foreach ($entry in $candidates) { Copy-Item -LiteralPath $entry.source -Destination $entry.path -Force }
# This optional height-map GUID is absent from the original pack. Keep the
# original reference in the backup, but remove the dangling live reference.
$concrete = "$root/Materials/Materials General/Concrete Wall.mat"
$content = [IO.File]::ReadAllText((Join-Path $project $concrete))
$content = $content.Replace('m_Texture: {fileID: 2800000, guid: a227a6a0ba6688f4498c494cf575d886, type: 3}', 'm_Texture: {fileID: 0}')
[IO.File]::WriteAllText((Join-Path $project $concrete), $content)
Write-Output "Restored $($candidates.Count) publisher-authored URP materials in place; retained all material GUIDs and textures. Backup: $backup"
