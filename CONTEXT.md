# OneNoteUtils

A bi-directional sync tool between OneNote and Obsidian-compatible Markdown. Pulls notebooks to Obsidian with incremental sync, or pushes markdown files back to OneNote.

## Language

**Notebook**:
A OneNote notebook — the top-level container identified by name or folder path. Contains **Sections** and **Section Groups**.
_Avoid_: binder, project

**Section Group**:
A named folder of **Sections** and/or nested **Section Groups** inside a **Notebook**. Maps to nested folders in the export output.
_Avoid_: folder, category

**Section**:
A named grouping of pages inside a **Notebook** or **Section Group** (maps to a OneNote section/tab).
_Avoid_: tab, category

**Page**:
A single OneNote page within a **Section**. Has a title, content, and a level indicating its depth in the parent–child hierarchy.
_Avoid_: note, document, file

**Page Level**:
An integer (1 = root) that encodes the parent–child nesting of **Pages** within a **Section**. A level-2 page is a child of the nearest preceding level-1 page.
_Avoid_: indent, depth

**Content Element**:
A discrete unit of content inside a **Page** — a heading, paragraph, list, table, image, attachment, code block, checkbox, blockquote, or horizontal rule. Content elements are ordered and can nest recursively (e.g. an image inside a list item).
_Avoid_: block, node, component

**Run**:
A span of inline text within a paragraph with uniform formatting (bold, italic, strikethrough, underline, code, highlight, hyperlink). A paragraph contains one or more **Runs**.
_Avoid_: span, fragment, chunk

**Source**:
The component that reads and writes raw data to/from OneNote (currently via COM Interop). Produces hierarchy and page XML for pulls; accepts page XML for pushes.
_Avoid_: reader, provider, connector

**Parser**:
The component that transforms raw OneNote XML into the domain model (**Notebook** → **Section** → **Page** → **Content Element** tree).
_Avoid_: transformer, converter, mapper

**Writer**:
The component that serializes the domain model to a specific output format (currently Obsidian Markdown). Responsible for file layout, link syntax, and presentation concerns like subpage link sections.
_Avoid_: exporter, renderer, formatter

**Export**:
The end-to-end process of reading a **Notebook** from a **Source**, parsing it, and writing it via a **Writer** to disk. A full **Export** overwrites all output files unconditionally.
_Avoid_: conversion, migration

**Sync**:
An incremental **Export** that compares OneNote's hierarchy and `lastModifiedTime` against a **Sync Manifest** and only re-exports **Pages** that are new, modified, moved, renamed, or deleted since the last run. OneNote is the source of truth; Obsidian is a read-only mirror. This is the "pull" direction.
_Avoid_: backup, replication

**Push**:
The reverse of **Sync** — takes one or more Markdown files from Obsidian and creates or updates **Pages** in a OneNote **Notebook** and **Section**. Used to share notes with a team that works in OneNote.
_Avoid_: upload, import

**Sync Manifest**:
A JSON file (`.onenote-sync.json`) stored in the output directory that records which **Pages** were last synced (pulled) and which Markdown files were last pushed. Used by **Sync** and **Push** to detect changes, match pages by ID, and clean up stale files.
_Avoid_: state file, cache, database

**Page Filter**:
An optional sync/export scope that selects **Pages** by exact title or OneNote page ID. Multiple filters may be supplied. Pages outside the filter remain untouched in the **Sync Manifest** and on disk.
_Avoid_: search, query

**Section Filter**:
An optional sync/export scope that selects **Sections** by exact name. Multiple filters may be supplied. Sections outside the filter remain untouched in the **Sync Manifest** and on disk.
_Avoid_: folder filter, category filter

## Relationships

- A **Notebook** contains **Sections** and **Section Groups**
- A **Section Group** contains **Sections** and/or nested **Section Groups**
- A **Section** contains zero or more **Pages**
- A **Page** contains an ordered list of **Content Elements**
- A **Content Element** may recursively contain other **Content Elements** (e.g. a table cell or list item containing images and formatted text)
- A paragraph **Content Element** contains one or more **Runs**
- **Page Level** determines the parent–child tree of **Pages** within a **Section**
- A **Sync** reads the **Sync Manifest**, compares timestamps, and re-exports only changed **Pages** (pull direction)
- A **Push** reads Markdown files, converts them to **Content Elements**, and writes them to OneNote via the **Source** (push direction)
- A **Sync Manifest** belongs to one output directory and tracks both pulled **Pages** and pushed Markdown files
- A **Page Filter** narrows an **Export** or **Sync** without treating excluded **Pages** as deleted
- A **Section Filter** narrows an **Export** or **Sync** without treating excluded **Sections** as deleted

## Example dialogue

> **Dev:** "When the **Parser** encounters a page with **Page Level** 2, how does it know which page is the parent?"
> **Domain expert:** "It walks the preceding pages in document order — the parent is the nearest earlier page whose **Page Level** is one less."

> **Dev:** "Should the **Writer** flatten all **Pages** into one folder?"
> **Domain expert:** "No — a **Page** with children becomes a subfolder. The **Writer** also appends subpage links, but that's a presentation concern, not part of the domain model."

## Flagged ambiguities

- "export" vs "sync" — resolved: **Export** is a full overwrite; **Sync** is incremental using a **Sync Manifest**. Sync is the default CLI behaviour; `--full` forces an Export.
- "sync" vs "push" — resolved: **Sync** is the pull direction (OneNote → Obsidian); **Push** is the reverse (Obsidian → OneNote). Both are explicit operations.
- "converter" was used to mean both the XML-to-model transformation and the model-to-Markdown serialization — resolved: the former is the **Parser**, the latter is the **Writer**.
