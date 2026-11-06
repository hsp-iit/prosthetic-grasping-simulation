import json,math,re,os,sys,collections
import numpy as np
import measure_grasp_boxes as m
P=os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
RESULTS=sys.argv[1] if len(sys.argv)>1 else 'grasp_box_angles.json'  # written by measure_grasp_boxes.py run
R=[r for r in json.load(open(RESULTS)) if r['in_dataset'] and r['mesh_blocks']]
gt=lambda n:re.sub(r'\[.*?\]','',n)
for r in R:
    r['gt']=gt(r['box'])
    L=[r['lim_xp'],r['lim_xm']]; r['limx']=min(L) if not any(x is None or (isinstance(x,float) and math.isnan(x)) for x in L) else None
    L=[r['lim_yp'],r['lim_ym']]; r['limy']=min(L) if not any(x is None or (isinstance(x,float) and math.isnan(x)) for x in L) else None
    r['pass']=r['pass_rate']*100
    r['vertical']=float(r['z_vs_object_up']<45)
def eta2(key,group='gt'):
    rows=[r for r in R if r[key] is not None]
    y=np.array([r[key] for r in rows],float); g=[r[group] for r in rows]
    ss=((y-y.mean())**2).sum(); b=0
    for k in set(g):
        v=y[[i for i,x in enumerate(g) if x==k]]; b+=len(v)*(v.mean()-y.mean())**2
    return b/ss if ss else float('nan'),len(rows),len(set(g))
print('share of variance explained by grouping (eta^2, n, groups)')
for key in ['limx','limy','pass','th_med','th_p95','a_mm','b_mm','c_mm','d_mm','vertical']:
    print(f'{key:9s} grasp type {eta2(key)[0]:.2f}  z orientation {eta2(key,"vertical")[0]:.2f}  object {eta2(key,"object")[0]:.2f}  (n={eta2(key)[1]})')
print('\nper grasp type: n | objects | z | a,b,c,d ranges | limx,limy | realised med | pass')
by=collections.defaultdict(list)
for r in R: by[r['gt']].append(r)
def rg(v):
    v=[x for x in v if x is not None]
    return f'{min(v):.0f}-{max(v):.0f}' if v else '-'
def rg1(v):
    v=[x for x in v if x is not None]
    return f'{min(v):.1f}-{max(v):.1f}' if v else '-'
for k,v in sorted(by.items()):
    print(k,len(v),len({r['object'] for r in v}),'zUp',rg([r['z_vs_object_up'] for r in v]),'a',rg1([r['a_mm'] for r in v]),'b',rg1([r['b_mm'] for r in v]),'c',rg1([r['c_mm'] for r in v]),'d',rg1([r['d_mm'] for r in v]),'limx',rg([r['limx'] for r in v]),'limy',rg([r['limy'] for r in v]),'th',rg([r['th_med'] for r in v]),'pass',rg([r['pass'] for r in v]),'blocked',sum(r['limx'] is None for r in v))
# geometry model: limit predicted from a/d, b/d only where centre under surface
rows=[]
for r in R:
    if r['d_mm'] and r['d_mm']>0:
        for pr,l in ((r['pred_x'],r['limx']),(r['pred_y'],r['limy'])):
            if l is not None: rows.append((pr,l,r['gt']))
pr=np.array([x[0] for x in rows]);l=np.array([x[1] for x in rows])
print('\nformula vs min limit n',len(rows),'r2',1-((l-pr)**2).sum()/((l-l.mean())**2).sum(),'corr',np.corrcoef(pr,l)[0,1])
cov=[r for r in R if r['footprint_cover']>=.99]
# overlap: same pose, which grasp types pass
print('\nsame camera+object pose: boxes passing per object (scissors with a working collider)')
idx=m.guid_index(P); sc=m.scene(); rng=np.random.default_rng(7); N=3000
tot=[]
for obj in m.DATASET:
    pf=m.parse_prefab(f'{P}/Assets/Prefabs/YCB/{obj}.prefab')
    if len({gt(b['name']) for b in pf['boxes']})<2 and len(pf['boxes'])<2: continue
    v,f,c=m.object_mesh(P,pf,idx); tr=m.Tris(v,f)
    res={}
    for flip in (0.0,180.0):
        xs=rng.uniform(-sc['ext_x'],sc['ext_x'],N); zs=rng.uniform(-sc['ext_z'],sc['ext_z'],N)
        y=(c[:,1].max()-c[:,1].min())/2
        roots=sc['plane_pos']+np.c_[xs,np.full(N,y),zs]; yaw=rng.uniform(0,360,N)
        cams=m.WRIST_POS+np.c_[np.zeros(N),rng.uniform(-sc['wrist_ext_y'],sc['wrist_ext_y'],N),rng.uniform(-sc['wrist_ext_z'],sc['wrist_ext_z'],N)]
        cl=np.array([m.euler_y_z(yaw[i],flip).T@(cams[i]-roots[i]) for i in range(N)])
        ok={}
        for b in pf['boxes']:
            n=b['name']
            if (flip==180 and not('[usd]' in n or '[susd]' in n)) or (flip==0 and '[usd]' in n): continue
            ok[n]=m.visible(tr,b,cl)
        if not ok: continue
        names=list(ok); M=np.array([ok[n] for n in names])
        types=sorted({gt(n) for n in names})
        T=np.array([np.any([ok[n] for n in names if gt(n)==t],axis=0) for t in types])
        any_=T.any(0); two=T.sum(0)>=2
        res[flip]=(names,types,any_.mean(),two[any_].mean() if any_.any() else 0,two.mean())
        print(f'{obj:20s} z={flip:3.0f} types={types} pass any={any_.mean()*100:5.1f}% | of those, >=2 grasp types pass={res[flip][3]*100:5.1f}%')
        if len(types)>=2: tot.append(res[flip][3])
print('median overlap, poses with >=2 candidate types',np.median(tot),'n',len(tot))
