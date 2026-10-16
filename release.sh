#!/usr/bin/env bash
set -euo pipefail

CHANGELOG="CHANGELOG.md"

last_tag="$(git describe --tags --abbrev=0)"
echo "Last tag: $last_tag"

if [ "$#" -ge 1 ]; then
  new_version="$1"
else
  minor="$(echo "$last_tag" | sed -E 's/^v[0-9]+\.([0-9]+)$/\1/')"
  major="$(echo "$last_tag" | sed -E 's/^v([0-9]+)\.[0-9]+$/\1/')"
  new_version="v${major}.$((minor + 1))"
fi
echo "New version: $new_version"

commits="$(git log "${last_tag}..HEAD" --pretty=format:'- %s')"
if [ -z "$commits" ]; then
  echo "No commits since $last_tag. Nothing to release."
  exit 1
fi

date_str="$(date +%Y-%m-%d)"

entry="## ${date_str} - ${new_version}

${commits}

"

tmp="$(mktemp)"
{
  sed -n '1,2p' "$CHANGELOG"
  printf '%s' "$entry"
  sed -n '3,$p' "$CHANGELOG"
} > "$tmp"

mv "$tmp" "$CHANGELOG"

git add "$CHANGELOG"
git commit -m "chore: release ${new_version}"
git tag "$new_version"

echo "Done. Changelog updated, committed, and tagged ${new_version}."
