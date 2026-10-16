"""Baked-only interior previews; analytic day/night shader is preview-only."""
import math
import bpy
from mathutils import Vector
from lib_space_room import SUN


def aim(o,target):o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()


def planet_preview(material,tex):
    nt=material.node_tree;nt.nodes.clear();n=nt.nodes;l=nt.links
    out=n.new('ShaderNodeOutputMaterial');em=n.new('ShaderNodeEmission');l.new(em.outputs[0],out.inputs['Surface'])
    uv=n.new('ShaderNodeUVMap');uv.uv_map='UVMap'
    day=n.new('ShaderNodeTexImage');day.image=bpy.data.images.load(str(tex/'Earth_Planet_BaseColor.png'),check_existing=True)
    night=n.new('ShaderNodeTexImage');night.image=bpy.data.images.load(str(tex/'Earth_Planet_Night.png'),check_existing=True)
    for t in [day,night]:l.new(uv.outputs['UV'],t.inputs['Vector'])
    geo=n.new('ShaderNodeNewGeometry')
    dot=n.new('ShaderNodeVectorMath');dot.operation='DOT_PRODUCT';l.new(geo.outputs['Normal'],dot.inputs[0]);dot.inputs[1].default_value=Vector(SUN).normalized()
    ramp=n.new('ShaderNodeMapRange');l.new(dot.outputs['Value'],ramp.inputs['Value']);ramp.inputs['From Min'].default_value=-.045;ramp.inputs['From Max'].default_value=.065;ramp.clamp=True;ramp.interpolation_type='SMOOTHERSTEP'
    shade=n.new('ShaderNodeMath');shade.operation='MULTIPLY_ADD';l.new(dot.outputs['Value'],shade.inputs[0]);shade.inputs[1].default_value=1.1;shade.inputs[2].default_value=.20;shade.use_clamp=True
    shaded=n.new('ShaderNodeMixRGB');shaded.blend_type='MULTIPLY';shaded.inputs[0].default_value=1;l.new(day.outputs['Color'],shaded.inputs[1]);l.new(shade.outputs[0],shaded.inputs[2])
    lights=n.new('ShaderNodeMixRGB');lights.blend_type='MULTIPLY';lights.inputs[0].default_value=1;l.new(night.outputs[0],lights.inputs[1]);lights.inputs[2].default_value=(1.1,1.1,1.1,1)
    mix=n.new('ShaderNodeMixRGB');l.new(ramp.outputs['Result'],mix.inputs[0]);l.new(lights.outputs[0],mix.inputs[1]);l.new(shaded.outputs[0],mix.inputs[2])
    # Thin blue inner atmosphere rim derived from angle, never baked into albedo.
    facing=n.new('ShaderNodeVectorMath');facing.operation='DOT_PRODUCT';l.new(geo.outputs['Normal'],facing.inputs[0]);l.new(geo.outputs['Incoming'],facing.inputs[1])
    absolute=n.new('ShaderNodeMath');absolute.operation='ABSOLUTE';l.new(facing.outputs['Value'],absolute.inputs[0])
    inverse=n.new('ShaderNodeMath');inverse.operation='SUBTRACT';inverse.inputs[0].default_value=1;l.new(absolute.outputs[0],inverse.inputs[1])
    power=n.new('ShaderNodeMath');power.operation='POWER';power.inputs[1].default_value=7;l.new(inverse.outputs[0],power.inputs[0])
    rim=n.new('ShaderNodeMixRGB');rim.blend_type='MULTIPLY';rim.inputs[0].default_value=1;rim.inputs[1].default_value=(.035,.22,.75,1);l.new(power.outputs[0],rim.inputs[2])
    add=n.new('ShaderNodeMixRGB');add.blend_type='ADD';add.inputs[0].default_value=1;l.new(mix.outputs[0],add.inputs[1]);l.new(rim.outputs[0],add.inputs[2]);l.new(add.outputs[0],em.inputs[0])


def preview(root,tex,configure_cycles,quick=False):
    scene=bpy.context.scene;dest=root/'Logs/art-preview'
    atlas=bpy.data.images.load(str(tex/'Space_Light.png'),check_existing=True);dimmers=[]
    for m in bpy.data.materials:
        if not m.get('space'):continue
        if 'planet' in m.name.lower():planet_preview(m,tex);continue
        nt=m.node_tree;nt.nodes.clear();out=nt.nodes.new('ShaderNodeOutputMaterial');em=nt.nodes.new('ShaderNodeEmission')
        base=nt.nodes.new('ShaderNodeTexImage');base.image=bpy.data.images.load(str(tex/f'{m.name}_BaseColor.png'),check_existing=True)
        uv=nt.nodes.new('ShaderNodeUVMap');uv.uv_map='UVMap';nt.links.new(uv.outputs[0],base.inputs[0])
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
    scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[1].default_value=0
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0;scene.compositing_node_group=None
    configure_cycles(scene);scene.cycles.samples=24 if quick else 64
    scene.render.resolution_x=1200 if quick else 1800;scene.render.resolution_y=750 if quick else 1125;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='8'
    data=bpy.data.cameras.new('Space preview');cam=bpy.data.objects.new('Space preview',data);scene.collection.objects.link(cam);scene.camera=cam
    views=[('space-seat2-on',(-.375,0,1.25),(0,8.3,2.75),17,1),('space-seat2-04pct',(-.375,0,1.25),(0,8.3,2.75),17,.04),
           ('space-side',(-.375,0,1.25),(9,7.5,1.1),19,1),('space-overview',(6.8,-3.8,4.3),(-.7,3.4,1.55),19,1),
           ('space-lounge',(-5.8,4.5,2.65),(.3,-1,1.2),23,1)]
    for name,loc,target,lens,dim in views:
        cam.location=loc;aim(cam,target);data.lens=lens;data.clip_start=.04;data.clip_end=3000
        for sock in dimmers:sock.default_value=2*dim
        scene.render.filepath=str(dest/f'{name}.png');bpy.ops.render.render(write_still=True)
    # Separate maximum-screen occlusion demonstration; helper never enters FBX/checkpoint.
    from lib_space_geometry import Geometry
    m=bpy.data.materials.new('Preview projection only');m.use_nodes=True;nt=m.node_tree;nt.nodes.clear()
    out=nt.nodes.new('ShaderNodeOutputMaterial');em=nt.nodes.new('ShaderNodeEmission');em.inputs[0].default_value=(.009,.019,.032,1);nt.links.new(em.outputs[0],out.inputs[0])
    g=Geometry();g.add(m,[(-4.75,8.3,1.35),(4.75,8.3,1.35),(4.75,8.3,5.35),(-4.75,8.3,5.35)],[(3,2,1,0)]);screen=g.objects()[0]
    for body,z,size in [('O R B I T',3.45,.46),('PRIVATE SCREENING  /  04',2.92,.13)]:
        d=bpy.data.curves.new('Preview lettering','FONT');d.body=body;d.align_x='CENTER';d.size=size
        obj=bpy.data.objects.new('Preview lettering',d);scene.collection.objects.link(obj);obj.location=(0,8.29,z);obj.rotation_euler=(math.pi/2,0,0)
        white=bpy.data.materials.get('Preview text')
        if not white:
            white=bpy.data.materials.new('Preview text');white.use_nodes=True;bs=white.node_tree.nodes.get('Principled BSDF');bs.inputs['Emission Color'].default_value=(.25,.42,.5,1);bs.inputs['Emission Strength'].default_value=1
        d.materials.append(white)
    cam.location=(-.375,0,1.25);aim(cam,(0,8.3,2.75));data.lens=17
    for sock in dimmers:sock.default_value=2
    scene.render.filepath=str(dest/'space-seat2-max-screen.png');bpy.ops.render.render(write_still=True)
