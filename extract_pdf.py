import PyPDF2
import sys

pdf_path = r"c:\Users\richardbasque\repo\DomainLinksRepo\Policy\Project Management Policy 1.00.pdf"

print(f"Opening PDF: {pdf_path}")
try:
    pdf = open(pdf_path, 'rb')
    reader = PyPDF2.PdfReader(pdf)
    print(f"Total pages: {len(reader.pages)}")
    
    for i, page in enumerate(reader.pages):
        print(f"\n{'='*80}")
        print(f"=== PAGE {i+1} ===")
        print(f"{'='*80}")
        text = page.extract_text()
        if text:
            print(text)
        else:
            print("No text extracted from this page.")
    
    pdf.close()
except Exception as e:
    print(f"Error: {e}")
    sys.exit(1)
