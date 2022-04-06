#!/bin/bash

VERSION=$(grep -Po '(?<=Version = ")[0-9\.]+' Source/Common/Version.cs)

cd Source
dotnet build --configuration Release
cd ..

git submodule update --init --recursive

mkdir -p Multiplayer
cp -r About Textures Multiplayer/
cat <<EOF > Multiplayer/LoadFolders.xml
<loadFolders>
  <v1.3.3311>
    <li>/</li>
    <li>1.3-1</li>
  </v1.3.3311>
  <v1.3>
    <li>/</li>
    <li>1.3-0</li>
  </v1.3>
  <v1.2>
    <li>/</li>
    <li>1.2</li>
  </v1.2>
  <v1.1>
    <li>/</li>
    <li>1.1</li>
  </v1.1>
  <v1.1>
    <li>/</li>
    <li>1.0</li>
  </v1.1>
</loadFolders>
EOF


sed -i "/<supportedVersions>/ a \ \ \ \ <li>1.2</li>" Multiplayer/About/About.xml
sed -i "/<supportedVersions>/ a \ \ \ \ <li>1.1</li>" Multiplayer/About/About.xml
sed -i "/<supportedVersions>/ a \ \ \ \ <li>1.0</li>" Multiplayer/About/About.xml
sed -i "/Multiplayer mod for RimWorld./aThis is version ${VERSION}." Multiplayer/About/About.xml
sed -i "s/<version>.*<\/version>\$/<version>${VERSION}<\/version>/" Multiplayer/About/Manifest.xml

rm -rf Multiplayer/1.3
mkdir -p Multiplayer/1.3
cp -r Assemblies Defs Languages Multiplayer/1.3/
rm -f Multiplayer/1.3/Languages/.git Multiplayer/1.3/Languages/LICENSE Multiplayer/1.3/Languages/README.md
mv Multiplayer/1.3 Multiplayer/1.3-1

mkdir -p Multiplayer/1.3
git --work-tree=Multiplayer/1.3 checkout origin/rw-1.3 -- Assemblies Defs
cp -r Languages Multiplayer/1.3/
git reset Assemblies Defs Languages
mv Multiplayer/1.3 Multiplayer/1.3-0

mkdir -p Multiplayer/1.2
git --work-tree=Multiplayer/1.2 checkout origin/rw-1.2 -- Assemblies Defs Languages
git reset Assemblies Defs Languages

mkdir -p Multiplayer/1.1
git --work-tree=Multiplayer/1.1 checkout origin/rw-1.1 -- Assemblies Defs Languages
git reset Assemblies Defs Languages

mkdir -p Multiplayer/1.0
git --work-tree=Multiplayer/1.0 checkout origin/rw-1.0 -- Assemblies Defs Languages
git reset Assemblies Defs Languages

rm -f Multiplayer-v$VERSION.zip
zip -r -q Multiplayer-v$VERSION.zip Multiplayer

echo "Ok, $PWD/Multiplayer-v$VERSION.zip ready for uploading to Workshop"
