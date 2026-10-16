"""Head correction only: translate the accepted terrace, never deform/rebake it."""
from mathutils import Vector
from lib_rooftop_room import SEATS

# The old 12 cm shoulder offset is inadequate for a 20 cm head plus 8 cm air.
# Loveseats: +10 cm relative to furniture; other seats: +8 cm.
SCENE_SHIFT_Y = -.10
OTHER_FORWARD = .08


def correct_heads(scene):
    previous = float(scene.get('rooftop_head_scene_shift_y', 0))
    delta = SCENE_SHIFT_Y - previous
    # Includes architecture, all furniture, skyline, panorama and screen markers;
    # also translates any bake lights/cameras if present. No relative changes.
    for obj in scene.objects:
        if obj.parent is None:
            obj.location.y += delta
    for i, position in enumerate(SEATS):
        point = Vector(position)
        point.y = 0 if i < 4 else point.y + SCENE_SHIFT_Y + OTHER_FORWARD
        scene.objects[f'Spot_{i:03d}'].location = point
        if i < 4:
            scene.objects[f'Seat{i+1}'].location = point
    scene['rooftop_head_scene_shift_y'] = SCENE_SHIFT_Y
    scene['rooftop_head_other_forward_m'] = OTHER_FORWARD
