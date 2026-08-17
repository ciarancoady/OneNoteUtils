# Incremental sync via JSON manifest with lastModifiedTime comparison

Sync is the default CLI mode. It compares OneNote's hierarchy and per-page `lastModifiedTime` against a `.onenote-sync.json` manifest stored in the output directory. New, modified, moved, renamed, or deleted pages trigger work. If no manifest exists, the first run exports the selected scope and creates one.

Section and page filters define the scope of a sync. Manifest entries outside that scope are not considered deleted and their exported files remain untouched. A page whose section or section-group path changes is treated as modified so its old files are removed before it is written to the new location.

We considered timestamp-only detection (no manifest — just compare file modification times on disk) but rejected it because: file mtime can drift from OneNote's modification time due to system clock differences or manual edits; renamed pages can't be detected without tracking the original page ID → filename mapping; and cleanup of associated images/attachments requires knowing which files belong to which page.

A database (SQLite) was considered instead of JSON but rejected as overkill for a flat page-ID-to-metadata mapping that rarely exceeds a few thousand entries.
