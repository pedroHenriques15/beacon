#!/usr/bin/env python3
"""
Usage: python readPdf.py <path-to-pdf>

Extracts and prints the text content of each page in the given PDF.
Delegates extraction to pdfExtractor.py via pdfplumber.
"""
import json
import subprocess
import sys
from pathlib import Path

if len(sys.argv) < 2:
    print("Usage: python readPdf.py <path-to-pdf>", file=sys.stderr)
    sys.exit(1)

pdf_path = sys.argv[1]
extractor = Path(__file__).parent / "pdfExtractor.py"

result = subprocess.run(
    [sys.executable, str(extractor), pdf_path],
    capture_output=True,
    text=True,
)

if result.returncode != 0:
    print(result.stderr, file=sys.stderr)
    sys.exit(result.returncode)

pages = json.loads(result.stdout)

for i, page_text in enumerate(pages, start=1):
    print(f"--- Page {i} ---")
    print(page_text)
    print()
