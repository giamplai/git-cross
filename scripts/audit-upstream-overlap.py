#!/usr/bin/env python3
"""Provenance check: does any of OUR source contain lines that also appear in the upstream GitExtensions sources?

usage: scripts/audit-upstream-overlap.py [path-to-upstream-checkout]
Exit code 1 when an identical, non-trivial line (>= MIN chars) is found, so it can gate releases.
Trivial lines (braces, usings, comments, one-token lines) are ignored. A hit is a prompt to review the code and,
if it is genuinely upstream's, to rewrite it or attribute it (see docs/PROVENANCE.md) — not proof of copying by itself.
"""
import os, re, sys

MIN = 45
UP = sys.argv[1] if len(sys.argv) > 1 else next(
    (p for p in (os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'external', 'gitextensions'),) if os.path.isdir(p)), None)
if not UP:
    sys.exit("upstream checkout not found: pass its path (scripts/fetch-upstream.sh clones it to external/gitextensions)")
HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SKIP = ('/obj', '/bin', '/artifacts', '/.git', '/external')
TRIVIAL = re.compile(r'^([{}()\[\];,]|else|try|\*/|/\*\*|//.*|using .*|namespace .*|\W+|\w+:?)$')

def files(root, exts):
    for dp, _, fn in os.walk(root):
        if any(s in dp for s in SKIP):
            continue
        for f in fn:
            if f.endswith(exts):
                yield os.path.join(dp, f)

def long_lines(path):
    with open(path, encoding='utf-8-sig', errors='ignore') as fh:
        for n, l in enumerate(fh, 1):
            l = l.strip()
            if len(l) >= MIN and not TRIVIAL.match(l):
                yield n, l

upstream = {}
count = 0
for p in files(UP, ('.cs', '.axaml', '.xaml', '.resx')):
    count += 1
    for _, l in long_lines(p):
        upstream.setdefault(l, p)

ours = [p for d in ('src', 'tests') for p in files(os.path.join(HERE, d), ('.cs', '.axaml'))]
hits = [(p, n, l, upstream[l]) for p in ours for n, l in long_lines(p) if l in upstream]

print(f"upstream: {count} files, {len(upstream)} distinct long lines | ours: {len(ours)} files | threshold: {MIN} chars")
for p, n, l, u in hits:
    print(f"  MATCH {os.path.relpath(p, HERE)}:{n}: {l[:100]}\n        also in {os.path.relpath(u, UP)}")
print(f"identical non-trivial lines: {len(hits)}")
sys.exit(1 if hits else 0)
