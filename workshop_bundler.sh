#!/bin/bash



VERSION=$(grep -Po '(?<=Version = ")[0-9\.]+' Source/Common/Version.cs)

git submodule update --init --recursive || { echo 'git submodule update FAILED' ; exit 1; }

cd Source || exit
dotnet build --configuration Release || { echo 'dotnet build FAILED' ; exit 1; }
cd ..

rm -rf Multiplayer/
mkdir -p Multiplayer
cd Multiplayer || exit

# About/ and Textures/ are shared between all versions
cp -r ../About ../Textures .

cat <<EOF > LoadFolders.xml
<loadFolders>
  <v1.5>
    <li>/</li>
    <li>1.5</li>
  </v1.5>
  <v1.4>
    <li>/</li>
    <li>1.4</li>
  </v1.4>
</loadFolders>
EOF


sed -i "/<supportedVersions>/ a \ \ \ \ <li>1.4</li>" About/About.xml
sed -i "/Multiplayer mod for RimWorld./aThis is version ${VERSION}." About/About.xml
sed -i "s/<version>.*<\/version>\$/<version>${VERSION}<\/version>/" About/Manifest.xml

# The current version
mkdir -p 1.5
cp -r ../Assemblies ../AssembliesCustom ../Defs ../Languages 1.5/
rm -f 1.5/Languages/.git 1.5/Languages/LICENSE 1.5/Languages/README.md

# Past versions
git clone -b rw-1.4 --depth=1 --single-branch --recurse-submodules https://github.com/rwmt/Multiplayer.git 1.4 || { echo 'Git cloning 1.4 FAILED' ; exit 1; }
shopt -s extglob
shopt -s dotglob
rm -rf -- 1.4/!(Languages|Assemblies|AssembliesCustom|Defs)
rm -f 1.4/Languages/.git 1.4/Languages/LICENSE 1.4/Languages/README.md

cd ..

# Zip for Github releases
rm -f Multiplayer-v$VERSION.zip
zip -r -q Multiplayer-v$VERSION.zip Multiplayer

echo "Ok, $PWD/Multiplayer-v$VERSION.zip ready for uploading to Workshop"
