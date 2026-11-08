# Tom's river foam textures (ArtKit/WaterTextures, D294) made tileable: one channel (brightness x alpha), the brightness step across
# each edge levelled with a smooth correction, then a narrow 40 px cross-fade. Usage: python tools/seamless_foam.py <dir for a check image>
import numpy as np, glob, os, sys
from PIL import Image
from scipy.ndimage import gaussian_filter1d
W=sys.argv[1]
def level(d, axis):
    # a smooth ramp per line that takes away the step across the wrap (low frequencies only, structure kept)
    d=np.moveaxis(d,axis,1); n=d.shape[1]
    step=d[:,0]-d[:,-1]
    step=gaussian_filter1d(step,25,mode='wrap')
    x=(np.arange(n)/(n-1)-.5)[None,:]
    d=d+step[:,None]*x*-1   # left edge lowered by step/2, right raised by step/2
    return np.moveaxis(d,1,axis)
def blend(d, b, axis):
    d=np.moveaxis(d,axis,1); n=d.shape[1]
    t=(np.arange(b)/(b-1))[None,:]; t=t*t*(3-2*t)
    out=d[:, :n-b].copy(); out[:, :b]=d[:, :b]*t+d[:, n-b:]*(1-t)
    return np.moveaxis(out,1,axis)
o=Image.new('L',(1200,400))
for k,f in enumerate(sorted(glob.glob('ArtKit/WaterTextures/river_*.png'))):
    a=np.asarray(Image.open(f).convert('RGBA')).astype(float)
    d=(a[:,:,:3].mean(axis=2)/255.0)*(a[:,:,3]/255.0); d=np.clip((d-0.08)/0.8,0,1)
    d=level(d,1); d=level(d,0)
    d=blend(d,40,1); d=blend(d,40,0)
    d=np.clip(d,0,1)
    img=Image.fromarray((d*255).astype('uint8'),'L').resize((1024,1024),Image.LANCZOS)
    img.save('Assets/Water/'+os.path.basename(f).replace('.png','_tiled.png'))
    dd=np.asarray(img).astype(float); n=dd.shape[0]; t=np.tile(dd,(2,2))
    o.paste(Image.fromarray(t[n-200:n+200,n-200:n+200].astype('uint8'),'L'),(k*400,0))
o.save(W+'/foam_seams_fixed.png')
print('ok')
