# Archived Source Map Work

This folder preserves the removed source-map implementation for future reuse.

The active monitor now uses direct Roslyn validation with Working overlay support before compare, so the old source-map runtime was removed from the build. The archived code is still useful as a seed for a future documentation/context generator.

Possible future use:

- generate feature/component maps from source structure
- build data-flow draft reports for selected folders or namespaces
- create source-anchor inventories for documentation passes
- export project structure for a future MCP/context layer

Do not copy these files back into the active monitor unless the source-map feature is deliberately redesigned around documentation/context generation rather than compare gating.
