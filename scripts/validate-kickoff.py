from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]
required = [
    '.gitattributes','AGENTS.md','README.md','docs/r0-scope.md','docs/acceptance-criteria.md',
    'docs/failure-test-matrix.md','tasks/R0-001-repository-bootstrap.md',
    'schemas/heartbeat.schema.json','schemas/material-event.schema.json',
    'schemas/event-batch.schema.json','schemas/incident-summary.schema.json'
]
missing = [p for p in required if not (ROOT / p).is_file()]
if missing:
    raise SystemExit(f'Missing required files: {missing}')

for path in (ROOT / 'schemas').glob('*.json'):
    with path.open(encoding='utf-8') as fh:
        json.load(fh)

agent_bytes = (ROOT / 'AGENTS.md').stat().st_size
if agent_bytes > 32 * 1024:
    raise SystemExit(f'AGENTS.md exceeds 32 KiB: {agent_bytes}')

expected_gitattributes = [
    '* text=auto eol=lf',
    '',
    '*.bat text eol=crlf',
    '*.cmd text eol=crlf',
]
gitattributes_lines = (ROOT / '.gitattributes').read_text(encoding='utf-8').splitlines()
if gitattributes_lines != expected_gitattributes:
    raise SystemExit('.gitattributes must enforce LF text checkout with CRLF exceptions only for .bat/.cmd')

excluded_directories = {'.git', 'bin', 'obj'}
lf_required_suffixes = {
    '.cs', '.csproj', '.json', '.md', '.props', '.ps1', '.py', '.sh', '.sln',
    '.targets', '.xml', '.yaml', '.yml',
}
lf_required_names = {'.gitattributes'}

for path in ROOT.rglob('*'):
    if excluded_directories.intersection(path.relative_to(ROOT).parts):
        continue

    if path.is_file() and (path.suffix.lower() in lf_required_suffixes or path.name in lf_required_names):
        raw = path.read_bytes()
        if b'\r\n' in raw:
            raise SystemExit(f'CRLF found in {path.relative_to(ROOT)}; repository standard is LF')

print('Kickoff package validation passed.')
