"""Export literal UI translation keys; run with Python 3 from any directory."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1]
source = (root / 'src/Moonrise/MainWindow.xaml.cs').read_text(encoding='utf-8-sig')
string = r'"((?:[^"\\]|\\.)*)"'
translations = {}
for match in re.finditer(r'\bT\(\s*' + string + r'\s*,\s*' + string + r'\s*\)', source):
    russian, english = (json.loads('"' + value + '"') for value in match.groups())
    translations[english] = russian
for code, name in [('en', 'English'), ('ru', 'Русский')]:
    content = dict(schemaVersion=1, code=code, name=name,
                   translations={key: key if code == 'en' else value for key, value in sorted(translations.items())})
    (root / f'assets/languages/{code}.template.json').write_text(
        json.dumps(content, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(f'Exported {len(translations)} keys per template.')
