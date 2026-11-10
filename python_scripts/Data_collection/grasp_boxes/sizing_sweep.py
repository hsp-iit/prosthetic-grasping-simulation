import os, numpy as np, copy, measure_grasp_boxes as m
P=os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
idx=m.guid_index(P)
def load(obj,name):
    pf=m.parse_prefab(f'{P}/Assets/Prefabs/YCB/{obj}.prefab')
    v,f,c=m.object_mesh(P,pf,idx)
    return m.Tris(v,f),c,next(b for b in pf['boxes'] if b['name']==name)
def rep(tr,c,obj,b,s,lab):
    lx=min(m.sweep_limit(tr,b,s,0),m.sweep_limit(tr,b,s,180)); ly=min(m.sweep_limit(tr,b,s,90),m.sweep_limit(tr,b,s,270))
    r=m.monte_carlo(tr,c,obj,b,s,2000,seed=5)
    print(f"  {lab:34s} xlim {lx:5.0f} ylim {ly:5.0f} pass {r['pass_rate']*100:5.1f}% realised {r.get('th_med',float('nan')):4.0f} ({r.get('th_p5',float('nan')):.0f}-{r.get('th_p95',float('nan')):.0f})")
print('A: mustard large_diameter[right], z horizontal, d=3.4 mm; vary b (a kept)')
tr,c,b0=load('006_mustard_bottle','large_diameter[right]')
for bb in [1,2,3.4,4.6,6,10,20]:
    b=copy.deepcopy(b0); b['half'][1]=bb*1e-3; rep(tr,c,'006_mustard_bottle',b,-1,f'b={bb} mm formula {np.degrees(np.arctan2(bb,3.4)):.0f}')
print('B: same box, vary a (b kept 4.6)')
for aa in [0.5,1,2,5,29.2]:
    b=copy.deepcopy(b0); b['half'][0]=aa*1e-3; rep(tr,c,'006_mustard_bottle',b,-1,f'a={aa} mm formula {np.degrees(np.arctan2(aa,3.4)):.0f}')
print('C: same box, push centre along z, sizes kept (d error)')
z=b0['axes'][:,2]
for dd in [-2,-1,1,2]:
    b=copy.deepcopy(b0); b['center']=b0['center']+z*(dd*1e-3)   # outward is -z, so +z goes deeper
    rep(tr,c,'006_mustard_bottle',b,-1,f'd={3.4+dd:.1f} mm')
print('D: small box, same ratio b/d=1, shifted +-1 mm: sensitivity')
for dd0,bb in [(1,1),(5,5)]:
    for e in [-0.5,0,1]:
        d=dd0+e; print(f'  d0={dd0} b={bb} d={d}: flat formula {np.degrees(np.arctan2(bb,d)):.0f}')
print('E: potted sphere_4fingers, z vertical, d=3.8; scale a and b together')
tr,c,b0=load('010_potted_meat_can','sphere_4fingers')
for k in [0.5,1,1.5,2,3,5]:
    b=copy.deepcopy(b0); b['half'][:2]=b0['half'][:2]*k; rep(tr,c,'010_potted_meat_can',b,-1,f'a,b x{k}: {b["half"][0]*1e3:.1f},{b["half"][1]*1e3:.1f}')
print('F: spatula adducted_thumb[right] thin part, vary a,b')
tr,c,b0=load('033_spatula','adducted_thumb[right]')
for aa,bb in [(1.2,1.4),(2,1.4),(1.2,3),(2,3),(3,5)]:
    b=copy.deepcopy(b0); b['half'][:2]=[aa*1e-3,bb*1e-3]; rep(tr,c,'033_spatula',b,1,f'a={aa} b={bb}')
