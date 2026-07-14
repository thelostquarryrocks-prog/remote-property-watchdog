from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]
required = [
    'AGENTS.md','README.md','docs/r0-scope.md','docs/acceptance-criteria.md',
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

for path in ROOT.rglob('*'):
    if path.is_file() and path.suffix.lower() in {'.md','.json','.yml','.yaml','.ps1','.py','.props'}:
        raw = path.read_bytes()
        if b'\r\n' in raw:
            raise SystemExit(f'CRLF found in {path.relative_to(ROOT)}; repository standard is LF')

print('Kickoff package validation passed.')
