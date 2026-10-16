"""Read-only, world-space head/cushion audit of imported meshes (all rooms).

A 10 cm radius sphere sweeps center heights 1.10..1.35 m above local floor.
Nearest triangle distance tests every direction, including cushion/frame edges.
Subtract half the 5 mm sampling step: distance to a surface is 1-Lipschitz,
so the reported lower bound covers the continuous interval between samples.
"""
import re
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HEAD_RADIUS = .10
REQUIRED_CLEARANCE = .08
HEIGHT_STEP = .005


def surface_tree(objects):
    vertices, faces, owners = [], [], []
    for obj in objects:
        mesh = obj.data
        mesh.calc_loop_triangles()
        offset = len(vertices)
        vertices.extend(obj.matrix_world @ v.co for v in mesh.vertices)
        faces.extend(tuple(offset + i for i in tri.vertices) for tri in mesh.loop_triangles)
        owners.extend([obj.name] * len(mesh.loop_triangles))
    tree = BVHTree.FromPolygons(vertices, faces, all_triangles=True)
    return tree, owners


def audit(objects, markers):
    tree, owners = surface_tree(objects)
    # Throws and decorative pillows can cover the seat. Trace the actual seat
    # upholstery underneath them; the head audit above still includes ALL meshes.
    support_tree, support_owners = surface_tree([obj for obj in objects if
        any(word in obj.name.lower() for word in ('linen', 'velvet', 'fabric'))])
    rows = []
    for marker in sorted(markers, key=lambda obj: obj.name):
        if not re.fullmatch(r'(Seat[1-4]|Spot_\d{3})', marker.name):
            continue
        floor = marker.matrix_world.translation
        samples = []
        for step in range(51):
            height = 1.10 + step * HEIGHT_STEP
            center = floor + Vector((0, 0, height))
            point, normal, index, distance = tree.find_nearest(center)
            # An unsigned distance alone could accept the middle of a solid.
            outside = (center - point).dot(normal) >= -1e-5
            samples.append({'height_m': height, 'surface_distance_m': distance,
                            'outside_surface': outside, 'mesh': owners[index],
                            'nearest_surface': list(point)})
        worst = min(samples, key=lambda sample: sample['surface_distance_m'])
        lower = worst['surface_distance_m'] - HEAD_RADIUS - HEIGHT_STEP / 2
        # The marker and a 20 cm head footprint must remain ABOVE the seat,
        # rather than escaping the backrest by moving beyond the front edge.
        support = []
        for dx in (-.10, 0, .10):
            for dy in (-.10, 0, .10):
                point, normal, index, distance = support_tree.ray_cast(
                    floor + Vector((dx, dy, .80)), Vector((0, 0, -1)), .55)
                valid = (point is not None and normal.z > .8 and
                         any(word in support_owners[index].lower() for word in ('linen', 'velvet', 'fabric')))
                support.append({'offset_xy': [dx, dy], 'pass': valid,
                                'mesh': support_owners[index] if point is not None else None,
                                'height_m': point.z - floor.z if point is not None else None})
        point, normal, _, _ = tree.ray_cast(floor + Vector((0, 0, .05)), Vector((0, 0, -1)), .10)
        floor_ok = point is not None and normal.z > .8 and abs(point.z - floor.z) < .02
        passed = (lower >= REQUIRED_CLEARANCE and all(s['outside_surface'] for s in samples)
                  and all(s['pass'] for s in support) and floor_ok)
        rows.append({'marker': marker.name, 'floor_position': list(floor), 'pass': passed,
                     'clearance_lower_bound_m': lower, 'worst_sample': worst,
                     'eye_125_clearance_m': samples[30]['surface_distance_m'] - HEAD_RADIUS,
                     'all_centers_outside': all(s['outside_surface'] for s in samples),
                     'floor_pass': floor_ok, 'seat_support': support})
    assert rows, 'No seat markers found'
    return {'pass': all(row['pass'] for row in rows), 'marker_count': len(rows),
            'head_diameter_m': 2 * HEAD_RADIUS, 'required_clearance_m': REQUIRED_CLEARANCE,
            'center_heights_m': [1.10, 1.35], 'height_step_m': HEIGHT_STEP,
            'continuous_interval_allowance_m': HEIGHT_STEP / 2,
            'minimum_clearance_lower_bound_m': min(r['clearance_lower_bound_m'] for r in rows),
            'failed_markers': [r['marker'] for r in rows if not r['pass']], 'markers': rows}
