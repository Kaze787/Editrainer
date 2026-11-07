from pathlib import Path
from PIL import Image, ImageDraw
import json

root = Path(__file__).resolve().parent.parent
assets = root / 'Assets'
icon = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
d = ImageDraw.Draw(icon)
d.ellipse((10, 10, 246, 246), fill='#ffffff', outline='#1b2838', width=12)
d.pieslice((16, 16, 240, 240), 180, 360, fill='#0a7e6e')
d.line((17, 128, 239, 128), fill='#1b2838', width=14)
d.ellipse((86, 86, 170, 170), fill='#ffffff', outline='#1b2838', width=12)
d.ellipse((111, 111, 145, 145), fill='#0a7e6e')
icon.save(assets / 'app.ico', sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
icon.save(assets / 'app.png')
files = list((assets / 'sprites').rglob('*.png')) + list((assets / 'artwork').rglob('*.png'))
for path in files:
    with Image.open(path) as picture:
        picture.verify()
ids = json.loads((root / 'build-tools' / 'species.json').read_text(encoding='utf-8-sig'))
for species in ids:
    for kind in ('sprites', 'artwork'):
        for variant in ('', 'shiny'):
            assert (assets / kind / variant / f'{species}.png').is_file(), (species, kind, variant)
report = {'valid_pngs': len(files), 'species': len(ids), 'size_mb': round(sum(p.stat().st_size for p in files) / 1024 ** 2, 1)}
(root / 'tests' / 'asset-results.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report))
