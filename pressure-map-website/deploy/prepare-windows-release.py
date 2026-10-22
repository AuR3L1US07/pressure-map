"""Update Windows download metadata from the freshly built installer."""
from pathlib import Path
import hashlib
import json

root = Path(__file__).resolve().parents[1]
setup = root.parent / 'pressure-map/output/installer/PressureMap-2.0.0-setup.exe'
digest = hashlib.file_digest(setup.open('rb'), 'sha256').hexdigest()
meta = dict(version='2.0.0', size=f'{setup.stat().st_size / 1048576:.1f} MB',
            sha256=digest, date='2026-10-20')
for path in [root / 'dist/app.js', root / '.tools/app.authored.js']:
    text = path.read_text(encoding='utf-8')
    if path.name == 'app.js':
        assert 'windows:""' in text
        text = text.replace('windows:""', 'windows:"download/PressureMap-2.0.0-setup.exe"', 1)
        marker = '{android:{version:'
        assert marker in text
        text = text.replace(marker, '{windows:' + json.dumps(meta) + ',android:{version:', 1)
    else:
        # Keep the authored source aligned without changing its other platform metadata.
        import re
        text = re.sub(r'windows:\s*[\'"]\s*[\'"]', 'windows: "download/PressureMap-2.0.0-setup.exe"', text, count=1)
    text = text.replace('自包含便携版|Portable Windows app', 'Windows 安装版|Windows installer')
    path.write_text(text, encoding='utf-8', newline='')
html = root / 'dist/index.html'
text = html.read_text(encoding='utf-8').replace('20261020-3', '20261020-windows-v1')
text = text.replace('自包含便携版', 'Windows 安装版')
html.write_text(text, encoding='utf-8', newline='')
(root / 'deploy/windows-release.json').write_text(json.dumps(meta, indent=2) + '\n')
print(json.dumps(meta))
