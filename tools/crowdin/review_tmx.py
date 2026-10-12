#!/usr/bin/env python3
"""Review en→uk TMX without changing the input; filtered output is a proposal."""
import argparse
import csv
import re
import unicodedata
import xml.etree.ElementTree as ET
from pathlib import Path

LANG = '{http://www.w3.org/XML/1998/namespace}lang'


def visible(text):
    text = re.sub(r'\{[^{}]*\}|<[^>]*>', ' ', text)
    # Atlus square-bracket commands can contain nested speaker brackets.
    out = []
    index = 0
    while index < len(text):
        if text[index] == '[' and re.match(r'\[[a-z_]+(?:\s|\])', text[index:]):
            end = index + 1
            depth = 1
            while end < len(text) and depth:
                depth += (text[end] == '[') - (text[end] == ']')
                end += 1
            if not depth:
                out.append(' ')
                index = end
                continue
        out.append(text[index])
        index += 1
    text = ' '.join(''.join(out).split())
    if re.fullmatch(r'[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+', text):
        return ''
    return text


def latin_only(text):
    letters = [c for c in visible(text) if c.isalpha()]
    return bool(letters) and all('LATIN' in unicodedata.name(c, '') for c in letters)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('tmx', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    source = args.tmx.resolve()
    if not source.is_file():
        parser.error('TMX file does not exist')
    output = args.output.resolve()
    report = output / 'review.csv'
    proposal = output / 'proposed-cleaned.tmx'
    if source in (report, proposal) or report.exists() or proposal.exists():
        parser.error('Use a new output directory; existing files are never overwritten')
    tree = ET.parse(source, parser=ET.XMLParser(target=ET.TreeBuilder(insert_comments=True)))
    root = tree.getroot()
    if root.tag != 'tmx' or root.find('body') is None:
        parser.error('Expected a TMX document with a body')
    body = root.find('body')
    rows = []
    checked = removed = 0
    excluded = set()
    for index, unit in enumerate(list(body), 1):
        if unit.tag != 'tu':
            continue
        variants = unit.findall('tuv')
        english = [v for v in variants if (v.get(LANG) or v.get('lang', '')).lower().split('-')[0] == 'en']
        ukrainian = [v for v in variants if (v.get(LANG) or v.get('lang', '')).lower().split('-')[0] == 'uk']
        if not english or not ukrainian:
            continue
        checked += 1
        texts = [''.join(v.find('seg').itertext()) for v in english if v.find('seg') is not None]
        suspect = []
        for variant in ukrainian:
            seg = variant.find('seg')
            if seg is None:
                continue
            target = ''.join(seg.itertext())
            reasons = []
            if any(target.strip() == s.strip() and latin_only(s) for s in texts):
                reasons.append('same_as_source')
            if latin_only(target):
                reasons.append('latin_only')
            if reasons:
                suspect.append((variant, target, reasons))
        for variant, target, reasons in suspect:
            rows.append([index, unit.get('tuid', ''), texts[0] if texts else '', target,
                         ','.join(reasons), 'uk_segment_excluded_from_proposal'])
            unit.remove(variant)
        if suspect:
            remaining = unit.findall('tuv')
            languages = {(v.get(LANG) or v.get('lang', '')).lower().split('-')[0] for v in remaining}
            if languages <= {'en'}:
                excluded.add(unit)
                removed += 1
    body[:] = [unit for unit in body if unit not in excluded]
    if not checked:
        parser.error('No en→uk translation units found; check TMX language codes')
    output.mkdir(parents=True, exist_ok=True)
    with report.open('w', encoding='utf-8-sig', newline='') as stream:
        writer = csv.writer(stream)
        writer.writerow(['unit_index', 'tuid', 'source_en', 'target_uk', 'reason', 'action'])
        writer.writerows(rows)
    serialized = ET.tostring(root, encoding='utf-8', xml_declaration=True)
    # Preserve character-reference carriage returns across XML reparsing.
    proposal.write_bytes(serialized.replace(b'\r', b'&#13;'))
    ET.parse(proposal)
    print(f'Checked {checked} en→uk units; flagged {len(rows)} segments; excluded {removed} units from proposal.')
    print(f'Review: {report}\nProposal (review before import): {proposal}')


if __name__ == '__main__':
    main()
