import sys
from PIL import Image, ImageDraw
# sheet.py out.jpg cols width "label=path" ...
out, cols, w = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
items = [a.split('=',1) for a in sys.argv[4:]]
ims = []
for label, p in items:
    im = Image.open(p).convert('RGB'); h = round(im.height * w / im.width); im = im.resize((w, h), Image.LANCZOS)
    d = ImageDraw.Draw(im); d.rectangle([0,0,len(label)*7+10,16], fill=(0,0,0)); d.text((5,2), label, fill=(255,255,255)); ims.append(im)
rows = (len(ims)+cols-1)//cols; H = ims[0].height
sheet = Image.new('RGB', (cols*w, rows*H))
for i, im in enumerate(ims): sheet.paste(im, ((i%cols)*w, (i//cols)*H))
sheet.save(out, quality=85)
