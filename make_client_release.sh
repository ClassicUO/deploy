#!/bin/bash

target="$1"
release_name="$2"
beta="${3:-false}"
tag="${4:-ClassicUO-dev-release}" 

if [ -z "$target" ]; then
  echo "Error: No target specified."
  echo "Usage: $0 <target> <release_name> [beta] [tag]"
  echo "Valid targets: linux-x64, win-x64, osx-x64"
  echo "Beta: true or false (default: false)"
  echo "Tag: GitHub release tag (default: ClassicUO-dev-release)"
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
  echo "Usage: $0 <target> <release_name> [beta] [tag]"
  exit 1
fi

echo "Target is valid: $target"
echo "Using tag: $tag"
echo "Beta: $beta"

set -e

mkdir -p ./tmp_download
mkdir -p ./tmp_download/client-$target

# UPDATED: tag injected into the download URL
curl -L -o ./tmp_download/client-$target/release.zip \
  "https://github.com/ClassicUO/ClassicUO/releases/download/${tag}/ClassicUO-${target}-release.zip"

rm -rfd ./client/$target
mkdir -p ./client/$target
unzip ./tmp_download/client-$target/release.zip -d ./client/$target

rm -rfd ./tmp_download

cd ManifestCreator
dotnet run -- --bin ../client/$target --version "$release_name" --name "ClassicUO $release_name" --latest true --beta $beta --target $target --output ../client
cd ..

rm -rfd ./client/$target
