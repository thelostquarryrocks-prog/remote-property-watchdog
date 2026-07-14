from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
SKIP_PARTS = {'.git', 'bin', 'obj', 'TestResults', 'artifacts'}
TEXT_SUFFIXES = {'.cs', '.csproj', '.props', '.targets', '.json', '.yml', '.yaml', '.md', '.ps1', '.py', '.sln'}
PATTERNS = [
    re.compile(r'(?i)(api[_-]?key|client[_-]?secret|access[_-]?token|bearer\s+)[\s:=]+[A-Za-z0-9_./+=-]{16,}'),
    re.compile(r'(?i)password\s*=\s*(?!local-development-only)([^;\s]{8,})'),
    re.compile(r'(?i)authorization:\s*(bearer|basic)\s+\S+'),
]

findings = []
for path in ROOT.rglob('*'):
    if not path.is_file() or any(part in SKIP_PARTS for part in path.parts):
        continue
    if path.suffix.lower() not in TEXT_SUFFIXES:
        continue
    text = path.read_text(encoding='utf-8', errors='ignore')
    for pattern in PATTERNS:
        if pattern.search(text):
            findings.append(str(path.relative_to(ROOT)))

if findings:
    raise SystemExit('Potential secret material found in: ' + ', '.join(sorted(set(findings))))
print('Secret scan passed.')
