#!/bin/bash
# Bundle the current RimWorld 1.7 build with the frozen 1.5 and 1.6 releases.

VERSION=$(grep -Po '(?<=SimpleVersion = ")[0-9\.]+' Source/Common/Version.cs)

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
  <v1.7>
    <li>/</li>
    <li>1.7</li>
  </v1.7>
  <v1.6>
    <li>/</li>
    <li>1.6</li>
  </v1.6>
  <v1.5>
    <li>/</li>
    <li>1.5</li>
  </v1.5>
</loadFolders>
EOF

GIT_COMMIT=$(git rev-parse --short HEAD 2>&1)
GIT_COMMIT_STATUS=$?
if [ $GIT_COMMIT_STATUS -eq 0 ]; then
  FULL_VERSION="${VERSION} (${GIT_COMMIT})"
else
  FULL_VERSION="${VERSION}"
  echo "WARN: Failed to check git commit: ${GIT_COMMIT}"
fi
sed -i "/<supportedVersions>/ a \ \ \ \ <li>1.6</li>" About/About.xml
sed -i "/<supportedVersions>/ a \ \ \ \ <li>1.5</li>" About/About.xml
sed -i "s/<modVersion>.*<\/modVersion>.*\$/<modVersion>${FULL_VERSION}<\/modVersion>/" About/About.xml

# Keep the current source dependencies separate from the older Prepatcher builds.
python3 - <<'PYTHON' || exit 1
import xml.etree.ElementTree as ET

metadata = ET.parse('About/About.xml')
root = metadata.getroot()
current = root.find('modDependencies')
root.remove(current)
current.tag = 'v1.7'
dependencies = ET.SubElement(root, 'modDependenciesByVersion')
dependencies.append(current)
for version in ('1.5', '1.6'):
    dependency = ET.SubElement(ET.SubElement(dependencies, f'v{version}'), 'li')
    ET.SubElement(dependency, 'packageId').text = 'zetrith.prepatcher'
    ET.SubElement(dependency, 'displayName').text = 'Prepatcher'
    ET.SubElement(dependency, 'steamWorkshopUrl').text = 'steam://url/CommunityFilePage/2934420800'
ET.SubElement(root.find('loadAfter'), 'li').text = 'zetrith.prepatcher'
metadata.write('About/About.xml', encoding='utf-8', xml_declaration=True)
PYTHON

# The current version
mkdir -p 1.7
cp -r ../1.7/Assemblies ../1.7/AssembliesCustom ../Defs ../Languages 1.7/
rm -f 1.7/Languages/.git 1.7/Languages/LICENSE 1.7/Languages/README.md

# Past versions
RELEASE_16=$(git ls-remote --heads https://github.com/rwmt/Multiplayer.git refs/heads/rw-1.6) || exit 1
if [ -n "$RELEASE_16" ]; then
  git clone -b rw-1.6 --depth=1 --single-branch --recurse-submodules https://github.com/rwmt/Multiplayer.git 1.6 || exit 1
else
  git init 1.6 || exit 1
  git -C 1.6 remote add origin https://github.com/rwmt/Multiplayer.git || exit 1
  git -C 1.6 fetch --depth=1 origin 9df94bd04ea9a94c00bd39a276e58c4c2f965740 || exit 1
  git -C 1.6 checkout --detach FETCH_HEAD || exit 1
  git -C 1.6 submodule update --init --recursive || exit 1
fi
# The 1.6 fallback contains source only, so compile it before retaining its files.
(cd 1.6 && dotnet build Source/Client/Multiplayer.csproj --configuration Release) || exit 1
git clone -b rw-1.5 --depth=1 --single-branch --recurse-submodules https://github.com/rwmt/Multiplayer.git 1.5 || { echo 'Git cloning 1.5 FAILED' ; exit 1; }
shopt -s extglob
shopt -s dotglob
for release in 1.5 1.6; do
  for assembly in Assemblies/MultiplayerLoader.dll AssembliesCustom/Multiplayer.dll AssembliesCustom/MultiplayerCommon.dll; do
    test -f "$release/$assembly" || { echo "Missing $release/$assembly"; exit 1; }
  done
  rm -rf -- "${release:?}"/!(Languages|Assemblies|AssembliesCustom|Defs)
  rm -f "$release/Languages/.git" "$release/Languages/LICENSE" "$release/Languages/README.md"
done

cd ..

# Zip for Github releases
rm -f "Multiplayer-v$VERSION.zip"
zip -r -q "Multiplayer-v$VERSION.zip" Multiplayer

echo "Ok, $PWD/Multiplayer-v$VERSION.zip ready for uploading to Workshop"
