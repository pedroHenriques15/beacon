"""PDF -> JSON text extractor, invoked by the .NET API (PdfExtractorService).

Contract with the C# side:
  stdout   : a JSON array of page texts (text-less pages are dropped).
  exit 0   : success (an empty array means the PDF has no text layer, e.g. a scan).
  exit 2   : the PDF is password-protected.
  exit 3   : the PDF is corrupt / not a valid PDF.
  exit 1   : any other failure. stderr carries a ONE-LINE message, never a traceback.

Encoding invariant: json.dumps defaults to ensure_ascii=True, so stdout is pure
ASCII (accented characters escaped as \\uXXXX) and survives any Windows codepage.
Do NOT switch to ensure_ascii=False without also pinning UTF-8 on both the Python
and C# sides of the pipe.
"""

import json
import sys


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: pdfExtractor.py <pdf-path>", file=sys.stderr)
        return 1

    try:
        import pdfplumber
        from pdfminer.pdfdocument import PDFPasswordIncorrect
        from pdfplumber.utils.exceptions import PdfminerException
    except ImportError as exc:
        print(f"missing Python dependency: {exc.name} (pip install -r scripts/requirements.txt)", file=sys.stderr)
        return 1

    pages = []
    try:
        with pdfplumber.open(sys.argv[1]) as pdf:
            for page in pdf.pages:
                text = page.extract_text()
                if text:
                    pages.append(text)
    except PdfminerException as exc:
        cause = exc.args[0] if exc.args else None
        if isinstance(cause, PDFPasswordIncorrect):
            print("PDF is password-protected", file=sys.stderr)
            return 2
        print(f"PDF could not be parsed: {type(cause).__name__ if cause else 'unknown'}", file=sys.stderr)
        return 3
    except PDFPasswordIncorrect:
        print("PDF is password-protected", file=sys.stderr)
        return 2
    except FileNotFoundError:
        print("PDF file not found", file=sys.stderr)
        return 1
    except Exception as exc:
        print(f"PDF extraction failed: {type(exc).__name__}", file=sys.stderr)
        return 3

    print(json.dumps(pages))
    return 0


if __name__ == "__main__":
    sys.exit(main())
