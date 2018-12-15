$zipName = "Multiplayer.zip"
Remove-Item ../$zipName

$a = (Get-Content Source/Common/Version.cs) | Select-String -Pattern 'Version = "(.*)"'
$version = $a.Matches[0].Groups[1].Value
Write-Host 'Version:'$version

Copy-Item "About/About.xml" -Destination $env:temp

$aboutTemp = Join-Path $env:temp "About.xml"
(Get-Content $aboutTemp) -replace "{{version}}", $version | Set-Content $aboutTemp

& 7z a ../$zipName $aboutTemp About/Preview.png Assemblies Defs Languages
& 7z rn ../$zipName About.xml About/About.xml