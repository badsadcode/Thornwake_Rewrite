# fdiff.py dir out.jpg: amplified differences between consecutive burst frames (shot, shot-1, ...), plus mean change per band of rows
import sys, os
from PIL import Image, ImageChops, ImageOps
d, out = sys.argv[1], sys.argv[2]
names = ['shot.png'] + sorted([f for f in os.listdir(d) if f.startswith('shot-')], key=lambda f: int(f[5:-4]))
ims = [Image.open(os.path.join(d, n)).convert('RGB') for n in names]
W, H = ims[0].size
sheet = Image.new('RGB', (W, H * 2))
sheet.paste(ims[0], (0, 0))
acc = None
for a, b in zip(ims, ims[1:]):
    df = ImageChops.difference(a, b).convert('L')
    acc = df if acc is None else ImageChops.lighter(acc, df)
    rows = []
    for y0 in range(0, H, H // 8):
        c = df.crop((0, y0, W, min(H, y0 + H // 8))); h = c.histogram(); n = sum(h)
        rows.append(round(sum(i * v for i, v in enumerate(h)) / n, 2))
    print(rows)
sheet.paste(ImageOps.autocontrast(acc.point(lambda v: min(255, v * 8))).convert('RGB'), (0, H))
sheet.resize((W // 2, H)).save(out, quality=85)
