# Four-project solution structure with layer isolation

The solution is split into four production projects: `OneNoteUtils.Core` (domain models + parser), `OneNoteUtils.OneNote` (COM interop behind `IOneNoteSource`), `OneNoteUtils.Writers.Obsidian` (Obsidian Markdown writer), and `OneNoteUtils.Cli` (entry point, application orchestration, and DI wiring). Three test projects cover the Core and writer libraries plus CLI orchestration.

We considered a simpler 2-project split (one library + one console app) but chose 4 production projects to enforce layer boundaries at the dependency level. `Core` has no dependency on COM or any output format. `Writers.Obsidian` depends only on `Core`. `OneNote` depends only on `Core`. `Cli` wires them together through a testable application service. This makes it impossible for the parser to accidentally call COM, or for the writer to depend on OneNote internals — the compiler enforces what code review would otherwise have to catch.
