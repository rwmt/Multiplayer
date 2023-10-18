#!/bin/bash

VERSION=$(grep -Po '(?<=Version = ")[0-9\.]+' Source/Common/Version.cs)

git submodule update --init --recursive

cd Source
dotnet build --configuration Release
cd ..

rm -rf Multiplayer/
mkdir -p Multiplayer
cd Multiplayer

# About/ and Textures/ are shared between all versions
cp -r ../About ../Textures .

mkdir ../Assemblies
mkdir ../AssembliesCustom

cat <<EOF > LoadFolders.xml
<loadFolders>
  <v1.4>
    <li>/</li>
    <li>1.4</li>
  </v1.4>
  <v1.3.3311>
    <li>/</li>
    <li>1.3.3311</li>
  </v1.3.3311>
  <v1.3>
    <li>/</li>
    <li>1.3</li>
  </v1.3>
</loadFolders>
EOF


sed -i "/<supportedVersions>/ a \ \ \ \ <li>1.3</li>" About/About.xml
sed -i "/Multiplayer mod for RimWorld./aThis is version ${VERSION}." About/About.xml
sed -i "s/<version>.*<\/version>\$/<version>${VERSION}<\/version>/" About/Manifest.xml

# The current version
mkdir -p 1.4
cp -r ../Assemblies ../AssembliesCustom ../Defs ../Languages 1.4/
rm -f 1.4/Languages/.git 1.4/Languages/LICENSE 1.4/Languages/README.md

# Past versions
mkdir -p 1.3.3311
git --work-tree=1.3.3311 restore --recurse-submodules --source=origin/rw-1.3.3311 -- Assemblies Defs Languages
rm -f 1.3.3311/Languages/.git 1.3.3311/Languages/LICENSE 1.3.3311/Languages/README.md

mkdir -p 1.3
git --work-tree=1.3 restore --recurse-submodules --source=origin/rw-1.3 -- Assemblies Defs Languages
rm -f 1.3/Languages/.git 1.3/Languages/LICENSE 1.3/Languages/README.md

cd ..

# Zip for Github releases
rm -f Multiplayer-v$VERSION.zip
zip -r -q Multiplayer-v$VERSION.zip Multiplayer

echo "Ok, $PWD/Multiplayer-v$VERSION.zip ready for uploading to Workshop"
