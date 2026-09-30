"""Lay out the real Blender renders, with asset IDs and measured budgets."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,math
ROOT=Path(__file__).resolve().parents[1];PACK=ROOT if (ROOT/'Textures').is_dir() else ROOT/'VegetationPack';PRE=PACK/'Previews'
records=[r for r in json.loads((PACK/'manifest.json').read_text()) if r['lod']==0]
fontpath='C:/Windows/Fonts/segoeui.ttf'
font=lambda size:ImageFont.truetype(fontpath,size)

def sheet(rows,columns,path,title):
    cw,ch=420,690;margin=34;header=130;w=columns*cw+margin*2;h=math.ceil(len(rows)/columns)*ch+header+margin
    im=Image.new('RGB',(w,h),(45,54,45));d=ImageDraw.Draw(im)
    d.text((margin,26),title,font=font(34),fill=(230,229,210))
    d.text((margin,76),'Actual 3D assets  /  low-poly geometry  /  crisp pixel textures  /  LOD 0',font=font(20),fill=(166,181,155))
    for i,r in enumerate(rows):
        x=margin+(i%columns)*cw;y=header+(i//columns)*ch
        tile=Image.open(PRE/(r['id']+'.png')).convert('RGBA');im.paste(tile,(x,y),tile)
        label=r['id'].removeprefix('TREE_').replace('_',' ')
        box=d.textbbox((0,0),label,font=font(22));d.text((x+(cw-box[2])/2,y+613),label,font=font(22),fill=(235,232,208))
        detail=f"{r['height_m']:.1f} m  |  {r['triangles']:,} triangles"
        box=d.textbbox((0,0),detail,font=font(16));d.text((x+(cw-box[2])/2,y+646),detail,font=font(16),fill=(169,181,158))
    im.save(PRE/path)
hero=['TREE_Pine_A','TREE_Spruce_A','TREE_Birch_Green_A','TREE_Birch_Autumn_A','TREE_Oak_A','TREE_Maple_Autumn_A','TREE_Sparse_A','TREE_Poplar_A','TREE_Willow_A']
sheet([next(r for r in records if r['id']==name) for name in hero],3,'Species_Contact_Sheet.png','THORNWAKE  /  WOODLAND SPECIES')
sheet(records,6,'All_17_Trees.png','THORNWAKE  /  COMPLETE WOODLAND COLLECTION')
# Close-range inspection images placed on a neutral backdrop.
for name in ['Birch_Close','Pine_Close','Oak_Close','Willow_Close','Oak_Gameplay']:
    rgba=Image.open(PRE/(name+'.png')).convert('RGBA');bg=Image.new('RGB',rgba.size,(57,65,55));bg.paste(rgba,(0,0),rgba);bg.save(PRE/(name+'_Neutral.png'))
print('CONTACT_SHEETS_READY: 9 species, all 17 variants, 5 neutral close/gameplay plates')
# LOD plate: identical camera and scale within each row.
rows=['Pine_A','Birch_Green_A','Oak_A','Willow_A'];w=1320;h=4*570+120
im=Image.new('RGB',(w,h),(45,54,45));d=ImageDraw.Draw(im);d.text((30,22),'THORNWAKE / LOD SILHOUETTE REVIEW',font=font(30),fill=(230,229,210))
for lod in range(3):d.text((70+lod*420,76),'LOD '+str(lod),font=font(22),fill=(180,191,162))
all_records=json.loads((PACK/'manifest.json').read_text())
for row,name in enumerate(rows):
    for lod in range(3):
        path=PRE/('LOD_'+name+'_'+str(lod)+'.png')
        if not path.exists():continue
        tile=Image.open(path).convert('RGBA');x=30+lod*420;y=120+row*570;im.paste(tile,(x,y),tile)
        triangles=next(r['triangles'] for r in all_records if r['id']=='TREE_'+name and r['lod']==lod)
        d.text((x+40,y+525),f'{name.replace("_"," ")}  |  {triangles:,} tris',font=font(18),fill=(225,225,200))
im.save(PRE/'LOD_Comparison.png')
