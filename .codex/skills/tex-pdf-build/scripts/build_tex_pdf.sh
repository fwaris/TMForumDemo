#!/usr/bin/env bash
set -euo pipefail

usage() {
  printf 'Usage: %s <path/to/file.tex>\n' "$(basename "$0")" >&2
}

if [ "$#" -ne 1 ]; then
  usage
  exit 64
fi

tex_path="$1"

if [ ! -f "$tex_path" ]; then
  printf 'TeX source not found: %s\n' "$tex_path" >&2
  exit 66
fi

case "$tex_path" in
  *.tex) ;;
  *)
    printf 'Input must be a .tex file: %s\n' "$tex_path" >&2
    exit 64
    ;;
esac

tex_abs="$(cd "$(dirname "$tex_path")" && pwd)/$(basename "$tex_path")"
tex_dir="$(dirname "$tex_abs")"
tex_file="$(basename "$tex_abs")"
pdf_file="${tex_file%.tex}.pdf"
pdf_abs="$tex_dir/$pdf_file"

(
  cd "$tex_dir"
  if command -v latexmk >/dev/null 2>&1; then
    latexmk -pdf -interaction=nonstopmode -halt-on-error "$tex_file"
  elif command -v tectonic >/dev/null 2>&1; then
    tectonic "$tex_file"
  else
    printf 'latexmk or tectonic is required but neither was found on PATH.\n' >&2
    exit 69
  fi
)

if [ ! -s "$pdf_abs" ]; then
  printf 'Expected PDF was not created or is empty: %s\n' "$pdf_abs" >&2
  exit 70
fi

printf '%s\n' "$pdf_abs"
