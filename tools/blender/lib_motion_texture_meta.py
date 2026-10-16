"""Keep existing texture GUIDs across effect renames, without launching Unity."""
from pathlib import Path
import re
import uuid

RENAMES={
 'Cinema':{'Bulb_emit':'Bulb_emit_flicker2'},
 'Home':{'Home_Embers_glow':'Home_Fire_glow_flicker30','Home_Limestone':'Home_Mineral_Atlas','Home_Night_unlit':'Home_Night_unlit_twinkle12'},
 'Rooftop':{'Rooftop_Olive_Leaves':'Rooftop_Foliage_Atlas','Rooftop_Festoon_bulb':'Rooftop_Festoon_bulb_flicker4','Rooftop_City_Windows_unlit':'Rooftop_City_Windows_unlit_twinkle10','Rooftop_Panorama_unlit':'Rooftop_Panorama_unlit_twinkle10'},
 'OrientExpress':{'OE_Silk_glow':'OE_Silk_glow_flicker2'},'Space':{}}
REMOVED={'Home':['Home_Warm_Plaster','Home_Charcoal'],'Rooftop':['Rooftop_Lavender']}
NEW={'Home':{'Home_Candle_emit_flicker15':'Home_Linen_emit'},'Rooftop':{'Rooftop_Candle_bulb_flicker20':'Rooftop_Festoon_bulb_flicker4'},'Space':{'Space_Navigation_unlit_blink16':'Space_Instruments_glow'}}


def sync(tex,room):
    def path(n):return tex/f'{n}_BaseColor.png.meta'
    for old,new in RENAMES.get(room,{}).items():
        if path(old).exists() and (tex/f'{new}_BaseColor.png').exists():
            assert not path(new).exists(),('competing GUIDs',old,new)
            path(old).rename(path(new))
    for old in REMOVED.get(room,[]):
        assert not (tex/f'{old}_BaseColor.png').exists()
        path(old).unlink(missing_ok=True)
    for new,source in NEW.get(room,{}).items():
        if path(new).exists():continue
        template=path(source)
        if not template.exists():continue # Fresh plain art checkout: importer can create it later.
        guid=uuid.uuid5(uuid.NAMESPACE_URL,'themovies-art/'+room+'/'+new).hex
        content=re.sub(r'(?m)^guid: [0-9a-f]+$',f'guid: {guid}',template.read_text())
        path(new).write_text(content)
