#!/bin/bash

target="$1"
release_name="$2"

if [ -z "$target" ]; then
  echo "Error: No target specified."
  echo "Usage: $0 <target>"
  echo "Valid targets: linux-x64, win-x64, osx-x64"
  exit 1
fi

# Validate the target
if [ "$target" != "linux-x64" ] && [ "$target" != "win-x64" ] && [ "$target" != "osx-x64" ]; then
  echo "Error: Invalid target '$target'."
  echo "Valid targets are: linux-x64, win-x64, osx-x64"
  exit 1
fi

if [ -z "$release_name" ]; then
  echo "Error: No release name specified."
  exit 1
fi

# If the target is valid, continue with the script
echo "Target is valid: $target"

set -e

mkdir -p ./tmp_download
mkdir -p ./tmp_download/client-$target
curl -L -o ./tmp_download/client-$target/release.zip https://github.com/ClassicUO/ClassicUO/releases/download/ClassicUO-dev-release/ClassicUO-$target-release.zip

rm -rfd ./client/$target
mkdir -p ./client/$target
unzip ./tmp_download/client-$target/release.zip -d ./client/$target

rm -rfd ./tmp_download

cd ManifestCreator
dotnet run -- --bin ../client/$target --version "$release_name" --name "ClassicUO $release_name" --latest true --target $target --output ../client
cd ..

rm -rfd ./client/$target