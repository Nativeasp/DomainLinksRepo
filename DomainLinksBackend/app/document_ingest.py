from __future__ import annotations

import re
from collections import Counter
from io import BytesIO

from pypdf import PdfReader


def extract_pdf_text(pdf_bytes: bytes) -> tuple[str, dict[str, int]]:
    reader = PdfReader(BytesIO(pdf_bytes))
    page_line_groups: list[list[str]] = []

    for page in reader.pages:
        raw_text = page.extract_text() or ""
        lines = _normalize_lines(raw_text)
        page_line_groups.append(lines)

    repeated_noise = {
        line
        for line, count in Counter(
            line
            for lines in page_line_groups
            for line in set(lines)
            if len(line) < 120
        ).items()
        if count >= 3
    }

    kept_lines: list[str] = []
    dropped_lines = 0
    for lines in page_line_groups:
        filtered = []
        for line in lines:
            if line in repeated_noise or _is_low_value_line(line):
                dropped_lines += 1
                continue
            filtered.append(line)
        kept_lines.extend(_merge_short_lines(filtered))

    extracted_text = "\n\n".join(line for line in kept_lines if line.strip())
    stats = {
        "pageCount": len(reader.pages),
        "keptLineCount": len(kept_lines),
        "droppedLineCount": dropped_lines,
    }
    return extracted_text, stats


def _normalize_lines(raw_text: str) -> list[str]:
    raw_text = raw_text.replace("\x00", " ")
    lines = []
    for line in raw_text.splitlines():
        normalized = re.sub(r"\s+", " ", line).strip()
        if normalized:
            lines.append(normalized)
    return lines


def _is_low_value_line(line: str) -> bool:
    if len(line) < 4:
        return True
    alpha_chars = sum(1 for char in line if char.isalpha())
    digit_chars = sum(1 for char in line if char.isdigit())
    useful_chars = alpha_chars + digit_chars
    if useful_chars == 0:
        return True
    alpha_ratio = alpha_chars / max(len(line), 1)
    if len(line) < 40 and alpha_ratio < 0.45:
        return True
    if len(line) < 20 and " " not in line and digit_chars > alpha_chars:
        return True
    return False


def _merge_short_lines(lines: list[str]) -> list[str]:
    merged: list[str] = []
    buffer = ""
    for line in lines:
        if len(line) < 80:
            buffer = f"{buffer} {line}".strip()
            continue
        if buffer:
            merged.append(buffer)
            buffer = ""
        merged.append(line)
    if buffer:
        merged.append(buffer)
    return merged
