"""Builds the gameplay models (D120, D121): weapons and the log / thatch building pieces, as GLB files.

Run with Blender 4.x / 5.x (no add-ons needed):
    blender --background --factory-startup --python tools/build_gameplay_models.py

Writes Assets/Gameplay/Weapons/*.glb and Assets/Gameplay/Building/*.glb. Placeholder art, made from simple shapes so
the dimensions match Content/gameplay_pieces.json exactly; replace any GLB with a hand-made one of the same name, size
and origin (docs/GAMEPLAY_DESIGN.md §5e lists the contract). Materials are named (Bark, Wood, Thatch, Stone, Fibre,
Metal): the game gives them its own textures by name.

Coordinates below are the GAME's (metres, Y up, a piece faces +Z); vertices are turned into Blender's Z-up axes when
written, and the glTF exporter turns them back.
"""
import math
import os
import random
import sys

import bmesh
import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT_BUILD = os.path.join(ROOT, "Assets", "Gameplay", "Building")
OUT_WEAPONS = os.path.join(ROOT, "Assets", "Gameplay", "Weapons")
COLOURS = {
    "Bark": (0.29, 0.20, 0.13, 1), "Wood": (0.62, 0.47, 0.30, 1), "Thatch": (0.66, 0.55, 0.30, 1),
    "Stone": (0.45, 0.44, 0.42, 1), "Fibre": (0.55, 0.47, 0.32, 1), "Metal": (0.5, 0.5, 0.52, 1), "Wedge": (0.35, 0.36, 0.38, 1),
}
rng = random.Random(7)


def g2b(p):
    """Game (x, y up, z) to Blender (x, -z, y up)."""
    return (p[0], -p[2], p[1])


def material(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = COLOURS[name]
        bsdf.inputs["Roughness"].default_value = 0.9 if name != "Metal" else 0.4
    return m


class Mesh:
    """Collects faces (game coordinates, with UVs) by material, then becomes one Blender object."""

    def __init__(self, name):
        self.name = name
        self.faces = []  # (material, [(pos, uv)...])

    def face(self, mat, verts):
        self.faces.append((mat, verts))

    def quad(self, mat, a, b, c, d, uv=None):
        uv = uv or [(0, 0), (1, 0), (1, 1), (0, 1)]
        self.face(mat, list(zip([a, b, c, d], uv)))

    def build(self, parent=None, location=(0, 0, 0)):
        me = bpy.data.meshes.new(self.name)
        bm = bmesh.new()
        uvl = bm.loops.layers.uv.new("UVMap")
        mats = []
        for mat, verts in self.faces:
            if mat not in mats:
                mats.append(mat)
            vs = [bm.verts.new(g2b((p[0] - location[0], p[1] - location[1], p[2] - location[2]))) for p, _ in verts]
            try:
                f = bm.faces.new(vs)
            except ValueError:
                continue
            f.material_index = mats.index(mat)
            for loop, (_, uv) in zip(f.loops, verts):
                loop[uvl].uv = uv
        # Shared corners welded (UVs stay per corner), faces turned outward per closed part.
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.normal_update()
        bm.to_mesh(me)
        bm.free()
        for mname in mats:
            me.materials.append(material(mname))
        for poly in me.polygons:
            poly.use_smooth = True
        if hasattr(me, "set_sharp_from_angle"):
            me.set_sharp_from_angle(angle=math.radians(40))  # (round logs smooth, cut ends and box edges sharp)
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        ob.location = g2b(location)
        if parent is not None:
            ob.parent = parent
            ob.location = (0, 0, 0)  # (the vertices are already relative to the parent)
        return ob


def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def mul(a, k): return (a[0] * k, a[1] * k, a[2] * k)
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def norm(a):
    l = math.sqrt(a[0] ** 2 + a[1] ** 2 + a[2] ** 2) or 1
    return (a[0] / l, a[1] / l, a[2] / l)


def frame(axis):
    """Two unit vectors across an axis."""
    up = (0, 1, 0) if abs(axis[1]) < 0.9 else (1, 0, 0)
    u = norm(cross(axis, up))
    v = norm(cross(axis, u))
    return u, v


def log(m, a, b, r, segs=10, half=False, flat=(0, 1, 0), bark="Bark", end="Wood", taper=1.0, wobble=0.0):
    """A log from a to b (radius r). half: split lengthwise, the flat face toward `flat` (a split log, plank side up)."""
    axis = norm(sub(b, a))
    length = math.sqrt(sum((b[i] - a[i]) ** 2 for i in range(3)))
    if half:
        # u across the flat face, v the flat face's normal
        v = norm(sub(flat, mul(axis, sum(flat[i] * axis[i] for i in range(3)))))
        u = norm(cross(v, axis))
        angles = [math.pi + math.pi * k / segs for k in range(segs + 1)]  # the round half, away from the flat face
    else:
        u, v = frame(axis)
        angles = [math.tau * k / segs for k in range(segs + 1)]
    ra, rb = r, r * taper
    rings = []
    for (p, rr) in ((a, ra), (b, rb)):
        ring = []
        for t in angles:
            w = 1 + wobble * math.sin(3 * t + p[0] * 7.1 + p[2] * 3.3)
            ring.append(add(p, add(mul(u, math.cos(t) * rr * w), mul(v, math.sin(t) * rr * w))))
        rings.append(ring)
    for k in range(len(angles) - 1):
        u0, u1 = angles[k] * r, angles[k + 1] * r
        m.quad(bark, rings[0][k], rings[0][k + 1], rings[1][k + 1], rings[1][k], [(u0, 0), (u1, 0), (u1, length), (u0, length)])
    if half:
        # the flat face (split wood)
        m.quad(end, rings[0][-1], rings[0][0], rings[1][0], rings[1][-1], [(0, 0), (2 * r, 0), (2 * r, length), (0, length)])
    for ring, p, flip in ((rings[0], a, True), (rings[1], b, False)):
        pts = ring[:-1] if not half else ring
        for k in range(len(pts)):
            q0, q1 = pts[k], pts[(k + 1) % len(pts)]
            if half and k == len(pts) - 1:
                break
            tri = [p, q1, q0] if flip else [p, q0, q1]
            m.face(end, [(x, (0.5 + (x[0] - p[0]) / (2 * r), 0.5 + (x[2] - p[2]) / (2 * r))) for x in tri])


def box(m, centre, size, mat="Wood", yaw=0.0, pitch=0.0):
    """A box (size x, y, z) turned by yaw about Y then pitched about its local X."""
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    cy, sy, cp, sp = math.cos(yaw), math.sin(yaw), math.cos(pitch), math.sin(pitch)

    def t(x, y, z):
        y2, z2 = y * cp - z * sp, y * sp + z * cp
        return (centre[0] + x * cy + z2 * sy, centre[1] + y2, centre[2] - x * sy + z2 * cy)

    c = [t(x, y, z) for x in (-hx, hx) for y in (-hy, hy) for z in (-hz, hz)]
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    for f in faces:
        pts = [c[i] for i in f]
        m.quad(mat, *pts, uv=[(0, 0), (size[0], 0), (size[0], size[1]), (0, size[1])])


def clear():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.objects):
        for x in list(block):
            if x.users == 0:
                block.remove(x)


def export(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True,
                              export_yup=True, export_cameras=False, export_lights=False, export_animations=False)
    print("wrote", os.path.relpath(path, ROOT))


# ---------------------------------------------------------------- building pieces
D = 0.3            # log diameter in walls
R = D / 2
ROWS = 10          # rows of logs in a 3 m wall


def wall_rows(m, height=3.0, opening=None, profile=None, overhang=0.15):
    """Rows of logs along X. opening: (x0, x1, y0, y1) left out; profile(y) -> (x0, x1) for gables."""
    n = max(1, int(round(height / D)))
    d = height / n
    for i in range(n):
        y = d * (i + 0.5)
        x0, x1 = (-1.5 - overhang, 1.5 + overhang) if profile is None else profile(y)
        if x1 - x0 < 0.12:
            continue
        spans = [(x0, x1)]
        if opening and opening[2] < y < opening[3]:
            spans = [(x0, opening[0]), (opening[1], x1)]
        for a, b in spans:
            if b - a > 0.1:
                log(m, (a, y, 0), (b, y, 0), d / 2 * 1.02, wobble=0.03)


def piece_wall(kind):
    m = Mesh("Wall")
    if kind == "wall":
        wall_rows(m)
    elif kind == "half":
        wall_rows(m, 1.5)
    elif kind == "window":
        wall_rows(m, opening=(-0.55, 0.55, 1.2, 2.1))
        box(m, (0, 1.2, 0), (1.25, 0.08, 0.34))        # sill
        box(m, (0, 2.1, 0), (1.25, 0.08, 0.34))        # head
        for x in (-0.58, 0.58):
            box(m, (x, 1.65, 0), (0.07, 0.9, 0.32))    # jambs
    elif kind == "doorway":
        wall_rows(m, opening=(-0.62, 0.62, 0, 2.1))
        box(m, (0, 2.13, 0), (1.45, 0.12, 0.34))       # lintel
        for x in (-0.65, 0.65):
            box(m, (x, 1.05, 0), (0.08, 2.1, 0.32))    # jambs
    elif kind == "gable_steep":
        wall_rows(m, 3.0, profile=lambda y: (y - 1.5, 1.5 + 0.15))
    elif kind == "gable_low":
        wall_rows(m, 1.5, profile=lambda y: (2 * y - 1.5, 1.5 + 0.15))
    elif kind == "peak_steep":
        wall_rows(m, 1.5, profile=lambda y: (-(1.5 - y), 1.5 - y))
    elif kind == "peak_low":
        wall_rows(m, 0.75, profile=lambda y: (-(1.5 - 2 * y), 1.5 - 2 * y))
    m.build()


def piece_foundation():
    m = Mesh("Foundation")
    for i in range(10):  # the deck: split logs, flat side up, along X
        z = -1.35 + i * 0.3
        log(m, (-1.5, 0, z), (1.5, 0, z), 0.15, half=True, flat=(0, 1, 0), wobble=0.02)  # (the flat face on the axis: top at 0)
    for x in (-1.32, 0, 1.32):  # beams under it along Z
        log(m, (x, -0.42, -1.5), (x, -0.42, 1.5), 0.15)
    for x in (-1.32, 1.32):     # posts down into the ground
        for z in (-1.32, 1.32):
            log(m, (x, -0.3, z), (x, -2.6, z), 0.15, taper=0.9)
    m.build()


def piece_floor(half=False):
    m = Mesh("Floor")
    n = 5 if half else 10
    for i in range(n):
        z = -0.6 + i * 0.3 if half else -1.35 + i * 0.3
        log(m, (-1.5, 0, z), (1.5, 0, z), 0.15, half=True, flat=(0, 1, 0), wobble=0.02)
    m.build()


def piece_door():
    leaf = bpy.data.objects.new("Leaf", None)
    bpy.context.scene.collection.objects.link(leaf)
    leaf.location = g2b((-0.55, 0, 0))
    m = Mesh("DoorLeaf")
    for i in range(5):  # vertical split logs, flat side out
        x = -0.55 + 0.11 + i * 0.22
        log(m, (x, 0.02, 0), (x, 2.05, 0), 0.11, half=True, flat=(0, 0, 1), bark="Wood", end="Wood")
    for y in (0.35, 1.7):
        box(m, (0, y, -0.07), (1.05, 0.12, 0.05), "Bark")
    box(m, (0, 1.03, -0.07), (0.07, 1.4, 0.05), "Bark", pitch=0.0, yaw=0)
    box(m, (0.38, 1.05, 0.06), (0.05, 0.18, 0.05), "Fibre")  # handle
    m.build(parent=leaf, location=(-0.55, 0, 0))


def piece_stairs(rise, run, steps):
    m = Mesh("Stairs")
    for x in (-0.66, 0.66):
        log(m, (x, -0.05, -run / 2), (x, rise - 0.05, run / 2), 0.11)
    for i in range(steps):
        y = rise * (i + 1) / steps - 0.06
        z = -run / 2 + run * (i + 0.5) / steps
        log(m, (-0.75, y + 0.06, z), (0.75, y + 0.06, z), 0.13, half=True, flat=(0, 1, 0))
    m.build()
    # The walking surface (the game makes it the collision and hides it).
    col = Mesh("Col_Ramp")
    length = math.hypot(rise, run)
    pitch = -math.atan2(rise, run)
    # a slab whose top runs from the bottom step's front to the top edge
    box(col, (0, rise / 2 - 0.1 * math.cos(pitch), 0.1 * math.sin(pitch) * -1), (1.5, 0.2, length), "Wood", pitch=pitch)
    col.build()


# Roof faces: a face rises along its direction from the eave (s = 0) at t metres per metre; c runs across it, between
# lo(s) and hi(s). to_xz(s, c) gives the piece's x, z.
DIRS = {
    "+z": lambda s, c, o: (c, s - o), "-z": lambda s, c, o: (c, o - s),
    "+x": lambda s, c, o: (s - o, c), "-x": lambda s, c, o: (o - s, c),
}


def roof_faces(shape):
    full = lambda s: (-1.5, 1.5)
    if shape == "slope":
        return [("+z", 3.0, full)]
    if shape == "ridge":
        return [("+z", 1.5, full), ("-z", 1.5, full)]
    if shape == "outer":
        return [("+z", 3.0, lambda s: (s - 1.5, 1.5)), ("+x", 3.0, lambda s: (s - 1.5, 1.5))]
    if shape == "inner":
        return [("+z", 3.0, lambda s: (-1.5, s - 1.5)), ("+x", 3.0, lambda s: (-1.5, s - 1.5))]
    if shape == "peak":
        tri = lambda s: (-1.5 + s, 1.5 - s)
        return [("+z", 1.5, tri), ("-z", 1.5, tri), ("+x", 1.5, tri), ("-x", 1.5, tri)]


def roof_logs(m, shape, t):
    for d, smax, lim in roof_faces(shape):
        to = DIRS[d]
        steps = 60
        c = -1.5 + 0.15
        while c < 1.5:
            inside = [k * smax / steps for k in range(steps + 1) if lim(k * smax / steps)[0] - 0.02 <= c <= lim(k * smax / steps)[1] + 0.02]
            if len(inside) > 1:
                s0, s1 = inside[0], inside[-1]
                x0, z0 = to(s0, c, 1.5)
                x1, z1 = to(s1, c, 1.5)
                r = 0.14
                # (on the slope: the log's centre one radius above the face, along the face's normal)
                # (the flat face, on the log's axis, lies on the roof's plane; the round side up)
                log(m, (x0, t * s0 + 0.01, z0), (x1, t * s1 + 0.01, z1), r, half=True,
                    flat=(0, -1, 0), wobble=0.02, bark="Bark", end="Wood")
            c += 0.28
    if shape in ("ridge", "peak"):
        top = t * 1.5 + 0.2
        if shape == "ridge":
            log(m, (-1.55, top, 0), (1.55, top, 0), 0.13)
        else:
            box(m, (0, top, 0), (0.3, 0.25, 0.3), "Wood")


def roof_thatch(m, shape, t):
    thick = 0.26
    ln = math.sqrt(1 + t * t)
    for d, smax, lim in roof_faces(shape):
        to = DIRS[d]
        course = 0.5
        n = max(1, int(math.ceil(smax / course - 1e-6)))
        for i in range(n):
            s0 = smax - (i + 1) * course  # from the top down: lower courses lie over the upper ones' edges
            s0 = max(0.0, s0)
            s1 = min(smax, s0 + course + 0.12)
            a0, b0 = lim(s0)
            a1, b1 = lim(s1)
            jitter = rng.uniform(-0.02, 0.03)
            lo_y0 = t * s0 + 0.02 + jitter     # (the bottom edge of a course stands out a little)
            lo_y1 = t * s1 + 0.02
            th0, th1 = thick * 1.15 + jitter, thick * 0.65
            p = lambda s, c, y: (lambda xz: (xz[0], y, xz[1]))(to(s, c, 1.5))
            # 8 corners: bottom (b) and top (u) at the course's low (0) and high (1) ends, from lo to hi across
            b00, b01, b10, b11 = p(s0, a0, lo_y0), p(s0, b0, lo_y0), p(s1, a1, lo_y1), p(s1, b1, lo_y1)
            u00, u01, u10, u11 = p(s0, a0, lo_y0 + th0), p(s0, b0, lo_y0 + th0), p(s1, a1, lo_y1 + th1), p(s1, b1, lo_y1 + th1)
            w = max(b0 - a0, b1 - a1, 0.01)
            for f in ((u00, u01, u11, u10), (b00, b10, b11, b01), (b00, b01, u01, u00), (b10, u10, u11, b11), (b00, u00, u10, b10), (b01, b11, u11, u01)):
                if d in ("-z", "+x"):
                    f = tuple(reversed(f))
                m.quad("Thatch", *f, uv=[(0, 0), (w, 0), (w, course), (0, course)])
    if shape in ("ridge", "peak"):
        top = t * 1.5 + 0.22
        if shape == "ridge":
            log(m, (-1.55, top, 0), (1.55, top, 0), 0.16, bark="Thatch", end="Thatch")
        else:
            box(m, (0, top, 0), (0.45, 0.3, 0.45), "Thatch")


def piece_roof(material_name, shape, pitch):
    t = 1.0 if pitch == "steep" else 0.5
    m = Mesh("Roof")
    (roof_logs if material_name == "log" else roof_thatch)(m, shape, t)
    m.build()


# ---------------------------------------------------------------- weapons (grip at the origin, pointing +Z)
def weapon_spear():
    m = Mesh("Spear")
    log(m, (0, 0, -1.0), (0, 0, 1.55), 0.022, segs=7, bark="Wood", end="Wood", taper=0.85)
    # the stone point and its lashing
    tip = Mesh("Point")
    base, point = (0, 0, 1.5), (0, 0, 1.82)
    for k in range(4):
        a0, a1 = k * math.pi / 2, (k + 1) * math.pi / 2
        p0 = (math.cos(a0) * 0.045, math.sin(a0) * 0.012, 1.52)
        p1 = (math.cos(a1) * 0.045, math.sin(a1) * 0.012, 1.52)
        tip.face("Stone", [(point, (0.5, 1)), (p0, (0, 0)), (p1, (1, 0))])
    m.faces += tip.faces
    log(m, (0, 0, 1.42), (0, 0, 1.54), 0.03, segs=7, bark="Fibre", end="Fibre")
    m.build()


def weapon_sword():
    m = Mesh("Sword")
    log(m, (0, 0, -0.12), (0, 0, 0.1), 0.022, segs=7, bark="Fibre", end="Wood")       # grip, wrapped
    box(m, (0, 0, 0.12), (0.16, 0.04, 0.04), "Wood")                                  # guard
    box(m, (0, 0, 0.5), (0.075, 0.025, 0.76), "Wood")                                  # the hardwood blade
    for side in (-1, 1):
        for i in range(7):                                                              # stone flakes on both edges
            box(m, (side * 0.045, 0, 0.2 + i * 0.1), (0.03, 0.012, 0.075), "Stone", yaw=side * 0.2)
    m.build()


def weapon_knife():
    m = Mesh("Knife")
    log(m, (0, 0, -0.1), (0, 0, 0.04), 0.017, segs=6, bark="Wood", end="Wood")
    tip = (0, 0, 0.24)
    for side in (-1, 1):
        m.face("Stone", [((0, 0.006 * side, 0.04), (0, 0)), ((0.025, 0, 0.05), (1, 0)), (tip, (0.5, 1))])
        m.face("Stone", [((0, 0.006 * side, 0.04), (0, 0)), (tip, (0.5, 1)), ((-0.02, 0, 0.05), (1, 0))])
    log(m, (0, 0, 0.02), (0, 0, 0.06), 0.02, segs=6, bark="Fibre", end="Fibre")
    m.build()


def weapon_bow():
    """Limbs along ±Z (vertical when the bow arm points forward), the string toward +Y (the archer)."""
    m = Mesh("Bow")
    n = 10
    pts = []
    for k in range(-n, n + 1):
        s = k / n
        pts.append((0, -0.12 * (1 - s * s) + 0.12, s * 0.72))  # bent away from the archer
    for i in range(len(pts) - 1):
        r = 0.02 * (1 - 0.5 * abs(i - n) / n)
        log(m, pts[i], pts[i + 1], r, segs=6, bark="Wood", end="Wood")
    log(m, (0, 0.12, -0.08), (0, 0.12, 0.08), 0.026, segs=6, bark="Fibre", end="Fibre")   # grip wrap
    m.build()
    s = Mesh("String")
    log(s, pts[0], pts[-1], 0.004, segs=4, bark="Fibre", end="Fibre")
    s.build()


def weapon_arrow():
    """Centred, the tip toward +Z, 0.8 m long."""
    m = Mesh("Arrow")
    log(m, (0, 0, -0.4), (0, 0, 0.36), 0.006, segs=5, bark="Wood", end="Wood")
    tip = (0, 0, 0.44)
    for k in range(3):
        a0, a1 = k * math.tau / 3, (k + 1) * math.tau / 3
        m.face("Stone", [(tip, (0.5, 1)), ((math.cos(a0) * 0.014, math.sin(a0) * 0.014, 0.35), (0, 0)), ((math.cos(a1) * 0.014, math.sin(a1) * 0.014, 0.35), (1, 0))])
    for k in range(3):  # fletching
        a = k * math.tau / 3
        dx, dy = math.cos(a) * 0.03, math.sin(a) * 0.03
        m.face("Fibre", [((0, 0, -0.38), (0, 0)), ((dx, dy, -0.36), (1, 0)), ((dx, dy, -0.24), (1, 1)), ((0, 0, -0.22), (0, 1))])
        m.face("Fibre", [((0, 0, -0.22), (0, 1)), ((dx, dy, -0.24), (1, 1)), ((dx, dy, -0.36), (1, 0)), ((0, 0, -0.38), (0, 0))])
    m.build()


def main():
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    jobs = []
    for kind in ("wall", "window", "doorway", "half", "gable_steep", "gable_low", "peak_steep", "peak_low"):
        name = {"wall": "log_wall", "window": "log_wall_window", "doorway": "log_wall_doorway", "half": "log_wall_half"}.get(kind, "log_" + kind)
        jobs.append((name, OUT_BUILD, lambda k=kind: piece_wall(k)))
    jobs += [("log_foundation", OUT_BUILD, piece_foundation), ("log_floor", OUT_BUILD, piece_floor),
             ("log_floor_half", OUT_BUILD, lambda: piece_floor(True)), ("log_door", OUT_BUILD, piece_door),
             ("log_stairs", OUT_BUILD, lambda: piece_stairs(3.0, 4.0, 12)), ("log_steps", OUT_BUILD, lambda: piece_stairs(1.5, 2.0, 6))]
    for mat in ("log", "thatch"):
        for shape in ("slope", "ridge", "outer", "inner", "peak"):
            for pitch in ("steep", "low"):
                jobs.append((f"{mat}_roof_{shape}_{pitch}", OUT_BUILD, lambda a=mat, b=shape, c=pitch: piece_roof(a, b, c)))
    jobs += [("spear", OUT_WEAPONS, weapon_spear), ("stone_sword", OUT_WEAPONS, weapon_sword), ("stone_knife", OUT_WEAPONS, weapon_knife),
             ("bow", OUT_WEAPONS, weapon_bow), ("arrow", OUT_WEAPONS, weapon_arrow)]
    for name, out, make in jobs:
        if only and name not in only:
            continue
        clear()
        make()
        export(os.path.join(out, name + ".glb"))


main()
