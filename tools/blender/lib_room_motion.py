"""Subtle room effects and opaque, padded material atlases. No app dependencies.

Atlas consolidation clips only at UV repeat boundaries: surfaces, UV2 and explicit
normals are interpolated on the original triangles, never moved. Two-pixel periodic
borders avoid filtering adjacent tiles. Original maps are sampled at 1020/1024 size.
"""
import math
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector


def marker(name, position):
    assert name not in bpy.data.objects,name
    o=bpy.data.objects.new(name,None);bpy.context.scene.collection.objects.link(o)
    o.location=position;o.empty_display_type='SPHERE';o.empty_display_size=.12
    return o


def image_material(name, image, emission=0, tag=None):
    m=bpy.data.materials.new(name);m.use_nodes=True
    if tag:m[tag]=True
    n=m.node_tree.nodes;bs=n.get('Principled BSDF');bs.inputs['Roughness'].default_value=.85
    uv=n.new('ShaderNodeUVMap');uv.uv_map='UVMap';tex=n.new('ShaderNodeTexImage');tex.image=image
    m.node_tree.links.new(uv.outputs[0],tex.inputs[0]);m.node_tree.links.new(tex.outputs[0],bs.inputs['Base Color'])
    if emission:
        m.node_tree.links.new(tex.outputs[0],bs.inputs['Emission Color']);bs.inputs['Emission Strength'].default_value=emission
    return m


def rename_material(old, new, texdir):
    m=bpy.data.materials.get(old)
    if m is None:
        assert bpy.data.materials.get(new),old
        return bpy.data.materials[new]
    src=texdir/f'{old}_BaseColor.png';dst=texdir/f'{new}_BaseColor.png'
    if src.exists():src.rename(dst)
    assert dst.exists(),dst
    for n in m.node_tree.nodes:
        if n.type=='TEX_IMAGE' and n.image and Path(n.image.filepath).name==src.name:
            n.image.filepath=str(dst);n.image.name=dst.stem
    for o in bpy.context.scene.objects:
        if o.type=='MESH' and o.name==old:o.name=new
    m.name=new
    return m


def clip(poly, axis, boundary, positive):
    out=[]
    for a,b in zip(poly,poly[1:]+poly[:1]):
        da=(a[axis]-boundary)*(1 if positive else -1);db=(b[axis]-boundary)*(1 if positive else -1)
        if da>=-1e-10:out.append(a)
        if (da>1e-10 and db < -1e-10) or (da < -1e-10 and db>1e-10):out.append(a+(b-a)*(da/(da-db)))
    return out


def triangles(o):
    me=o.data;me.calc_loop_triangles();nm=o.matrix_world.to_3x3().inverted().transposed()
    for t in me.loop_triangles:
        rows=[]
        for li in t.loops:
            rows.append(np.array([*(o.matrix_world@me.vertices[me.loops[li].vertex_index].co),
                                  *(nm@me.corner_normals[li].vector).normalized(),
                                  *me.uv_layers['UVMap'].data[li].uv,
                                  *(me.uv_layers['Lightmap'].data[li].uv if 'Lightmap' in me.uv_layers else (0,0))]))
        yield rows,me.polygons[t.polygon_index].use_smooth


def surface_object(name, mat, faces, lightmap=True):
    vs=[];fs=[];ns=[];uv=[];lm=[];smooth=[]
    for poly,s in faces:
        for j in range(1,len(poly)-1):
            tri=[poly[0],poly[j],poly[j+1]]
            if np.linalg.norm(np.cross(tri[1][:3]-tri[0][:3],tri[2][:3]-tri[0][:3]))<1e-12:continue
            fs.append(tuple(range(len(vs),len(vs)+3)));smooth.append(s)
            for p in tri:vs.append(p[:3]);ns.append(p[3:6]);uv.append(p[6:8]);lm.append(p[8:10])
    me=bpy.data.meshes.new(name);me.from_pydata(vs,[],fs);me.update()
    me.materials.append(mat);u=me.uv_layers.new(name='UVMap')
    u.data.foreach_set('uv',np.array(uv).ravel())
    if lightmap:me.uv_layers.new(name='Lightmap').data.foreach_set('uv',np.array(lm).ravel())
    for p,s in zip(me.polygons,smooth):p.use_smooth=s
    me.normals_split_custom_set(ns)
    o=bpy.data.objects.new(name,me);bpy.context.scene.collection.objects.link(o);return o


def consolidate(names, name, texdir, tag=None, emission=0):
    """Consolidate up to four materials, including repeating source UVs."""
    from lib_rooftop_textures import save
    assert len(names)<=4
    rgb=np.zeros((2048,2048,3),np.float32);faces=[];had_lm=False
    for index,old in enumerate(names):
        mat=bpy.data.materials[old];im=bpy.data.images.load(str(texdir/f'{old}_BaseColor.png'),check_existing=False)
        im.scale(1020,1020);pixels=np.empty(1020*1020*4,np.float32);im.pixels.foreach_get(pixels)
        tile=np.pad(pixels.reshape(1020,1020,4)[:,:,:3],((2,2),(2,2),(0,0)),mode='wrap')
        ox=(index%2)*1024;oy=(index//2)*1024;rgb[oy:oy+1024,ox:ox+1024]=tile;bpy.data.images.remove(im)
        for o in list(bpy.context.scene.objects):
            if o.type!='MESH' or o.data.materials[0]!=mat:continue
            had_lm=had_lm or 'Lightmap' in o.data.uv_layers
            for poly,s in triangles(o):
                if tag=='rooftop':
                    # Foliage has only +/-3.5% grain. Keep its accepted topology and
                    # UV2 EXACT: clamp only triangles crossing a source repeat seam.
                    # This changes that tiny grain region, never the leaf silhouette.
                    part=[p.copy() for p in poly]
                    offset=np.floor(np.array(part)[:,6:8].mean(axis=0))
                    for p in part:
                        local=np.clip(p[6:8]-offset,0,1)
                        p[6:8]=local*1020/2048+np.array([ox+2,oy+2])/2048
                    faces.append((part,s));continue
                arr=np.array(poly);lo=np.floor(arr[:,6:8].min(axis=0)+1e-7).astype(int);hi=np.floor(arr[:,6:8].max(axis=0)-1e-7).astype(int);hi=np.maximum(lo,hi)
                for u in range(lo[0],hi[0]+1):
                    for v in range(lo[1],hi[1]+1):
                        part=poly
                        for axis,a in ((6,u),(7,v)):
                            part=clip(part,axis,a,True);part=clip(part,axis,a+1,False)
                        if len(part)<3:continue
                        part=[p.copy() for p in part]
                        for p in part:p[6:8]=(p[6:8]-[u,v])*1020/2048+np.array([ox+2,oy+2])/2048
                        faces.append((part,s))
            bpy.data.objects.remove(o,do_unlink=True)
    im=save(texdir/f'{name}_BaseColor.png',rgb);mat=image_material(name,im,emission,tag)
    result=surface_object(name,mat,faces,had_lm)
    for old in names:
        bpy.data.materials.remove(bpy.data.materials[old]);(texdir/f'{old}_BaseColor.png').unlink(missing_ok=True)
    return result


def split_material(source,new,texdir,predicate):
    """Move whole selected faces to an independent effect; preserves UVs/normals."""
    m=bpy.data.materials[source];copy=m.copy();copy.name=new
    src=texdir/f'{source}_BaseColor.png';dst=texdir/f'{new}_BaseColor.png';dst.write_bytes(src.read_bytes())
    for n in copy.node_tree.nodes:
        if n.type=='TEX_IMAGE' and n.image:
            n.image=bpy.data.images.load(str(dst),check_existing=True)
    selected=[];rest=[]
    objs=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.data.materials[0]==m]
    lm=any('Lightmap' in o.data.uv_layers for o in objs)
    for o in objs:
        for tri,s in triangles(o):(selected if predicate(np.array(tri)[:,:3].mean(axis=0)) else rest).append((tri,s))
        bpy.data.objects.remove(o,do_unlink=True)
    assert selected and rest,(source,'empty material split')
    surface_object(source,m,rest,lm);return surface_object(new,copy,selected,lm)


def home_effects(texdir,triangulate_source=True):
    from lib_home_geometry import Geometry
    from lib_rooftop_textures import save
    # Match the accepted builder's BMesh triangulation before clipping source UVs.
    # Mesh.calc_loop_triangles chooses different diagonals on non-planar bevels.
    import bmesh
    for obj in ([o for o in bpy.context.scene.objects if o.type=='MESH'] if triangulate_source else []):
        bm=bmesh.new();bm.from_mesh(obj.data)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001)
        bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.000001)
        bmesh.ops.triangulate(bm,faces=list(bm.faces))
        bm.to_mesh(obj.data);bm.free();obj.data.update()
    # Preserve original materials in a padded atlas to meet the original 12-slot budget.
    consolidate(['Home_Warm_Plaster','Home_Limestone','Home_Charcoal'],'Home_Mineral_Atlas',texdir,'home')
    candle=split_material('Home_Linen_emit','Home_Candle_emit_flicker15',texdir,
                          lambda p:p[0]<-3.8 and 1.64<p[2]<1.82)
    candle.data.materials[0]['home']=True
    # Original opaque sculpted flame: bent, tapering six-sided tongues, no billboards/alpha.
    n=1024;y,x=np.mgrid[0:n,0:n]/(n-1)
    heat=np.clip(1-y*.80-np.abs(x-.5)*.32,0,1)
    rgb=np.stack((.98+heat*.02,.19+.74*heat,.012+.32*heat**3),axis=2)
    mat=image_material('Home_Flame_Source',save(texdir/'Home_Flame_Source_BaseColor.png',rgb),2.2,'home')
    g=Geometry()
    def flame(cx,cy,z,h,w,lean):
        vs=[];fs=[];uv=[];segments=6
        for j,(height,width,bend) in enumerate([(0,.72,0),(.22,1,-.12),(.54,.67,.25),(.80,.30,.65),(1,.008,1)]):
            for k in range(segments):
                a=math.tau*k/segments;vs.append((cx+math.cos(a)*w*.48*width,cy+math.sin(a)*w*width+lean*bend,z+h*height))
        for j in range(4):
            for k in range(segments):
                fs.append((j*segments+k,j*segments+(k+1)%segments,(j+1)*segments+(k+1)%segments,(j+1)*segments+k))
                uv.append([(k/6,j/4),((k+1)/6,j/4),((k+1)/6,(j+1)/4),(k/6,(j+1)/4)])
        g.add(mat,vs,fs,False,uv)
    for i,(dy,h,w,lean) in enumerate([(-.39,.34,.074,.06),(-.23,.50,.09,-.055),(-.04,.59,.105,.065),(.14,.43,.083,-.05),(.33,.36,.07,.04)]):
        flame(-3.87+(i%2)*.055,1.70+dy,.255,h,w,lean)
    # Small candle flame tips share the independent 15 percent candle effect.
    for cy in (.92,2.48):
        g.lathe(candle.data.materials[0],(-3.90,cy,1.81),[(.008,0),(.014,.025),(.006,.048),(.0005,.065)],8)
    g.objects()
    consolidate(['Home_Embers_glow','Home_Flame_Source'],'Home_Fire_glow_flicker30',texdir,'home',2.2)
    rename_material('Home_Night_unlit','Home_Night_unlit_twinkle12',texdir)
    marker('FireLight_350',(-3.62,1.70,.44))
    marker('FireLight_65',(-3.89,.92,1.84));marker('FireLight_70',(-3.89,2.48,1.84))
    return [o for o in bpy.context.scene.objects if o.type=='MESH']


def rooftop_effects(texdir):
    consolidate(['Rooftop_Olive_Leaves','Rooftop_Lavender'],'Rooftop_Foliage_Atlas',texdir,'rooftop')
    split_material('Rooftop_Festoon_bulb','Rooftop_Candle_bulb_flicker20',texdir,lambda p:p[2]<1.5)
    rename_material('Rooftop_Festoon_bulb','Rooftop_Festoon_bulb_flicker4',texdir)
    rename_material('Rooftop_Panorama_unlit','Rooftop_Panorama_unlit_twinkle10',texdir)
    rename_material('Rooftop_City_Windows_unlit','Rooftop_City_Windows_unlit_twinkle10',texdir)
    # Unique radii avoid Blender .001 suffixes, while retaining the exact marker grammar.
    for r,p in [(140,(-2.15,0,.643)),(145,(2.1,0,.643)),(150,(0,1.36,.523)),(125,(3.8,-1.45,.983))]:marker(f'FireLight_{r}',p)


def space_effects(texdir):
    from lib_rooftop_textures import save
    # Only reassign small patches on EXISTING station ribs. No new silhouette or
    # occluder; accepted geometry, irradiance and both UV sets remain valid.
    m=bpy.data.materials['Space_Porcelain'];rest=[];beacons=[];counts=[0,0,0]
    boxes=[((-8.1,3.88,3.18),(-7.6,4.12,3.26)),((7.6,3.88,3.18),(8.1,4.12,3.26)),((6.75,10.05,4.68),(7.3,10.5,4.76))]
    for o in [o for o in bpy.context.scene.objects if o.type=='MESH' and o.data.materials[0]==m]:
        for tri,s in triangles(o):
            pending=[tri]
            for color,(lo,hi) in enumerate(boxes):
                following=[]
                for poly in pending:
                    inside=poly
                    for axis in range(3):
                        outside=clip(inside,axis,lo[axis],False)
                        if len(outside)>=3:following.append(outside)
                        inside=clip(inside,axis,lo[axis],True)
                        outside=clip(inside,axis,hi[axis],True)
                        if len(outside)>=3:following.append(outside)
                        inside=clip(inside,axis,hi[axis],False)
                    if len(inside)>=3:
                        inside=[p.copy() for p in inside]
                        for p in inside:p[6:8]=((color+.5)/3,.5)
                        beacons.append((inside,s));counts[color]+=1
                pending=following
            rest.extend((p,s) for p in pending)
        bpy.data.objects.remove(o,do_unlink=True)
    assert min(counts)>0,counts
    rgb=np.zeros((64,192,3),np.float32)
    for i,c in enumerate(((.85,.025,.008),(.015,.62,.09),(.72,.79,.86))):rgb[:,i*64:(i+1)*64]=c
    mat=image_material('Space_Navigation_unlit_blink16',save(texdir/'Space_Navigation_unlit_blink16_BaseColor.png',rgb))
    surface_object('Space_Porcelain',m,rest);surface_object(mat.name,mat,beacons)


def load_preview_export(root,room,tag):
    """Preview the delivered effects, rather than a potentially older checkpoint."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(root/'Assets/TheMovies/Art'/room/f'{room}.fbx'))
    scene=bpy.context.scene
    scene.world=bpy.data.worlds.new(room+' preview world');scene.world.use_nodes=True
    for m in bpy.data.materials:m[tag]=True
    return scene
