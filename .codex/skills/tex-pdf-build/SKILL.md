---
name: tex-pdf-build
description: Regenerate PDFs from LaTeX `.tex` sources in this repository, especially `docs/icnp2026_submission/icnp2026_submission.tex`. Use when the user asks to compile, rebuild, regenerate, or verify a PDF from a TeX file after edits to figures, listings, references, or paper text.
---

# TeX PDF Build

## Workflow

Use the bundled script for repeatable PDF generation:

```bash
.codex/skills/tex-pdf-build/scripts/build_tex_pdf.sh docs/icnp2026_submission/icnp2026_submission.tex
```

If the user names a different `.tex` file, pass that path instead. The script:

- runs from the TeX file directory, preferring `latexmk -pdf -interaction=nonstopmode -halt-on-error` and falling back to `tectonic` when `latexmk` is unavailable;
- writes the PDF beside the source file;
- verifies that the PDF exists and is non-empty;
- prints the absolute PDF path.

## After Building

Report the generated PDF path and whether the build succeeded. If the build fails, summarize the first actionable LaTeX error and point to the `.log` file in the same directory as the `.tex` source.

Do not clean auxiliary files by default. This project keeps LaTeX build artifacts around during paper iteration unless the user asks to clean them.

## Manual Fallback

If the script is unavailable and `latexmk` exists, run:

```bash
cd docs/icnp2026_submission
latexmk -pdf -interaction=nonstopmode -halt-on-error icnp2026_submission.tex
```

If only `tectonic` exists, run:

```bash
cd docs/icnp2026_submission
tectonic icnp2026_submission.tex
```

For other TeX files, run the same command from that file's directory with its basename.
