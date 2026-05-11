import pdfplumber, json, sys

pages = []
with pdfplumber.open(sys.argv[1]) as pdf:
    for page in pdf.pages:
        text = page.extract_text()
        if text:
            pages.append(text)

print(json.dumps(pages))
