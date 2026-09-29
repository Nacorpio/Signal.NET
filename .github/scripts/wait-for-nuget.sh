#!/usr/bin/env bash
# Waits until every package in a directory is listed on nuget.org at the given version.
#
# "Pushed" is not "published": nuget.org accepts a push immediately, then validates and indexes the package
# asynchronously (usually 5-15 minutes, longer for new package IDs). This script polls the flat container
# index, which is what `dotnet restore` reads, until each package reports the version, or fails on timeout.
#
# Usage: wait-for-nuget.sh <version> <artifacts-dir> [timeout-seconds=2700] [interval-seconds=30]
set -euo pipefail

version="${1:?version required}"
artifacts="${2:?artifacts directory required}"
timeout="${3:-2700}"
interval="${4:-30}"

# NuGet normalizes IDs and versions to lower case in the flat container.
lower_version="$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')"

pending=()
for package in "$artifacts"/*.nupkg; do
  name="$(basename "$package" .nupkg)"
  id="${name%."$version"}"
  if [[ "$id" == "$name" ]]; then
    echo "::error::$name does not end with version $version"
    exit 1
  fi
  pending+=("$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')")
done

if [[ ${#pending[@]} -eq 0 ]]; then
  echo "::error::No packages found in $artifacts"
  exit 1
fi

echo "Waiting for ${#pending[@]} package(s) to be listed at $version: ${pending[*]}"
deadline=$(( $(date +%s) + timeout ))

while :; do
  still_pending=()
  for id in "${pending[@]}"; do
    if curl --silent --fail "https://api.nuget.org/v3-flatcontainer/$id/index.json" \
        | grep --quiet --fixed-strings "\"$lower_version\""; then
      echo "Listed: $id $version"
    else
      still_pending+=("$id")
    fi
  done
  pending=("${still_pending[@]+"${still_pending[@]}"}")

  if [[ ${#pending[@]} -eq 0 ]]; then
    echo "All packages are listed on nuget.org."
    exit 0
  fi

  if (( $(date +%s) >= deadline )); then
    echo "::error::Not listed on nuget.org after ${timeout}s: ${pending[*]}. Check the package owner's e-mail and nuget.org 'Manage Packages' for validation failures."
    exit 1
  fi

  echo "Still waiting for: ${pending[*]} (next check in ${interval}s)"
  sleep "$interval"
done
