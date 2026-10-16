"""Baked-look previews: diffuse base x atlas x2; glow dims; scenery stays unlit."""
import bpy
from mathutils import Vector


def aim(o,target):o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()


def preview(root,tex,configure_cycles,quick=False):
    scene=bpy.context.scene;dest=root/'Logs/art-preview'
    atlas=bpy.data.images.load(str(tex/'OrientExpress_Light.png'),check_existing=True);dimmers=[]
    for m in bpy.data.materials:
        if not m.get('orient_express'):continue
        nt=m.node_tree;nt.nodes.clear();n=nt.nodes;l=nt.links
        out=n.new('ShaderNodeOutputMaterial');em=n.new('ShaderNodeEmission')
        base=n.new('ShaderNodeTexImage');base.image=bpy.data.images.load(str(tex/f'{m.name}_BaseColor.png'),check_existing=True)
        uv=n.new('ShaderNodeUVMap');uv.uv_map='UVMap';l.new(uv.outputs[0],base.inputs[0])
        lm=n.new('ShaderNodeTexImage');lm.image=atlas;lm.extension='EXTEND';uv2=n.new('ShaderNodeUVMap');uv2.uv_map='Lightmap';l.new(uv2.outputs[0],lm.inputs[0])
        mul=n.new('ShaderNodeMixRGB');mul.blend_type='MULTIPLY';mul.inputs[0].default_value=1;l.new(base.outputs[0],mul.inputs[1]);l.new(lm.outputs[0],mul.inputs[2])
        unlit='unlit' in m.name;glow='glow' in m.name;l.new(base.outputs[0] if unlit or glow else mul.outputs[0],em.inputs[0]);em.inputs[1].default_value=1 if unlit else 2;l.new(em.outputs[0],out.inputs[0])
        if not unlit:dimmers.append(em.inputs[1])
    for o in list(scene.objects):
        if o.type in ('LIGHT','CAMERA'):bpy.data.objects.remove(o,do_unlink=True)
    scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[1].default_value=0
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0;scene.compositing_node_group=None
    configure_cycles(scene);scene.cycles.samples=24 if quick else 64
    scene.render.resolution_x=1200 if quick else 1800;scene.render.resolution_y=800 if quick else 1200;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='8'
    data=bpy.data.cameras.new('OrientExpress preview');cam=bpy.data.objects.new('OrientExpress preview',data);scene.collection.objects.link(cam);scene.camera=cam
    views=[('orient-express-seat2-on',(-.375,0,1.25),(0,6.65,1.55),18,1),
           ('orient-express-seat2-04pct',(-.375,0,1.25),(0,6.65,1.55),18,.04),
           ('orient-express-window-left',(.3,1.2,1.25),(-5,2.1,1.3),23,1),
           ('orient-express-window-right',(-.3,1.2,1.25),(5,2.1,1.3),23,1),
           ('orient-express-overview',(1.12,-8.95,2.18),(-.15,1.8,1.0),16,1),
           ('orient-express-salon',(1.05,2.0,1.65),(-.3,-4,1.1),20,1)]
    for name,loc,target,lens,dim in views:
        cam.location=loc;aim(cam,target);data.lens=lens;data.clip_start=.02;data.clip_end=600
        for sock in dimmers:sock.default_value=2*dim
        scene.render.filepath=str(dest/f'{name}.png');bpy.ops.render.render(write_still=True)
    # Additional projection mockup only, created AFTER FBX and baked checkpoint.
    from lib_orient_express_geometry import Geometry
    def emissive(name,color):
        mat=bpy.data.materials.new(name);mat.use_nodes=True;nt=mat.node_tree;nt.nodes.clear()
        out=nt.nodes.new('ShaderNodeOutputMaterial');em=nt.nodes.new('ShaderNodeEmission');em.inputs[0].default_value=(*color,1);nt.links.new(em.outputs[0],out.inputs[0]);return mat
    screenmat=emissive('Preview projection only',(.008,.018,.025));letter=emissive('Preview title only',(.58,.41,.21))
    g=Geometry();g.add(screenmat,[(-1.3,6.65,.9),(1.3,6.65,.9),(1.3,6.65,2.4),(-1.3,6.65,2.4)],[(3,2,1,0)]);g.objects()
    for body,z,size in [('A S T E R   V A L E',1.74,.16),('S A L O N   /   1 9 2 8',1.48,.065)]:
        d=bpy.data.curves.new('Preview lettering','FONT');d.body=body;d.align_x='CENTER';d.size=size;d.materials.append(letter)
        obj=bpy.data.objects.new('Preview lettering',d);scene.collection.objects.link(obj);obj.location=(0,6.64,z);obj.rotation_euler=(1.57079632679,0,0)
    cam.location=(-.375,0,1.25);aim(cam,(0,6.65,1.55));data.lens=18
    for sock in dimmers:sock.default_value=2
    scene.render.filepath=str(dest/'orient-express-seat2-projection.png');bpy.ops.render.render(write_still=True)
