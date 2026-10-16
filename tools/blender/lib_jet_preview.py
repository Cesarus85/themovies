"""Runtime-style base color x saved atlas x 2 previews, with 4% dimming."""
import bpy
import re
from mathutils import Vector

def aim(o,target):o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()

def preview(root,tex,configure_cycles,quick=False,motion=False):
    scene=bpy.context.scene;dest=root/'Logs/art-preview'
    atlas=bpy.data.images.load(str(tex/'Jet_Light.png'),check_existing=True);dimmers=[];offsets=[]
    for m in bpy.data.materials:
        if not m.get('jet'):continue
        nt=m.node_tree;nt.nodes.clear();out=nt.nodes.new('ShaderNodeOutputMaterial');em=nt.nodes.new('ShaderNodeEmission')
        base=nt.nodes.new('ShaderNodeTexImage');base.image=bpy.data.images.load(str(tex/f'{m.name}_BaseColor.png'),check_existing=True)
        uv=nt.nodes.new('ShaderNodeUVMap');uv.uv_map='UVMap';nt.links.new(uv.outputs[0],base.inputs[0])
        if motion and 'scroll' in m.name:
            shift=nt.nodes.new('ShaderNodeVectorMath');shift.operation='ADD'
            nt.links.new(uv.outputs[0],shift.inputs[0]);nt.links.new(shift.outputs[0],base.inputs[0])
            offsets.append((shift.inputs[1],int(re.search(r'scroll(\d+)',m.name)[1])/10000))
        lm=nt.nodes.new('ShaderNodeTexImage');lm.image=atlas;lm.extension='EXTEND';uv2=nt.nodes.new('ShaderNodeUVMap');uv2.uv_map='Lightmap';nt.links.new(uv2.outputs[0],lm.inputs[0])
        mul=nt.nodes.new('ShaderNodeMixRGB');mul.blend_type='MULTIPLY';mul.inputs[0].default_value=1
        nt.links.new(base.outputs[0],mul.inputs[1]);nt.links.new(lm.outputs[0],mul.inputs[2])
        unlit='unlit' in m.name.lower();nt.links.new(base.outputs[0] if unlit else mul.outputs[0],em.inputs[0])
        # Runtime emissive keyword surfaces also dim with house lighting.
        if 'glow' in m.name.lower():nt.links.new(base.outputs[0],em.inputs[0])
        em.inputs[1].default_value=1 if unlit else 2
        nt.links.new(em.outputs[0],out.inputs[0])
        if not unlit:dimmers.append(em.inputs[1])
    for o in list(scene.objects):
        if o.type in ('LIGHT','CAMERA'):bpy.data.objects.remove(o,do_unlink=True)
    if scene.world is None:scene.world=bpy.data.worlds.new('Jet preview world')
    scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[1].default_value=0
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0;scene.compositing_node_group=None
    configure_cycles(scene);scene.cycles.samples=24 if quick else 64
    scene.render.resolution_x=1200 if quick else 1800;scene.render.resolution_y=750 if quick else 1125;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='8'
    data=bpy.data.cameras.new('Jet preview');cam=bpy.data.objects.new('Jet preview',data);scene.collection.objects.link(cam);scene.camera=cam
    views=[('jet-seat2-on',(-.375,0,1.25),(0,5.48,1.58),20,1),
           ('jet-seat2-04pct',(-.375,0,1.25),(0,5.48,1.58),20,.04),
           ('jet-window-left',(-2.1,1.70,1.25),(-3.15,2.0,1.20),28,1),
           ('jet-window-right',(2.1,1.70,1.25),(3.15,2.0,1.20),28,1),
           ('jet-overview',(2.35,-5.15,2.08),(-.25,1.35,1.05),17,1),
           ('jet-conference',(2.55,.95,1.9),(-.4,-3.3,.90),20,1),
           ('jet-seating',(-2.65,2.0,1.65),(.1,-.25,.70),24,1)]
    if motion:
        frames=dest/'jet-motion-frames';frames.mkdir(exist_ok=True)
        scene.render.resolution_x=960;scene.render.resolution_y=600;scene.cycles.samples=8
        scene.render.fps=24
        views=[(f'jet-motion-frames/{i:03d}',(-.375,0,1.25),(-3.15,.4,1.18),70,1) for i in range(72)]
    for frame,(name,loc,target,lens,dim) in enumerate(views):
        if motion:
            for socket,speed in offsets:socket.default_value=(speed*frame/24%1,0,0)
        cam.location=loc;aim(cam,target);data.lens=lens;data.clip_start=.04;data.clip_end=3000
        for sock in dimmers:sock.default_value=2*dim
        scene.render.filepath=str(dest/f'{name}.png');bpy.ops.render.render(write_still=True)
