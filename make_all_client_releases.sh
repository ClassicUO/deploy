set -e

release_name="$1"
beta="${2:-false}"
tag="${3:-ClassicUO-dev-release}" 

if [ -z "$release_name" ]; then
  echo "Error: No release name specified."
  echo "Usage: $0 <release_name> [beta]"
  echo "Beta: true or false (default: false)"
  exit 1
fi

bash ./make_client_release.sh linux-x64 $release_name $beta $tag
bash ./make_client_release.sh win-x64 $release_name $beta $tag
bash ./make_client_release.sh osx-x64 $release_name $beta $tag

cd ManifestCreator
dotnet run -- --clean
cd ..