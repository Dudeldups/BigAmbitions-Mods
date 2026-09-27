"""Run with Blender --background --python ExportBlenderSelections.py -- source.blend production.glb output.json.
The authored object vertex groups are selections; production local coordinates remain authoritative.
"""
import bpy
import json
import sys
from pathlib import Path

blend, production, output = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=blend)
selections = []
for obj in bpy.data.objects:
    if obj.type != 'MESH':
        continue
    for group in obj.vertex_groups:
        indices = {v.index for v in obj.data.vertices
                   if any(g.group == group.index and g.weight > .5 for g in v.groups)}
        if group.name == 'Propeller':
            # The supplied file also marks the disconnected rear header (466 vertices).
            # Only the three blades and hub at the decorative zeppelin rotate.
            indices = {i for i in indices if obj.data.vertices[i].co.z > 50}
        if not indices:
            print('Empty group:', obj.name, group.name)
            continue
        used = {i for face in obj.data.polygons if all(i in indices for i in face.vertices)
                for i in face.vertices}
        selections.append((obj.name, group.name, len(obj.data.vertices), sorted(used)))

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=production)
result = []
for name, group, count, indices in selections:
    obj = bpy.data.objects.get(name)
    if obj is None or len(obj.data.vertices) != count:
        raise RuntimeError('Production topology differs: ' + name)
    # glTFast local Unity coordinates from Blender's glTF-imported local mesh.
    vertices = [{'x': -obj.data.vertices[i].co.x, 'y': obj.data.vertices[i].co.z,
                 'z': -obj.data.vertices[i].co.y} for i in indices]
    result.append({'source': name, 'name': group, 'sourceVertexCount': count, 'vertices': vertices})
Path(output).write_text(json.dumps({'groups': result}, separators=(',', ':')), encoding='utf-8')
print('Exported', len(result), 'selections to', output)
