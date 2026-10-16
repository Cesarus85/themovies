"""Apply effects to accepted exports. Run Blender -b -P ... -- Cinema Rooftop Space OrientExpress.
Home is rebuilt/baked by home.py; Jet deliberately remains byte-identical (no wing).
Material-only exports preserve the original binary FBX numeric properties exactly.
"""
import sys
sys.dont_write_bytecode=True
from pathlib import Path
import hashlib
import bpy
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from lib_room_motion import rooftop_effects,space_effects
from lib_motion_texture_meta import sync as sync_texture_meta

RENAMES={'Cinema':{'Bulb_emit':'Bulb_emit_flicker2'},'OrientExpress':{'OE_Silk_glow':'OE_Silk_glow_flicker2'}}


def rename_fbx(fbx,tex,mapping):
    from io_scene_fbx import parse_fbx,encode_bin,data_types
    source,version=parse_fbx.parse(str(fbx))
    methods={getattr(data_types,n.upper()):'add_'+n for n in ('bool','char','int8','int16','int32','int64','float32','float64','string','bytes','bool_array','byte_array','int32_array','int64_array','float32_array','float64_array')}
    def replacement(value):
        if not isinstance(value,bytes):return value
        for old,new in mapping.items():value=value.replace(old.encode(),new.encode())
        return value
    def clone(elem):
        out=encode_bin.FBXElem(elem.id)
        for kind,value in zip(elem.props_type,elem.props):getattr(out,methods[kind])(replacement(value))
        out.elems=[clone(e) for e in elem.elems];return out
    temporary=fbx.with_suffix('.motion.fbx');encode_bin.write(str(temporary),clone(source),version)
    result,v=parse_fbx.parse(str(temporary));assert v==version
    def compare(a,b):
        assert a.id==b.id and a.props_type==b.props_type and len(a.elems)==len(b.elems)
        for av,bv in zip(a.props,b.props):assert np.array_equal(replacement(av),bv),a.id
        for aa,bb in zip(a.elems,b.elems):compare(aa,bb)
    compare(source,result)
    for old,new in mapping.items():(tex/f'{old}_BaseColor.png').rename(tex/f'{new}_BaseColor.png')
    temporary.replace(fbx)


def preserve_reference_frames(fbx,accepted,room):
    """Keep accepted marker frames and orbital geometry/hierarchy verbatim.

    Blender FBX re-export otherwise applies its Y-up basis twice. Only IDs change
    to match the newly serialized connections; every orbital numeric field stays.
    """
    from io_scene_fbx import parse_fbx,encode_bin,data_types
    current,version=parse_fbx.parse(str(fbx))
    def objects(tree):return next(e for e in tree.elems if e.id==b'Objects')
    def key(e):return (e.id,e.props[1])
    names={b'Earth',b'Earth_rotate5520',b'Space_Panorama',b'Earth_Planet',b'Space_Panorama_unlit'} if room=='Space' else set()
    original={key(e):e for e in objects(accepted).elems
              if (e.id==b'Model' and e.props[2]==b'Null') or
                 (e.id in (b'Model',b'Geometry') and e.props[1].split(b'\x00')[0] in names)}
    assert len(original)>=6
    substitutions={}
    for e in objects(current).elems:
        if len(e.props)>1 and key(e) in original:substitutions[id(e)]=original[key(e)]
    assert len(substitutions)==len(original)
    methods={getattr(data_types,n.upper()):'add_'+n for n in ('bool','char','int8','int16','int32','int64','float32','float64','string','bytes','bool_array','byte_array','int32_array','int64_array','float32_array','float64_array')}
    def clone(e):
        original=substitutions.get(id(e));source=original or e;out=encode_bin.FBXElem(e.id)
        for i,(kind,value) in enumerate(zip(source.props_type,source.props)):
            getattr(out,methods[kind])(e.props[0] if original is not None and i==0 else value)
        out.elems=[clone(c) for c in source.elems];return out
    temporary=fbx.with_suffix('.orbit.fbx');encode_bin.write(str(temporary),clone(current),version)
    temporary.replace(fbx)


def update_room(root,room):
    out=root/'Assets/TheMovies/Art'/room;tex=out/'Textures';fbx=out/f'{room}.fbx'
    if room in RENAMES:
        mapping={a:b for a,b in RENAMES[room].items() if (tex/f'{a}_BaseColor.png').exists()}
        if mapping:rename_fbx(fbx,tex,mapping)
        sync_texture_meta(tex,room)
        return
    assert room in ('Rooftop','Space')
    sentinel='Rooftop_Candle_bulb_flicker20' if room=='Rooftop' else 'Space_Navigation_unlit_blink16'
    from io_scene_fbx import parse_fbx
    accepted_tree,accepted_version=parse_fbx.parse(str(fbx))
    atlas=tex/f'{room}_Light.png';before=hashlib.sha256(atlas.read_bytes()).hexdigest()
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(fbx));bpy.context.view_layer.update()
    if bpy.data.materials.get(sentinel):return
    (rooftop_effects if room=='Rooftop' else space_effects)(tex)
    # Rebind texture paths to this worktree; imported FBX paths may refer to the original builder checkout.
    for m in bpy.data.materials:
        for n in m.node_tree.nodes if m.use_nodes else []:
            if n.type=='TEX_IMAGE' and n.image:
                p=tex/Path(n.image.filepath).name
                assert p.exists(),p
                n.image.filepath=str(p)
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=False,object_types={'EMPTY','MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',use_space_transform=True,bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
    preserve_reference_frames(fbx,accepted_tree,room)
    assert hashlib.sha256(atlas.read_bytes()).hexdigest()==before
    sync_texture_meta(tex,room)


if __name__=='__main__':
    root=Path(__file__).resolve().parents[2]
    for room in sys.argv[sys.argv.index('--')+1:]:
        update_room(root,room);print('Motion export complete:',room,flush=True)
