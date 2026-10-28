---
description: Create a new release - tags the commit, summarizes changes, and updates CHANGELOG in all languages
agent: build
---

Create a new release for this project. Follow these steps exactly:

## 1. Determine the new version

Run these commands to find the current version:
```bash
# The git repository root is the Assets/ directory (where .git lives)
cd "$(git rev-parse --show-toplevel)"

# Get the latest version tag (tags follow the pattern v1.X)
LATEST_TAG=$(git tag --sort=-v:refname | head -1)
echo "Latest tag: $LATEST_TAG"
```

Parse the version number from the tag (e.g., `v1.4` → major=1, minor=4).
Increment the minor version by 1 to get the new version (e.g., `v1.4` → `v1.5`).
Set `NEW_VERSION` to the new tag (e.g., `v1.5`).

## 2. Get today's date

```bash
TODAY=$(date +%Y-%m-%d)
```

## 3. Collect and summarize changes

Get all commits between the latest tag and HEAD:
```bash
COMMITS=$(git log --oneline "${LATEST_TAG}..HEAD")
echo "$COMMITS"
```

Also get a diff summary to understand what files changed:
```bash
git diff --stat "${LATEST_TAG}..HEAD"
```

**Filter out unrelated commits** — exclude commits that are not part of the game release:
- `chore:` commits (e.g., "chore: release v1.4")
- Commits that only modify `.opencode/` (release commands, agent configs)
- Commits that only modify `CHANGELOG*.md` files (these are auto-generated)
- Commits that only modify documentation, README, or CI/CD configs
- Merge commits and empty commits

**Summarize the changes** into a concise bullet-point list. Group related commits together. Write for end users, not developers:

- Start each bullet with an action verb (Add, Fix, Improve, Remove, Disable, Allow, etc.)
- Explain **what the user experiences**, not the technical implementation
- Use clear, friendly language — avoid jargon like "pickup", "stock", "autofill" without context
- Be specific about which game mode or feature is affected
- Match the style of existing CHANGELOG entries

**Merge related changes into single bullets when appropriate:**
- Multiple fixes to the same feature → one bullet (e.g., "Fix card interaction issues")
- Multiple rule changes to the same game type → one bullet (e.g., "Adjust solitaire rules: ...")
- A minor translation + small visual tweaks → one bullet (e.g., "Various UI improvements including ...")
- Related new features → one bullet (e.g., "Add win streak multiplier and Double or Nothing betting")
- Aim for 3-5 bullets per release — avoid long lists of tiny changes

Example summary format:
```
- Adjust solitaire rules: disable auto-fill in Canfield, allow empty slots in Spider Solitaire
- Add win streak multiplier and Double or Nothing betting
- Fix picking up cards that are underneath a held card
- Various UI improvements including button translations and visual fixes
```

## 4. Update CHANGELOG files

There are 3 CHANGELOG files that must ALL be updated with the same version entry:
- `CHANGELOG.md` (English)
- `CHANGELOG-zh-CN.md` (Chinese Simplified)
- `CHANGELOG-ja.md` (Japanese)

The entry format is:
```markdown
## YYYY-MM-DD - vX.Y

- change description in English
- another change
```

**For each file:**
1. Read the current file to understand its structure and language
2. Add the new version entry at the TOP (right after the title line `# Changelog` / `# 更新日志` / `# 変更履歴`)
3. Translate the change summary into the appropriate language:
   - **English** (`CHANGELOG.md`): Use the summary as-is
   - **Chinese Simplified** (`CHANGELOG-zh-CN.md`): Translate to Simplified Chinese
   - **Japanese** (`CHANGELOG-ja.md`): Translate to Japanese

Match the existing style and tone of each language's entries. Keep translations natural and consistent with previous entries.

## 5. Commit the CHANGELOG updates

```bash
cd "$(git rev-parse --show-toplevel)"
git add CHANGELOG.md CHANGELOG-zh-CN.md CHANGELOG-ja.md
git commit -m "chore: release ${NEW_VERSION}"
```

## 6. Create the version tag

```bash
git tag -a "${NEW_VERSION}" -m "Release ${NEW_VERSION}"
```

## 7. Verify and report

Run these to verify everything worked:
```bash
git tag --sort=-v:refname | head -3
git log --oneline -3
head -10 CHANGELOG.md
```

Report the results to the user:
- The new version tag created
- The summary of changes included in the release
- Confirmation that all 3 CHANGELOG files were updated

---

**Important notes:**
- Tags follow semantic versioning pattern: `vMAJOR.MINOR` (e.g., `v1.0`, `v1.4`)
- Always increment the MINOR version for new releases if not specified otherwise
- All 3 CHANGELOG files MUST be updated - do not skip any language
- Match the existing formatting exactly (date format, bullet style, spacing)
- The new entry goes at the TOP of each changelog, right after the title
