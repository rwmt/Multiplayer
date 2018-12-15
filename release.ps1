$zipName = "Multiplayer.zip"
Remove-Item ../$zipName

$a = (Get-Content Source/Common/Version.cs) | Select-String -Pattern 'Version = "(.*)"'
$version = $a.Matches[0].Groups[1].Value
Write-Host 'Version:'$version

New-Item -ItemType directory -Path ../MultiplayerRelease/ -Force
Copy-Item "About","Assemblies","Defs","Languages" -Destination ../MultiplayerRelease/ -Recurse

$aboutFile = "../MultiplayerRelease/About/About.xml"
(Get-Content $aboutFile) -replace "{{version}}", $version | Set-Content $aboutFile

& 7z a ../$zipName ../MultiplayerRelease/
& 7z rn ../$zipName MultiplayerRelease Multiplayer