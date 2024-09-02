set -e

release_name="$1"

if [ -z "$release_name" ]; then
  echo "Error: No release name specified."
  exit 1
fi

bash ./make_client_release.sh linux-x64 $release_name
bash ./make_client_release.sh win-x64 $release_name
bash ./make_client_release.sh osx-x64 $release_name

cd ManifestCreator
dotnet run -- --clean
cd ..