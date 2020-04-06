#!/bin/bash

mkdir -p Multiplayer
cp -r About Languages Multiplayer/
sed -i "/<supportedVersions>/ a \ \ \ \ <li>1.0</li>" Multiplayer/About/About.xml
sed -i "s/This is version .*\$/This is version $(grep -Po '(?<=Version = ")[0-9\.]+' Source/Common/Version.cs)./" Multiplayer/About/About.xml

rm -rf Multiplayer/1.1
mkdir -p Multiplayer/1.1
cp -r Assemblies Defs Multiplayer/1.1/

mkdir -p Multiplayer/1.0
git --work-tree=Multiplayer/1.0 checkout origin/rw-1.0 -- Assemblies Defs
git reset Assemblies Defs

rm -f Multiplayer.zip
zip -r -q Multiplayer.zip Multiplayer

echo "Ok, $PWD/Multiplayer.zip ready for uploading to Workshop"
