"""Prepare real 64px maps from the three original image-generated atlases.
Nearest resampling only. Binary card alpha comes from the generated transparent image.
"""
from pathlib import Path
from PIL import Image
import json
ROOT=Path(__file__).resolve().parents[1]; PACK=ROOT if (ROOT/'Textures').is_dir() else ROOT/'VegetationPack'; TEX=PACK/'Textures'; SRC=TEX/'Source'

def tile(image,col,row,cols,rows):
    w,h=image.size
    return image.crop((round(col*w/cols),round(row*h/rows),round((col+1)*w/cols),round((row+1)*h/rows))).resize((64,64),Image.Resampling.NEAREST)

def palette(im,n=20):
    return im.convert('RGB').quantize(colors=n,method=Image.Quantize.MEDIANCUT,dither=Image.Dither.NONE).convert('RGB')

bark=Image.open(SRC/'Bark_Atlas.png');foliage=Image.open(SRC/'Foliage_Atlas.png');cutouts=Image.open(SRC/'Foliage_Cutouts.png').convert('RGBA')
assert cutouts.getextrema()[3][0]==0,'Generated cutout atlas has no transparency'
report=[]
for i,name in enumerate(['Pine','Birch','Oak','Willow']):
    im=palette(tile(bark,i%2,i//2,2,2),18);dest=TEX/f'T_Tree_{name}_Bark_64.png';im.save(dest)
    report.append({'file':dest.name,'size':list(im.size),'colors':len(im.getcolors(65536)),'type':'bark'})
for i,name in enumerate(['Pine','Spruce','Birch_Green','Birch_Autumn','Oak','Maple_Autumn','Sparse','Willow']):
    source=tile(Image.open(SRC/'Spruce_Foliage.png'),0,0,1,1) if name=='Spruce' else tile(foliage,i%4,i//4,4,2)
    im=palette(source,20);dest=TEX/f'T_Tree_{name}_Foliage_64.png';im.save(dest)
    mask_index=1 if name in ['Pine','Spruce'] else 2 if name=='Willow' else 3 if name=='Sparse' else 0
    original=tile(cutouts,mask_index%2,mask_index//2,2,2)
    alpha=original.getchannel('A').point(lambda a:255 if a>=128 else 0)
    # Reuse generated branch silhouette, with the matching species' generated foliage albedo.
    card=im.convert('RGBA');card.putalpha(alpha);card=card.resize((32,32),Image.Resampling.NEAREST);cdest=TEX/f'T_Tree_{name}_Card_32.png';card.save(cdest)
    old=TEX/f'T_Tree_{name}_Card_64.png'
    if old.exists():old.unlink()
    report.extend([{'file':dest.name,'size':[64,64],'colors':len(im.getcolors(65536)),'type':'foliage'}, {'file':cdest.name,'size':[32,32],'alpha_values':sorted(set(alpha.getdata())),'type':'cutout'}])
(PACK/'Reports'/'texture_validation.json').write_text(json.dumps(report,indent=2))
# Readable inspection sheet: enlargement does not alter the original deliverable maps.
canvas=Image.new('RGB',(1024,768),(44,48,43))
for i,rec in enumerate(report):
    im=Image.open(TEX/rec['file']).convert('RGBA').resize((128,128),Image.Resampling.NEAREST)
    canvas.paste(im,((i%8)*128,(i//8)*256),im)
canvas.save(PACK/'Previews'/'Texture_Pixels.png')
print('TEXTURES_READY',len(report),'maps: 64px bark/foliage, 32px twig cards, limited palettes, binary alpha')
