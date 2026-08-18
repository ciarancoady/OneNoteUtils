# OneNoteUtils

Bi-directional sync between OneNote and Obsidian-compatible Markdown. Pull notebooks to Obsidian with incremental sync, or push markdown files back to OneNote for your team.

## Features

### Pull (OneNote → Obsidian)

- **Incremental sync** — only fetches and exports pages that changed since the last run
- **Section groups** — full nested section group hierarchy exported as nested folders
- **Full content export** — headings, paragraphs, bold/italic/strikethrough/underline/links
- **Inline code & code blocks** — monospace font detection auto-wraps in backticks/fences
- **Kusto/KQL detection** — query patterns grouped into fenced code blocks
- **Checkboxes** — OneNote To-Do tags → `- [x]` / `- [ ]` markdown checkboxes
- **Highlights** — background-color styles → `==highlight==` (Obsidian syntax)
- **Internal links** — `onenote://` URLs → `[[wikilinks]]`
- **Ink/pen drawings** — pages with ink auto-export a companion PDF embedded as `![[page.pdf]]`
- **Images & attachments** — extracted into `_attachments/` subfolder with Obsidian `![[embed]]` syntax
- **Tables** — rendered as markdown tables; single-column "container" tables rendered as content blocks
- **Nested lists** — bullet and numbered lists with correct indentation
- **Page hierarchy** — parent/child pages become nested folders with subpage links
- **Duplicate page names** — auto-detected and disambiguated with a short ID suffix
- **YAML frontmatter** — page title, OneNote page ID, section name
- **Move, rename & delete detection** — relocated or renamed pages clean up old files; deleted pages are removed
- **Section filtering** — export specific sections via CLI flag or config
- **Page filtering** — export specific page titles or IDs via CLI flag or config
- **Dry run** — `--dry-run` previews what would sync without writing files

### Push (Obsidian → OneNote)

- **Push files or folders** — `--push` sends .md files to a target notebook/section
- **Create or update** — new files create pages, re-pushing updates the same page
- **Full formatting** — headings, bold/italic/strikethrough/underline/links, inline code
- **Lists & tables** — bullet, numbered, nested lists and tables
- **Images** — resolves vault attachments beside the Markdown file or in nested `images`/`_attachments` folders, then embeds them in OneNote
- **Safe replacement** — unresolved images fail before an existing OneNote page is cleared
- **Code blocks** — rendered as shaded Consolas paragraphs without unsafe synthetic tables
- **Blockquotes** — rendered as italic with vertical bar prefix
- **Page title from h1** — uses the first heading as the OneNote page title

## Prerequisites

- **Windows** — required for OneNote COM interop
- **OneNote desktop** (Win32, not UWP/Store) — must be installed and running with notebooks open
- **.NET 8.0 SDK** or later — to build and run

## Quick Start

```bash
# Clone the repo
git clone https://github.com/ciarancoady/OneNoteUtils.git
cd OneNoteUtils

# Build
dotnet build OneNoteUtils.slnx

# Sync a notebook (incremental — default)
dotnet run --project src/OneNoteUtils.Cli -- -n "My Notebook" -o "C:\Export"

# Sync a specific section
dotnet run --project src/OneNoteUtils.Cli -- -n "My Notebook" -o "C:\Export" -s "Daily Notes"

# Sync a specific page
dotnet run --project src/OneNoteUtils.Cli -- -n "My Notebook" -o "C:\Export" -s "Daily Notes" -p "Handoff"

# Force full re-export (cleans output folder first)
dotnet run --project src/OneNoteUtils.Cli -- -n "My Notebook" -o "C:\Export" --full

# Preview what would sync without writing
dotnet run --project src/OneNoteUtils.Cli -- -n "My Notebook" -o "C:\Export" --dry-run

# Push a markdown file to OneNote
dotnet run --project src/OneNoteUtils.Cli -- --push "Note.md" -n "Team Notebook" -s "Shared Notes"

# Push all .md files in a folder
dotnet run --project src/OneNoteUtils.Cli -- --push "C:\Vault\Notes\" -n "Team Notebook" -s "Shared"

# Verbose logging
dotnet run --project src/OneNoteUtils.Cli -- -n "My Notebook" -o "C:\Export" -v
```

### Sync Behaviour

By default, the tool performs an **incremental sync**:
1. First run: no manifest exists → exports the selected scope + creates `.onenote-sync.json`
2. Subsequent runs: compares `lastModifiedTime` → only syncs changed pages
3. Nothing changed? Exits instantly after one hierarchy call (~5 seconds)

New pages are exported. Modified, moved, or renamed pages are re-exported after their old files are cleaned up. Deleted pages are removed. Section and page filters only affect their selected scope; excluded manifest entries and files remain untouched. Use `--full` to force a clean re-export of the selected scope.

## CLI Reference

```
Usage (sync/export):
  OneNoteUtils.Cli -n <notebook> -o <path> [options]

Usage (push to OneNote):
  OneNoteUtils.Cli --push <file-or-folder> -n <notebook> -s <section>

Required (sync/export):
  -n, --notebook <name>    Notebook name or folder path
  -o, --output <path>      Output directory

Required (push):
  --push <path>            .md file or folder to push to OneNote
  -n, --notebook <name>    Target notebook name
  -s, --section <name>     Target section name

Options:
  -s, --section <name>     Filter sections for sync (repeatable)
  -p, --page <name-or-id>  Filter pages for sync (repeatable)
      --full               Force full export (skip incremental sync)
      --dry-run            Preview sync plan without writing files
  -c, --config <path>      Path to a JSON config file (default: appsettings.json)
  -v, --verbose            Enable debug logging
  -h, --help               Show this help message
```

## Configuration

Default settings can be overridden in `appsettings.json` or via `--config`:

```json
{
  "ExportOptions": {
    "SectionFilter": [],
    "PageFilter": [],
    "IncludeFrontmatter": true,
    "UseObsidianWikilinks": true,
    "EmbedImages": true,
    "EmbedPdfs": false,
    "UseAliasLinks": true,
    "AlwaysSuffixWithShortId": false,
    "ShortIdLength": 8
  }
}
```

## Architecture

The solution is split into four projects with enforced layer boundaries (see [ADR-0002](docs/adr/0002-four-project-solution-structure.md)):

```
OneNoteUtils.slnx
├── src/
│   ├── OneNoteUtils.Core/              — Domain models, parsers, interfaces
│   ├── OneNoteUtils.OneNote/           — COM interop via dedicated STA thread (net8.0-windows)
│   ├── OneNoteUtils.Writers.Obsidian/  — Obsidian Markdown writer
│   └── OneNoteUtils.Cli/              — Entry point, config, DI wiring
└── tests/
    ├── OneNoteUtils.Cli.Tests/         — CLI orchestration integration tests (5 tests)
    ├── OneNoteUtils.Core.Tests/        — Parser, sync, and utility tests (104 tests)
    └── OneNoteUtils.Writers.Obsidian.Tests/ — Writer output tests (21 tests)
```

**Pull pipeline:** OneNote COM → raw XML → domain model (Notebook → Section → Page → ContentElement tree) → Markdown files on disk.

**Push pipeline:** Markdown files → MarkdownReader → ContentElement tree → OneNoteXmlWriter → OneNote COM `UpdatePageContent`.

Incremental sync uses a `.onenote-sync.json` manifest to track state between runs (see [ADR-0004](docs/adr/0004-incremental-sync-via-json-manifest.md)). Push tracking also uses the manifest (see [ADR-0005](docs/adr/0005-explicit-push-command-for-markdown-to-onenote.md)).

See [CONTEXT.md](CONTEXT.md) for the domain glossary and [docs/adr/](docs/adr/) for architectural decisions.
Current and completed development work is tracked in [docs/ROADMAP.md](docs/ROADMAP.md).

## Running Tests

```bash
dotnet test OneNoteUtils.slnx
```

## AI Agent Skills

This repo uses [Matt Pocock's Skills](https://github.com/mattpocock/skills) for AI-assisted development. Skills are installed in `.agents/skills/` and configured via `AGENTS.md` and `docs/agents/`.

Available skills include:
- `/grill-with-docs` — stress-test plans against the domain model and sharpen terminology
- `/tdd` — test-driven development with red-green-refactor loop
- `/diagnose` — disciplined debugging loop for hard bugs
- `/to-issues` — break plans into GitHub Issues
- `/triage` — triage issues through a state machine
- `/improve-codebase-architecture` — find deepening opportunities in the codebase

See [AGENTS.md](AGENTS.md) for the full configuration.

## License

See [LICENSE](LICENSE) for details.
