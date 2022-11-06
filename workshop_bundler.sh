#!/bin/bash

VERSION=$(grep -Po '(?<=Version = ")[0-9\.]+' Source/Common/Version.cs)

cd Source
dotnet build --configuration Release
cd ..

git submodule update --init --recursive

rm -rf Multiplayer/
mkdir -p Multiplayer

# About/ and Textures/ are shared between all versions
cp -r About Textures Multiplayer/

cat <<EOF > Multiplayer/LoadFolders.xml
<loadFolders>
  <v1.4>
    <li>/</li>
    <li>1.4</li>
  </v1.4>
  <v1.3.3311>
    <li>/</li>
    <li>1.3</li>
  </v1.3.3311>
  <v1.2>
    <li>/</li>
    <li>1.2</li>
  </v1.2>
</loadFolders>
EOF


sed -i "/<supportedVersions>/ a \ \ \ \ <li>1.3</li>" Multiplayer/About/About.xml
sed -i "/<supportedVersions>/ a \ \ \ \ <li>1.2</li>" Multiplayer/About/About.xml
sed -i "/Multiplayer mod for RimWorld./aThis is version ${VERSION}." Multiplayer/About/About.xml
sed -i "s/<version>.*<\/version>\$/<version>${VERSION}<\/version>/" Multiplayer/About/Manifest.xml

# The current version
rm -rf Multiplayer/1.4
mkdir -p Multiplayer/1.4
cp -r Assemblies Defs Languages Multiplayer/1.4/
rm -f Multiplayer/1.4/Languages/.git Multiplayer/1.4/Languages/LICENSE Multiplayer/1.4/Languages/README.md

# Past versions
mkdir -p Multiplayer/1.3
git --work-tree=Multiplayer/1.3 checkout origin/rw-1.3.3311 -- Assemblies Defs Languages
git reset Assemblies Defs Languages

mkdir -p Multiplayer/1.2
git --work-tree=Multiplayer/1.2 checkout origin/rw-1.2 -- Assemblies Defs Languages
git reset Assemblies Defs Languages

# Zip for Github releases
rm -f Multiplayer-v$VERSION.zip
zip -r -q Multiplayer-v$VERSION.zip Multiplayer

echo "Ok, $PWD/Multiplayer-v$VERSION.zip ready for uploading to Workshop"
