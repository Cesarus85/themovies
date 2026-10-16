"""Independent effect grammar, room budgets and physical FireLight marker audit."""
import json
import math
import re
from pathlib import Path

BUDGETS={'Cinema':16,'Home':12,'Rooftop':14,'Space':14,'OrientExpress':14,'Jet':14}
BOUNDS={'Cinema':((-12,-7,-1),(12,15,11)), 'Home':((-4.2,-2.35,0),(4.2,5.45,2.75)),
        'Rooftop':((-6,-4.5,0),(6,9,6.4)), 'Space':((-8,-5,0),(8,11,6.4)),
        'OrientExpress':((-1.5,-9,0),(1.5,7,2.7)), 'Jet':((-3,-10,0),(3,7,3.4))}
EXPECTED={
 'Cinema':{'Bulb_emit_flicker2':{'flicker':2}},
 'Home':{'Home_Fire_glow_flicker30':{'flicker':30},'Home_Candle_emit_flicker15':{'flicker':15},'Home_Night_unlit_twinkle12':{'twinkle':12}},
 'Rooftop':{'Rooftop_Festoon_bulb_flicker4':{'flicker':4},'Rooftop_Candle_bulb_flicker20':{'flicker':20},'Rooftop_Panorama_unlit_twinkle10':{'twinkle':10},'Rooftop_City_Windows_unlit_twinkle10':{'twinkle':10}},
 'Space':{'Space_Navigation_unlit_blink16':{'blink':16}},
 'OrientExpress':{'OE_Silk_glow_flicker2':{'flicker':2}},'Jet':{}}


def parse(name):
    result={}
    for token in name.lower().split('_'):
        for key in ('scrollv','scroll','flicker','twinkle','blink'):
            if not token.startswith(key):continue
            match=re.fullmatch(key+r'([0-9]+)',token)
            assert match,(name,'malformed motion keyword',token)
            n=int(match[1]);assert n>0,(name,'non-positive effect')
            assert key not in result,(name,'duplicate keyword',key)
            if key in ('flicker','twinkle'):assert n<=100,(name,'percent out of range')
            result[key]=n;break
    if any(k in result for k in ('flicker','blink')):
        assert any(k in name.lower() for k in ('emit','glow','bulb','unlit')),(name,'effect without emission')
    return result


def audit(scene,room,root):
    objects=[o for o in scene.objects if o.type=='MESH'];mats={m.name:m for o in objects for m in o.data.materials}
    assert len(mats)<=BUDGETS[room],(room,'material budget',len(mats))
    effects={n:parse(n) for n in mats if parse(n)}
    for n,want in EXPECTED[room].items():assert effects.get(n)==want,(room,n,'missing/wrong effect')
    # No extra effect on the accepted lamps, sky or moving landscape.
    new={n:e for n,e in effects.items() if any(k in e for k in ('flicker','twinkle','blink','scrollv'))}
    assert new==EXPECTED[room],(room,'unexpected added motion',new)
    for m in mats.values():
        bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        assert not bs.inputs['Alpha'].is_linked and bs.inputs['Alpha'].default_value==1,(m.name,'alpha forbidden')
    markers=[o for o in scene.objects if o.name.lower().startswith('firelight')]
    assert len(markers)<=4,(room,'FireLight budget')
    assert len(markers)=={'Home':3,'Rooftop':4}.get(room,0),(room,'expected FireLight count')
    lights=[];lo,hi=BOUNDS[room]
    for o in markers:
        match=re.fullmatch(r'FireLight_([1-9][0-9]*)',o.name)
        assert o.type=='EMPTY' and match,(o.name,'invalid FireLight marker')
        radius=int(match[1])/100;assert .1<=radius<=10,(o.name,'radius')
        p=o.matrix_world.translation
        assert all(math.isfinite(p[i]) and lo[i]<p[i]<hi[i] for i in range(3)),(o.name,'outside occupied room',list(p))
        lights.append({'name':o.name,'radius_m':radius,'position_m':list(p)})
    if room=='Home':
        hearth=next(o for o in markers if o.name=='FireLight_350')
        assert -3.8<hearth.matrix_world.translation.x<-3.4 and 1.5<hearth.matrix_world.translation.y<1.9
    if room=='Space':
        assert not any('twinkle' in n.lower() for n in mats)
        assert scene.objects.get('Earth_rotate5520') is not None
    result={'validation':'PASS','materials':len(mats),'budget':BUDGETS[room],'effects':effects,'firelights':lights,'opaque':True}
    path=Path(root)/'Logs/art-preview'/f'{room.lower()}-motion-verification.json';path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(result,indent=2)+'\n')
    return result
