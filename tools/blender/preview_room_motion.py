"""Static baked-export closeups for the requested fire and lantern review."""
import sys
sys.dont_write_bytecode=True
from pathlib import Path
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
ROOT=Path(__file__).resolve().parents[2]

def render(room,name,position,target,lens):
    folder=ROOT/'Assets/TheMovies/Art'/room;texdir=folder/'Textures'
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(folder/f'{room}.fbx'))
    scene=bpy.context.scene;scene.world=bpy.data.worlds.new('Black preview world');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=0
    atlas=bpy.data.images.load(str(texdir/f'{room}_Light.png'))
    for m in bpy.data.materials:
        nt=m.node_tree;nt.nodes.clear();nodes=nt.nodes
        out=nodes.new('ShaderNodeOutputMaterial');em=nodes.new('ShaderNodeEmission');nt.links.new(em.outputs[0],out.inputs['Surface'])
        base=nodes.new('ShaderNodeTexImage');base.image=bpy.data.images.load(str(texdir/f'{m.name}_BaseColor.png'),check_existing=True)
        uv=nodes.new('ShaderNodeUVMap');uv.uv_map='UVMap';nt.links.new(uv.outputs[0],base.inputs[0])
        if 'unlit' in m.name:nt.links.new(base.outputs[0],em.inputs[0]);continue
        lm=nodes.new('ShaderNodeTexImage');lm.image=atlas;lm.extension='EXTEND'
        uv2=nodes.new('ShaderNodeUVMap');uv2.uv_map='Lightmap';nt.links.new(uv2.outputs[0],lm.inputs[0])
        mult=nodes.new('ShaderNodeMixRGB');mult.blend_type='MULTIPLY';mult.inputs[0].default_value=1
        nt.links.new(base.outputs[0],mult.inputs[1]);nt.links.new(lm.outputs[0],mult.inputs[2]);nt.links.new(mult.outputs[0],em.inputs[0]);em.inputs[1].default_value=2
        if any(k in m.name.lower() for k in ('glow','emit','bulb')):
            add=nodes.new('ShaderNodeMixRGB');add.blend_type='ADD';add.inputs[0].default_value=.40
            nt.links.new(mult.outputs[0],add.inputs[1]);nt.links.new(base.outputs[0],add.inputs[2]);nt.links.new(add.outputs[0],em.inputs[0])
    scene.render.engine='CYCLES';scene.cycles.samples=48;scene.cycles.use_denoising=True
    prefs=bpy.context.preferences.addons['cycles'].preferences
    try:
        prefs.compute_device_type='METAL';prefs.get_devices()
        for d in prefs.devices:d.use=d.type=='METAL'
        scene.cycles.device='GPU'
    except Exception:scene.cycles.device='CPU'
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0
    scene.render.resolution_x=1500;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='8'
    data=bpy.data.cameras.new(name);cam=bpy.data.objects.new(name,data);scene.collection.objects.link(cam);scene.camera=cam
    cam.location=position;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();data.lens=lens;data.clip_start=.025;data.clip_end=350
    scene.render.filepath=str(ROOT/'Logs/art-preview'/f'{name}.png');bpy.ops.render.render(write_still=True)

render('Home','home-fireplace-motion-closeup',(-1.90,.52,1.35),(-3.93,1.70,1.02),31)
render('Rooftop','rooftop-lanterns-motion-closeup',(-2.1,2.65,1.7),(-.72,.43,.56),31)
