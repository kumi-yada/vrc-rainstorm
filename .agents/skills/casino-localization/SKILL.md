---
name: casino-localization
description: Use when adding, changing, or verifying localized text in this project's Casino scene (Casino/Casino.unity), including LocalizationManager keys, LanguageTable translations, LocalizedText labels, and untranslated UI strings such as RESHUFFLE, START, BUY, or REFUND.
---

# Casino Localization

This project uses a custom UdonSharp localization system, not Unity's Localization package. Scene data lives in `Casino/Casino.unity`.

## Architecture

- `Localization/LocalizationManager.cs`
  - Stores `tables` and `keys`.
  - `GetText(key)` finds the key index, reads that index from the current language table, falls back to `tables[0]` (English) if empty, then returns the raw key if nothing matches.
  - Key lookup is case-sensitive and expects exact string equality.
- `Localization/LanguageTable.cs`
  - One component per language: `languageCode`, `displayName`, and `texts`.
  - Every table must have the same number of entries in the same order as the manager's `keys`.
- `Localization/LocalizedText.cs`
  - Attached to a TextMeshProUGUI element.
  - On `Start`, if no key was set programmatically, it uses the authored TMP text as the key.
  - Dynamic labels call `SetKey`, `SetKeyAndSuffix`, `SetPrefixKeyAndSuffix`, etc.

Translations are stored in the scene instances, not in `Localization/LanguageTable.asset` or `Localization/LocalizationManager.asset`. Do not add translations to those `.asset` files; they are script/template assets.

## Current scene tables

The `Localization` scene object contains the manager and three tables:

| Language | Code | Table component |
| --- | --- | --- |
| English (fallback) | `en` | `&24147550` |
| Japanese | `ja` | `&1593721869` |
| Chinese | `zh-CN` | `&2010334513` |

The manager component is `&356741924`. Component IDs are stable in the current scene revision, but when in doubt locate by `languageCode:` and the `keys:` block rather than line numbers.

## Invariants

1. `LocalizationManager.keys` and every `LanguageTable.texts` array must have equal length.
2. **Append new entries at the end.** Never insert or reorder existing entries; that shifts every later index and breaks translations.
3. Add a new key to the manager **and** a translation to **every** language table.
4. If removing a string, remove the same index from the manager and every table.
5. Keys are uppercase constants with underscores. Examples: `START`, `DOUBLE_OR_NOTHING`, `NOTIFY_STOCK_EMPTY`, `RESHUFFLE`.
6. English is the fallback table. It must still contain the entry even if the key text is identical to the English value.

## Adding a new localized string

### 1. Add the key

In `Casino/Casino.unity`, find the `LocalizationManager` component (`&356741924`) and append to its `keys:` list:

```yaml
  keys:
  - ...
  - REFUND_CONFIRM
  - RESHUFFLE
```

The value must exactly match what `LocalizedText` or gameplay code passes to `GetText`/`SetKey`.

### 2. Add translations to every table

Append one entry to each `texts:` list at the same final index:

English (`languageCode: en`):

```yaml
  - RESHUFFLE
```

Japanese (`languageCode: ja`) uses YAML Unicode escapes for non-ASCII text:

```yaml
  - "\u914D\u308A\u76F4\u3057"   # 配り直し
```

Chinese (`languageCode: zh-CN`):

```yaml
  - "\u91CD\u65B0\u53D1\u724C"   # 重新发牌
```

Preserve the existing YAML style: English entries are usually plain, Japanese/Chinese entries are double-quoted `\uXXXX`. Escape quotes and keep multiline YAML indentation intact.

### 3. Wire the label

For a **new static label**:

- Add a `LocalizedText` component to the TextMeshProUGUI object.
- Ensure its `textElement` points at that TMP component.
- Ensure `localization` points at the `Localization` object's `LocalizationManager`, or leave it null and rely on the automatic `GameObject.Find("Localization")` fallback.
- The authored TMP text must equal the key, or the owning script must call `SetKey("KEY")`. Otherwise `LocalizedText.Start` uses the visible placeholder as the key and the label stays untranslated.

For an **existing label whose TMP text is already the key** (for example `Reshuffle.prefab` has `m_text: RESHUFFLE`), adding the key and translations is sufficient.

For a **dynamic label**, add the key/translations and call the appropriate `LocalizedText` setter from code. Do not rely on the TMP placeholder for keys set by `SetKey`.

## Worked example: RESHUFFLE

- Key appended to `LocalizationManager.keys`: `RESHUFFLE`
- English: `RESHUFFLE`
- Japanese: `配り直し`
- Chinese: `重新发牌`
- `Casino/Reshuffle.prefab` already displays `RESHUFFLE`, so its `LocalizedText` captures that as the key automatically.

## Editing safely

- Prefer Unity Inspector edits while the scene is open. If editing YAML directly, close the scene or Unity first so Unity does not overwrite the file on save.
- Do not use line numbers as anchors; search for `&356741924`, `languageCode:`, `keys:`, or the key value.
- Do not edit generated `.asset` UdonSharp assets to add translations.
- Scene YAML uses Unity tags and exact indentation. Keep two spaces before list markers under `keys:`/`texts:`, and four spaces for continuation lines.

## Validate after editing

Run from `Assets/`:

```bash
python3 - <<'PY'
import re

scene = open("Casino/Casino.unity", encoding="utf-8-sig").read()

m = re.search(r"\n  keys:\n((?:  - .*\n)+)", scene)
assert m, "LocalizationManager keys block not found"
keys = [line[4:] for line in m.group(1).splitlines()]

assert len(keys) == len(set(keys)), "duplicate keys"
print(f"keys: {len(keys)}")

for lang in ("en", "ja", "zh-CN"):
    start = scene.index(f"  languageCode: {lang}\n")
    end = scene.index("\n--- !u!", start)
    block = scene[start:end]
    count = len(re.findall(r"^  - ", block, re.M))
    print(f"{lang}: {count}")
    assert count == len(keys), f"{lang} has {count} entries, expected {len(keys)}"

print("OK: all tables match manager key count")
PY
```

Also confirm the new key appears once in `keys:` and once in the English `texts:` block:

```bash
rg -n "RESHUFFLE" Casino/Casino.unity
```

Expected: two literal matches in this example (manager key + English text). Japanese and Chinese entries are stored as `\uXXXX` escapes, so inspect the end of those `texts:` arrays separately.

## Common mistakes

- **Only added English.** Japanese/Chinese then fall back to English instead of showing a translation.
- **Added a translation but no key.** `GetText` returns the raw key string.
- **Inserted instead of appended.** Every later translation is now off by one.
- **Key mismatch.** `RESHUFFLE`, `Reshuffle`, and `RESHUFFLE ` are different keys.
- **Edited `LanguageTable.asset`.** That file only defines the script; the scene instances hold the actual translations.
- **New label placeholder differs from key.** `LocalizedText.Start` treats the placeholder as the key unless code calls `SetKey` first.
