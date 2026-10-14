"""Build the CC0 game clips from the source collection in workspace/output/audio."""
from pathlib import Path
import json, hashlib, shutil, math
import numpy as np
import soundfile as sf
from scipy.signal import butter, sosfilt, resample_poly, find_peaks

WORKSPACE = Path(__file__).resolve().parents[3]
SOURCE = WORKSPACE/'output/audio'
DEST = WORKSPACE/'MobileDeath/UnityPart/PancakeCooker/Assets/Audio/Blinopek'
RATE = 44100
for folder in ['Sfx','Loops','Music','Licenses']:
    (DEST/folder).mkdir(parents=True,exist_ok=True)
catalog = {s['id']:s for s in json.loads((SOURCE/'catalog.json').read_text(encoding='utf-8'))['sources']}
cache = {}
records = []
used = set()

def read(id, file=None):
    key=(id,file)
    if key not in cache:
        s=catalog[id]
        if id.startswith('F'): p=SOURCE/'public-previews'/(id+'.mp3')
        elif file: p=WORKSPACE/s['local_path']/'Audio'/file
        else: p=WORKSPACE/s['local_path']
        x,r=sf.read(p,always_2d=True)
        if r!=RATE:
            g=math.gcd(r,RATE); x=resample_poly(x,RATE//g,r//g)
        cache[key]=x
    used.add(id)
    return cache[key].copy()

def mono(x): return x.mean(axis=1) if x.ndim>1 else x
def filter_audio(x, low=80, high=7000):
    return sosfilt(butter(2,[low,high],btype='bandpass',fs=RATE,output='sos'),x,axis=0)
def fade(x, start=.008, end=.03):
    x=x.copy(); a=min(int(start*RATE),len(x)//2); b=min(int(end*RATE),len(x)//2)
    shape=(-1,1) if x.ndim>1 else (-1,)
    if a: x[:a]*=np.linspace(0,1,a).reshape(shape)
    if b: x[-b:]*=np.linspace(1,0,b).reshape(shape)
    return x
def level(x, rms=.10, peak=.60):
    x=np.nan_to_num(x); x-=x.mean(axis=0)
    factor=min(rms/max(np.sqrt(np.mean(x*x)),1e-7),peak/max(np.max(np.abs(x)),1e-7))
    return x*factor
def trim(x):
    amplitude=np.max(np.abs(x),axis=1) if x.ndim>1 else np.abs(x)
    active=np.flatnonzero(amplitude>max(amplitude.max()*.025,1e-6))
    if not len(active): raise ValueError('Silent source')
    return x[max(0,active[0]-int(.01*RATE)):min(len(x),active[-1]+int(.04*RATE))]
def raw(id,file): return trim(filter_audio(mono(read(id,file))))
def transient(id,seconds,variant=0):
    x=filter_audio(mono(read(id)))
    # Select separated local attacks; retain pre-attack audio instead of cutting into the transient.
    hop=220; env=np.array([np.sqrt(np.mean(x[i:i+hop]**2)) for i in range(0,len(x),hop)])
    peaks,_=find_peaks(env,distance=max(1,int(seconds*RATE/hop)),height=env.max()*.22)
    if not len(peaks): peaks=np.array([env.argmax()])
    best=peaks[np.argsort(env[peaks])[-min(8,len(peaks)):]]
    best=np.sort(best); center=int(best[variant%len(best)]*hop)
    start=max(0,min(center-int(.075*RATE),len(x)-int(seconds*RATE)))
    return x[start:start+int(seconds*RATE)]
def mix(*layers):
    n=max(int(delay*RATE)+len(x) for x,delay,gain in layers)
    out=np.zeros(n)
    for x,delay,gain in layers:
        i=int(delay*RATE); out[i:i+len(x)]+=mono(x)*gain
    return out
def steady(id,seconds,start=None,high=6500):
    x=filter_audio(mono(read(id)),90,high)
    n=int(seconds*RATE)
    if start is not None: return x[int(start*RATE):int(start*RATE)+n]
    overall=np.sqrt(np.mean(x*x)); candidates=[]
    for i in range(0,len(x)-n+1,max(RATE,n//4)):
        piece=x[i:i+n]; blocks=np.array([np.sqrt(np.mean(b*b)) for b in np.array_split(piece,32)])
        if blocks.mean()<overall*.45: continue
        score=blocks.std()/max(blocks.mean(),1e-8)+.025*np.max(np.abs(piece))/max(blocks.mean(),1e-8)
        candidates.append((score,i))
    i=min(candidates)[1] if candidates else 0
    return x[i:i+n]
def seam(x,seconds=.15):
    n=min(int(seconds*RATE),len(x)//4)
    w=np.linspace(0,1,n); w=w[:,None] if x.ndim>1 else w
    # End -> beginning crossfade becomes the beginning of the circular clip.
    return np.concatenate([x[-n:]*(1-w)+x[:n]*w,x[n:-n]],axis=0)
def save(folder,name,x,ids,loop=False,music=False):
    x=level(x, .065 if music else .075 if loop else .105, .70 if music else .60)
    x=seam(x,1.5 if music else .12) if loop else fade(x)
    ext='.ogg' if loop else '.wav'
    p=DEST/folder/(name+ext)
    # libsndfile's Windows Vorbis encoder can overflow its stack on one long write.
    with sf.SoundFile(p, 'w', samplerate=RATE, channels=1 if x.ndim == 1 else x.shape[1],
                      subtype='VORBIS' if loop else 'PCM_16') as stream:
        for start in range(0, len(x), 16384): stream.write(x[start:start+16384])
    decoded,r=sf.read(p,always_2d=True)
    assert len(decoded)>0 and np.isfinite(decoded).all() and np.abs(decoded).max()<.99
    records.append(dict(file=p.relative_to(DEST).as_posix(),sources=ids,seconds=round(len(decoded)/r,3),
        channels=decoded.shape[1],sample_rate=r,sha256=hashlib.sha256(p.read_bytes()).hexdigest(),
        peak=round(float(abs(decoded).max()),5),processing='trim, filter, level, fades' if not loop else 'filter/level and circular crossfade'))

simple={
 'Button':('K01',['click_001.ogg','click_002.ogg']),
 'Select':('K01',['select_001.ogg','select_002.ogg']),
 'Back':('K01',['back_001.ogg']), 'Toggle':('K01',['toggle_001.ogg','toggle_002.ogg']),
 'Error':('K01',['error_001.ogg']), 'BookOpen':('K02',['bookOpen.ogg']),
 'BookClose':('K02',['bookClose.ogg']), 'BookPage':('K02',['bookFlip1.ogg','bookFlip2.ogg','bookFlip3.ogg']),
 'Clarify':('K01',['question_001.ogg']), 'Positive':('K01',['confirmation_001.ogg']),
 'Negative':('K01',['error_002.ogg']), 'Ready':('K01',['pluck_001.ogg']),
 'DayOpen':('K04',['Pizzicato jingles/jingles_PIZZI02.ogg']),
 'DayClose':('K02',['doorClose_2.ogg']), 'DaySuccess':('K04',['Pizzicato jingles/jingles_PIZZI00.ogg']),
 'DayFailure':('K01',['error_003.ogg']),
}
for cue,(id,files) in simple.items():
    for i,file in enumerate(files): save('Sfx',f'{cue}__{i+1:02}',raw(id,file),[id])
for i in range(3):
    plate=transient('F06',.55,i)
    dough=transient('F04',.30,i)
    cloth=raw('K02',f'cloth{i+1}.ogg')[:int(.35*RATE)]
    save('Sfx',f'Plate__{i+1:02}',mix((plate,0,.5),(dough,.06,.4)),['F06','F04'])
    save('Sfx',f'Fold__{i+1:02}',filter_audio(mix((cloth,0,.3),(dough,.04,.65)),100,4000),['K02','F04'])
    scoop=transient('F05',.24,i)
    save('Sfx',f'Curd__{i+1:02}',scoop,['F05'])
    save('Sfx',f'Mushrooms__{i+1:02}',filter_audio(scoop,180,4500)[:int(.17*RATE)],['F05'])
    save('Sfx',f'Flip__{i+1:02}',mix((raw('K02',f'metalPot{i+1}.ogg')[:int(.25*RATE)],0,.18),(cloth,.06,.5),(dough,.18,.7)),['K02','F04'])
    save('Sfx',f'Discard__{i+1:02}',mix((raw('K03',f'impactSoft_medium_00{i}.ogg'),0,.8),(plate,.06,.25)),['K03','F06'])
    save('Sfx',f'Scrape__{i+1:02}',filter_audio(mix((transient('F07',.5,i),0,.3),(dough,0,.7)),130,3800),['F07','F04'])
for cue,seconds in [('Payment',.65),('Tip',.4),('Refund',.55)]:
    for i in range(2): save('Sfx',f'{cue}__{i+1:02}',transient('F10',seconds,i+1),['F10'])
save('Sfx','GuestEnter__01',transient('F09',1.1),['F09'])
save('Sfx','OrderAccept__01',raw('K02','bookFlip1.ogg'),['K02'])
save('Sfx','Tray__01',transient('F06',.4,2),['F06'])
save('Sfx','Serve__01',mix((transient('F08',.65),0,.65),(transient('F06',.3,1),.4,.4)),['F08','F06'])
save('Sfx','Purchase__01',mix((raw('K02','handleCoins.ogg'),0,.6),(raw('K01','confirmation_002.ogg'),.15,.4)),['K02','K01'])
save('Sfx','Patience__01',mix((raw('K01','tick_001.ogg'),0,1),(raw('K01','tick_001.ogg'),.19,.75)),['K01'])
save('Sfx','Burnt__01',steady('F01',.8,start=30,high=8000),['F01'])
for cue,id,v in [('ButterStroke','F02',0),('JamStroke','F04',2),('HoneyStroke','F03',0)]:
    save('Sfx',cue+'__01',transient(id,.38,v),[id])
save('Loops','frying',steady('F01',8),['F01'],loop=True)
save('Loops','batter',mix((steady('F04',1.4,high=3800),0,.75),(steady('F03',1.4,high=3800),0,.25)),['F04','F03'],loop=True)
save('Loops','butter',steady('F02',1.3,high=4000),['F02'],loop=True)
save('Loops','jam',steady('F04',1.5,high=3600),['F04'],loop=True)
save('Loops','honey',mix((steady('F04',1.4,high=2600),0,.65),(steady('F03',1.4,high=2600),0,.35)),['F04','F03'],loop=True)
save('Loops','cafe',steady('F12',40,high=2400),['F12'],loop=True)
for name,id in [('day','M01'),('menu','M02')]: save('Music',name,read(id),[id],loop=True,music=True)

sources=[]
for id in sorted(used):
    s=dict(catalog[id]); s.pop('auditioned',None)
    if id.startswith('F'):
        s.update(json.loads((SOURCE/'public-previews'/(id+'.json')).read_text(encoding='utf-8')))
    elif id.startswith('K'):
        shutil.copyfile(WORKSPACE/s['local_path']/'License.txt',DEST/'Licenses'/(id+'-Kenney.txt'))
    if id in ('M01','M02'):
        slug='one-step-at-a-time' if id=='M01' else 'revelation'
        shutil.copyfile(SOURCE/'license-evidence'/(slug+'.html'),DEST/'Licenses'/(id+'-source.html'))
    sources.append(s)
manifest=dict(date='2026-10-14',license='CC0-1.0',sources=sources,clips=records,
              notes='Freesound inputs are public HQ MP3 previews. No lossless-original claim. Derived clips are edited for this game.')
(DEST/'audio-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
credits=['Блинопёк — звуковые источники, 14.10.2026','Все перечисленные источники: CC0 1.0. Коммерческое использование и изменение разрешены; атрибуция добровольная.',
 'https://creativecommons.org/publicdomain/zero/1.0/','', 'Записи Freesound получены как публичные HQ MP3 preview; они не являются WAV-оригиналами.','']
for s in sources: credits += [s['title']+' — '+s['author'],s['url'],'']
(DEST/'CREDITS.txt').write_text('\n'.join(credits),encoding='utf-8')
print(json.dumps(dict(clips=len(records),sources=len(sources),bytes=sum((DEST/r['file']).stat().st_size for r in records)),ensure_ascii=False))
