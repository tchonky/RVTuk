"""final.py: the recommended corner-height rule for Topo Tools Update ("rule N": nearest line, the
next other height in view, same-height gaps bridged), and the full re-run of the battery and of
every attack file's cases, sweeps and scans with it, next to rule A, A-cone, the TIN, the rays,
IDW and the six attackers' fixes.

    python -I final.py [section ...]
        screenshot  the three circled corners of images/2.png under every approach
        battery     the battery (close-lines' re-encoding, with the brief's hand-worked A values)
        cases       every point case of every attack file (each judged by its own file's judge)
        sweeps      every sweep of every attack file: largest 1 cm step and true jumps (bisected)
        grids       close-lines' corner squares (5 mm continuity grid, 1-2 cm reading-range grid)
        random      line-ends' even slopes, curves' random mounds, crossings' stress, site-scale's
                    synthetic site, consistency's Monte Carlo cut-out plans
        jumps2d     2D jump scans of consistency's scenes (A and N)
        order       drawing order / direction independence (all case points, close-lines grids)
        timing      2000 outline points x 200 lines; the bridging cost
        ablation    each part of rule N switched off in turn, on every case point
        vectors     every case point with its lines, bridges and rule N's value -> final_vectors.json
    (no section: everything; about 15 minutes in CPython, the TIN and the rays are the slow part)

Nothing outside this file is edited: harness.py is executed up to its unfinished class Tin, and the
attack files are imported read-only (importlib; their __main__ blocks do not run).

=============================================================================================
THE RULE (metres; every number is part of the specification)
=============================================================================================
  0. Bridges, once per Update, from the drawn lines only.
     A free end is an end of an open line (first and last points more than 1 mm apart) that is
     more than 1 mm from every other line of the same height. Its direction t is the unit vector
     along its end segment, pointing out of the line. Two free ends E, F of the same height
     (within 0.5 mm) face each other when they are more than 1 mm apart, F lies within 60 degrees
     of t_E seen from E, and E within 60 degrees of t_F seen from F. Each free end picks the
     nearest free end facing it (equal distances: the smaller x of the other end, then the smaller
     y). When E and F pick each other, the segment E-F shortened by 1 mm at both ends is tested
     against every drawn line whose height differs by more than 0.5 mm; if it touches none, E-F
     becomes a bridge, an extra line at that height (the higher of the two). Bridges take part in
     steps 1-5 exactly like drawn lines; no points are placed on them.
  1. For every line i (drawn and bridges): d_i = plan distance from V to the polyline, P_i the
     nearest point (equally near points, within 1e-9 m: the smaller x, then the smaller y).
  2. If some d_i <= 1 mm, V takes the highest such line's height.
  3. L1 = the line with the smallest d_i (within 1e-9 m: the higher; same height: the smaller
     P_i.x, then P_i.y); u = (V - P1) / d1. The far side is the open half-plane
     {P : (P - V) . u > 1e-9 m} (the 1e-9 keeps a line lying exactly along its edge outside).
  4. For every line j with a part on the far side (each segment clipped to it; parts of zero
     length ignored), Q_j = the nearest point of that part to V, e_j = |Q_j - V|; far_min = the
     smallest e_j of all lines. Candidates: lines whose height differs from h1 by more than 0.5 mm.
     t_same = the smallest t > 0 at which the walk V + t u meets a segment of a line of h1's height
     (within 0.5 mm), a vertex of such a line within 1 mm of the walk counting at its projection t
     (infinite if none). Take the candidates nearest first (e within 1e-9 m: the higher, then the
     first in the list). A candidate with e_j <= far_min + 1e-9 m is L2 at once (nothing can hide
     the nearest thing on the far side). Otherwise stop at a candidate with e_j > t_same (no L2),
     and skip one when the straight segment from V to 1 mm short of Q_j touches any other line
     (drawn or bridge, any height; touching counts). The first candidate not skipped is L2.
  5.   L2 found:                          h = h1 + (h2 - h1) * d1 / (d1 + e2)
       no L2, some line on the far side:  h = h1
       no line on the far side:           V keeps its height, unless d1 <= 5 cm: then h = h1.
  Left alone: exactly the points with no line (drawn or bridge) on the far side, more than 5 cm
  from their nearest line. The "at once" shortcut in step 4 never changes a value (checked against
  the full test on every case point and 2000 random points); it only saves the walk and the sight
  test unless the nearest thing beyond the corner has h1's height.
  Full run: about 15 minutes in CPython; outputs final_out_<sections>.txt / .json.
"""
import importlib.util
import json
import math
import os
import random
import sys
import time
import types
from collections import OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

# =============================================================================================
# PART 1: the rule (self-contained; this is the reference for the C# port)
# =============================================================================================

SAME_SPOT = 0.001        # 1 mm: the on-line snap, and the bridges' and sight lines' tolerances
SAME_Z = 0.0005          # two heights within 0.5 mm are the same height
NEAR = 0.05              # nothing beyond, but within 5 cm of the nearest line: take its height
TIE = 1e-9               # distances within this are equal
BRIDGE_DEG = 60.0        # two free ends face each other within this angle


def _dist(a, b):
    return math.hypot(a[0] - b[0], a[1] - b[1])


def _seg_nearest(v, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    ll = dx * dx + dy * dy
    t = 0.0 if ll <= 0 else max(0.0, min(1.0, ((v[0] - a[0]) * dx + (v[1] - a[1]) * dy) / ll))
    p = (a[0] + t * dx, a[1] + t * dy)
    return math.hypot(v[0] - p[0], v[1] - p[1]), p


def nearest_on_polyline(v, poly):
    """(distance, nearest point); equally near points (within TIE): the one with the smaller x,
    then the smaller y, so that the drawing direction does not matter."""
    best_d, best_p = math.inf, None
    for i in range(1, len(poly)):
        d, p = _seg_nearest(v, poly[i - 1], poly[i])
        if d < best_d - TIE or (d <= best_d + TIE and best_p is not None and (p[0], p[1]) < (best_p[0], best_p[1])):
            best_d, best_p = min(d, best_d), p
        elif best_p is None:
            best_d, best_p = d, p
    if best_p is None and poly:
        best_d, best_p = _dist(v, poly[0]), poly[0]
    return best_d, best_p


FAR_EPS = 1e-9           # the far side is (P - V).u > 1e-9 m: a line lying exactly along the
                         # boundary (V on a line's extension) is outside whatever the round-off


def far_part_nearest(v, poly, u):
    """(distance, point) of the nearest point to v of the polyline's part inside the open
    half-plane {P : (P - v).u > FAR_EPS}; None when no part of positive length lies inside. A part
    that starts on the boundary counts from that boundary point (as harness.dist_to_part)."""
    best = None
    vx, vy = v
    ux, uy = u
    for i in range(1, len(poly)):
        ax, ay = poly[i - 1]
        bx, by = poly[i]
        fa = ux * (ax - vx) + uy * (ay - vy) - FAR_EPS
        fb = ux * (bx - vx) + uy * (by - vy) - FAR_EPS
        lo, hi = 0.0, 1.0
        if fa > 0 and fb > 0:
            pass
        elif fa <= 0 and fb <= 0:
            continue
        else:
            tc = fa / (fa - fb)
            if fa <= 0:
                lo = tc
            else:
                hi = tc
        if hi - lo <= 1e-12:
            continue
        p = (ax + (bx - ax) * lo, ay + (by - ay) * lo)
        q = (ax + (bx - ax) * hi, ay + (by - ay) * hi)
        d, pt = _seg_nearest(v, p, q)
        if best is None or d < best[0]:
            best = (d, pt)
    return best


def _orient(a, b, c):
    return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])


def segments_touch(p, q, a, b):
    """Closed segments pq and ab share a point (touching counts)."""
    d1, d2 = _orient(a, b, p), _orient(a, b, q)
    d3, d4 = _orient(p, q, a), _orient(p, q, b)
    if ((d1 > 0 > d2) or (d1 < 0 < d2)) and ((d3 > 0 > d4) or (d3 < 0 < d4)):
        return True

    def within(s, t, r):
        return (min(s[0], t[0]) - 1e-12 <= r[0] <= max(s[0], t[0]) + 1e-12 and
                min(s[1], t[1]) - 1e-12 <= r[1] <= max(s[1], t[1]) + 1e-12)
    return ((d1 == 0 and within(a, b, p)) or (d2 == 0 and within(a, b, q)) or
            (d3 == 0 and within(p, q, a)) or (d4 == 0 and within(p, q, b)))


def ray_hit(v, u, a, b, tol=SAME_SPOT):
    """Smallest t > 0 at which v + t u meets segment ab, an end of ab within tol of the ray counting
    at its projection; None when there is none."""
    best = None
    ex, ey = b[0] - a[0], b[1] - a[1]
    den = u[0] * ey - u[1] * ex
    apx, apy = a[0] - v[0], a[1] - v[1]
    if abs(den) > 1e-12:
        t = (apx * ey - apy * ex) / den
        s = (apx * u[1] - apy * u[0]) / den
        if t > 0 and -1e-12 <= s <= 1 + 1e-12:
            best = t
    for px, py in ((a[0] - v[0], a[1] - v[1]), (b[0] - v[0], b[1] - v[1])):
        t = px * u[0] + py * u[1]
        if t > 0 and abs(px * u[1] - py * u[0]) <= tol:
            best = t if best is None else min(best, t)
    return best


# ---- step 0: bridges -------------------------------------------------------------------------

def _clean(poly):
    out = []
    for p in poly:
        if not out or _dist(out[-1], p) > 1e-12:
            out.append((float(p[0]), float(p[1])))
    return out


def free_ends(lines):
    """[(line index, end point, outward unit direction, z)] of every free end."""
    ends = []
    for i, ln in enumerate(lines):
        p = _clean(ln["poly"])
        if len(p) < 2 or _dist(p[0], p[-1]) <= SAME_SPOT:
            continue
        for E, Q in ((p[0], p[1]), (p[-1], p[-2])):
            L = _dist(E, Q)
            ends.append((i, E, ((E[0] - Q[0]) / L, (E[1] - Q[1]) / L), ln["z"]))
    free = []
    for i, E, t, z in ends:
        if not any(k != i and abs(ln["z"] - z) <= SAME_Z and nearest_on_polyline(E, ln["poly"])[0] <= SAME_SPOT
                   for k, ln in enumerate(lines)):
            free.append((i, E, t, z))
    return free


def _hermite(E, ta, F, tb, d, nseg=16):
    """Optional smooth bridge (not part of the recommended rule): cubic Hermite leaving E along ta
    and arriving at F along -tb, tangent lengths d."""
    pts = []
    for k in range(nseg + 1):
        s = k / nseg
        h00, h10, h01, h11 = 2 * s ** 3 - 3 * s ** 2 + 1, s ** 3 - 2 * s ** 2 + s, -2 * s ** 3 + 3 * s ** 2, s ** 3 - s ** 2
        pts.append((h00 * E[0] + h10 * d * ta[0] + h01 * F[0] - h11 * d * tb[0],
                    h00 * E[1] + h10 * d * ta[1] + h01 * F[1] - h11 * d * tb[1]))
    return pts


def make_bridges(lines, cone_deg=BRIDGE_DEG, curved=False):
    """Step 0: the bridges ({"z", "poly", "name", "bridge": True})."""
    ends = free_ends(lines)
    cmin = math.cos(math.radians(cone_deg))
    pick = {}
    for a, (i, E, ta, za) in enumerate(ends):
        best = None
        for b, (j, F, tb, zb) in enumerate(ends):
            if b == a or abs(za - zb) > SAME_Z:
                continue
            dx, dy = F[0] - E[0], F[1] - E[1]
            g = math.hypot(dx, dy)
            if g <= SAME_SPOT:
                continue
            if dx * ta[0] + dy * ta[1] < cmin * g or -dx * tb[0] - dy * tb[1] < cmin * g:
                continue
            key = (g, F[0], F[1])
            if best is None or key < best[0]:
                best = (key, b)
        if best is not None:
            pick[a] = best[1]
    out = []
    for a, b in sorted(pick.items()):
        if a >= b or pick.get(b) != a:
            continue
        (_, E, ta, za), (_, F, tb, zb) = ends[a], ends[b]
        z = max(za, zb)
        pts = _hermite(E, ta, F, tb, _dist(E, F)) if curved else [E, F]
        test = list(pts)
        n0 = _dist(test[0], test[1])
        test[0] = (test[0][0] + (test[1][0] - test[0][0]) * SAME_SPOT / n0, test[0][1] + (test[1][1] - test[0][1]) * SAME_SPOT / n0)
        n1 = _dist(test[-1], test[-2])
        test[-1] = (test[-1][0] + (test[-2][0] - test[-1][0]) * SAME_SPOT / n1, test[-1][1] + (test[-2][1] - test[-1][1]) * SAME_SPOT / n1)
        blocked = any(abs(ln["z"] - z) > SAME_Z and
                      any(segments_touch(test[n - 1], test[n], ln["poly"][m - 1], ln["poly"][m])
                          for m in range(1, len(ln["poly"])) for n in range(1, len(test)))
                      for ln in lines)
        if not blocked:
            out.append({"z": z, "poly": pts, "name": f"bridge {z:.2f}", "bridge": True})
    return out


class Prepared:
    """Once per Update: the drawn lines plus their bridges."""

    def __init__(self, lines, bridges=True, cone_deg=BRIDGE_DEG, curved=False):
        self.drawn = list(lines)
        self.bridges = make_bridges(self.drawn, cone_deg, curved) if bridges else []
        self.all = self.drawn + self.bridges


def prepare(lines, **kw):
    return Prepared(lines, **kw)


# ---- steps 1-5 ---------------------------------------------------------------------------------

def _pick(cands):
    """The smallest c[0]; within TIE the higher c[1], then the first."""
    best = None
    for c in cands:
        if best is None or c[0] < best[0] - TIE or (abs(c[0] - best[0]) <= TIE and c[1] > best[1]):
            best = c
    return best


def _in_view(v, q, lines, skip, z_only=None):
    """The straight segment from v to 1 mm short of q touches no line but `skip` (z_only: only
    lines of that height count, for the ablation)."""
    L = _dist(v, q)
    if L <= 2 * SAME_SPOT:
        return True
    qs = (q[0] + (v[0] - q[0]) * SAME_SPOT / L, q[1] + (v[1] - q[1]) * SAME_SPOT / L)
    for k, ln in enumerate(lines):
        if k == skip or (z_only is not None and abs(ln["z"] - z_only) > SAME_Z):
            continue
        p = ln["poly"]
        for m in range(1, len(p)):
            if segments_touch(v, qs, p[m - 1], p[m]):
                return False
    return True


def corner_height(v, prepared, detail=False, walk=True, sight="all", near=NEAR, shortcut=True):
    """Steps 1-5. Ablations: walk=False drops the walk straight on; sight="same" lets only lines of
    h1's height block a sight line; near=0 drops the 5 cm exception; shortcut=False runs the sight
    test and the walk for every candidate (same values, for the check)."""
    L = prepared.all
    info = {}

    def done(h, why):
        info["why"] = why
        return (h, info) if detail else h

    if not L:
        return done(None, "no lines")
    nearest = [nearest_on_polyline(v, ln["poly"]) for ln in L]
    on = [ln["z"] for ln, (d, _) in zip(L, nearest) if d <= SAME_SPOT]
    if on:
        return done(max(on), "on a line")
    best = None                      # L1: smallest d; within TIE the higher, then the smaller P.x, P.y
    for i, (ln, (d, p)) in enumerate(zip(L, nearest)):
        if best is None or d < best[0] - TIE:
            best = (d, ln["z"], i, p)
        elif abs(d - best[0]) <= TIE and (ln["z"] > best[1] + SAME_Z or
                                          (abs(ln["z"] - best[1]) <= SAME_Z and (p[0], p[1]) < (best[3][0], best[3][1]))):
            best = (d, max(ln["z"], best[1]) if abs(ln["z"] - best[1]) <= SAME_Z else ln["z"], i, p)
    d1, z1, i1, p1 = best
    z1 = L[i1]["z"]
    u = ((v[0] - p1[0]) / d1, (v[1] - p1[1]) / d1)
    info.update(L1=i1, d1=d1, P1=p1, u=u, z1=z1)
    far_min = math.inf
    cands = []
    for j, ln in enumerate(L):
        r = far_part_nearest(v, ln["poly"], u)
        if r is None:
            continue
        far_min = min(far_min, r[0])
        if abs(ln["z"] - z1) > SAME_Z:
            cands.append((r[0], ln["z"], j, r[1]))
    if far_min == math.inf:
        if d1 <= near:
            return done(z1, "nothing beyond, within 5 cm of L1")
        return done(None, "nothing beyond")
    pool = list(cands)
    t_same = None
    skipped = []
    while pool:
        c = _pick(pool)
        e, z, j, q = c
        if not (shortcut and e <= far_min + TIE):
            if t_same is None:
                t_same = math.inf
                if walk:
                    for ln in L:
                        if abs(ln["z"] - z1) > SAME_Z:
                            continue
                        p = ln["poly"]
                        for m in range(1, len(p)):
                            t = ray_hit(v, u, p[m - 1], p[m])
                            if t is not None and t < t_same:
                                t_same = t
            if e > t_same:
                info.update(cut_by_walk=j, t_same=t_same, skipped=skipped)
                break
            if not _in_view(v, q, L, j, z1 if sight == "same" else None):
                skipped.append(j)
                pool.remove(c)
                continue
        info.update(L2=j, e2=e, Q2=q, skipped=skipped)
        return done(z1 + (z - z1) * d1 / (d1 + e), "between L1 and L2")
    info.setdefault("skipped", skipped)
    return done(z1, "only L1's height in view beyond")


_PREP_CACHE = {}


def _prepared_for(lines, **kw):
    key = (id(lines), tuple(sorted(kw.items())))
    hit = _PREP_CACHE.get(key)
    if hit is not None and hit[0] is lines:
        return hit[1]
    if len(_PREP_CACHE) > 4000:
        _PREP_CACHE.clear()
    pr = Prepared(lines, **kw)
    _PREP_CACHE[key] = (lines, pr)
    return pr


def rule_n(v, lines, detail=False):
    """The recommended rule, harness signature: rule(v, lines) -> metres or None."""
    return corner_height(v, _prepared_for(lines), detail=detail)


def variant(bridges=True, cone_deg=BRIDGE_DEG, curved=False, walk=True, sight="all", near=NEAR, shortcut=True):
    def f(v, lines, detail=False):
        return corner_height(v, _prepared_for(lines, bridges=bridges, cone_deg=cone_deg, curved=curved),
                             detail=detail, walk=walk, sight=sight, near=near, shortcut=shortcut)
    return f


# =============================================================================================
# PART 2: the re-run (read-only use of harness.py and the attack files)
# =============================================================================================

def _load_harness():
    path = os.path.join(HERE, "harness.py")
    src = open(path, encoding="utf-8").read()
    ns = {"__name__": "harness", "__file__": path}
    exec(compile(src[:src.index("\nclass Tin")], path, "exec"), ns)
    return types.SimpleNamespace(**ns)


def _load(modname, filename):
    spec = importlib.util.spec_from_file_location(modname, os.path.join(HERE, filename))
    m = importlib.util.module_from_spec(spec)
    sys.modules[modname] = m
    spec.loader.exec_module(m)
    return m


H = _load_harness()
CL = _load("attack_close_lines", "attack_close-lines.py")
LE = _load("attack_line_ends", "attack_line-ends.py")
CU = _load("attack_curves", "attack_curves.py")
CR = _load("attack_crossings", "attack_crossings.py")
SS = _load("attack_site_scale", "attack_site-scale.py")
COC = _load("attack_consistency_core", "attack_consistency_core.py")
CO = _load("attack_consistency", "attack_consistency.py")

_LEN_CACHE = {}


def _total_len(lines):
    hit = _LEN_CACHE.get(id(lines))
    if hit is not None and hit[0] is lines:
        return hit[1]
    t = sum(_dist(ln["poly"][i - 1], ln["poly"][i]) for ln in lines for i in range(1, len(ln["poly"])))
    _LEN_CACHE[id(lines)] = (lines, t)
    return t


def tin_ref(v, lines):
    """The harness's TIN (Bowyer-Watson, exact predicates; site-scale's completion of the class):
    lines resampled every 0.05 m, or every 0.25 m when that would exceed 8000 points."""
    sp = 0.05 if _total_len(lines) / 0.05 <= 8000 else 0.25
    return SS.tin(v, lines, sp)


def rays_ref(v, lines):
    return SS.rays(v, lines)


REF = OrderedDict([("A", H.rule_a), ("A-cone", H.rule_a_cone), ("TIN", tin_ref), ("rays", rays_ref), ("IDW", H.idw)])
FIXES = OrderedDict([("A-fix", CL.rule_a_fix), ("A+bridges", LE.rule_a_bridged), ("A-flat45", CU.flat(45.0)),
                     ("A-SB", CR.rule_sb), ("A-lat", SS.rule_a_lat), ("Afix", COC.rule_afix)])
N = OrderedDict([("N", rule_n)])
ABLATIONS = OrderedDict([
    ("N-noWalk", variant(walk=False)),
    ("N-noBridge", variant(bridges=False)),
    ("N-noNear", variant(near=0.0)),
    ("N-sightSame", variant(sight="same")),
    ("N-bridge90", variant(cone_deg=89.999)),
    ("N-hermite", variant(curved=True)),
    ("N-noShortcut", variant(shortcut=False)),
])
SLOW = {"TIN", "rays"}
LEFT = "LEFT"

OUT_LINES = []


def out(s=""):
    print(s, flush=True)
    OUT_LINES.append(s)


def fmt(h):
    if h is None:
        return "left"
    if isinstance(h, float) and h != h:
        return "n/c"
    return f"{h:.3f}"


def head(t):
    out()
    out("=" * 110)
    out(t)
    out("=" * 110)


# ---------------------------------------------------------------------------------------------
# Every point case of every attack file, as uniform records with that file's own judge
# ---------------------------------------------------------------------------------------------

class Rec:
    def __init__(self, src, case, label, lines, xy, expect_txt, judge):
        self.src, self.case, self.label, self.lines, self.xy = src, case, label, lines, (float(xy[0]), float(xy[1]))
        self.expect_txt, self.judge = expect_txt, judge


def _exp_txt(e):
    if e is None:
        return "none obvious"
    if e == LEFT or e == "LEFT":
        return "left"
    if isinstance(e, tuple) and len(e) == 2 and all(isinstance(x, (int, float)) for x in e):
        return f"{e[0]:.3f}..{e[1]:.3f}"
    if isinstance(e, (int, float)):
        return f"{e:.3f}"
    return str(e)


def recs_close_lines():
    R = []
    for fam, cases in (("battery", CL.battery()), ("close", CL.close_cases())):
        for c in cases:
            for p in c.points:
                def j(h, e=p.expect):
                    return {"ok": True, "x": False, "?": None}[CL.judge(h, e)]
                R.append(Rec("close-lines" + ("/battery" if fam == "battery" else ""), c.key, p.label, c.lines, p.xy,
                             _exp_txt(p.expect), j))
    return R


def _le_plane_rec(case, label, lines, v, hp, tol=0.01):
    return Rec("line-ends", case, label, lines, v, f"{hp:.3f} (plane)", lambda h, hp=hp, tol=tol: h is not None and abs(h - hp) <= tol)


def recs_line_ends():
    R = []
    for key, label, L, pt, exp in LE.battery_cases():
        def j(h, e=exp):
            r = LE.judge(h, e)
            return None if r == "-" else r == "ok"
        R.append(Rec("line-ends/battery", key, label, L, pt, _exp_txt(exp), j))
    pl, L, bldg = LE.corner_scene(2.0)
    for v in bldg:
        R.append(_le_plane_rec("FAIL1-corner", f"building corner {v}", L, v, pl.h(v)))
    pl = LE.Plane(90.0, 0.20, 41.0, (0.0, 1.0))
    site = LE.rect_loop(-15, -10, 27, 22)
    Lc = [(0.0, 0.0), (12.0, 0.0), (12.0, 4.0), (4.0, 4.0), (4.0, 10.0), (0.0, 10.0)]
    L2 = LE.drawn(pl, [41.0 + 0.5 * k for k in range(-4, 9)], site, [Lc])
    for v in Lc:
        R.append(_le_plane_rec("FAIL2-Lshape", f"L-building corner {v}", L2, v, pl.h(v)))
    pl3, L3, hole = LE.round_scene(0.15)
    for v in hole:
        R.append(_le_plane_rec("FAIL3a-round", f"round cut-out vertex ({v[0]:.2f},{v[1]:.2f})", L3, v, pl3.h(v), tol=0.03))
    gap = [LE.line(45, LE.seg((-20, 10), (20, 10))), LE.line(43, LE.seg((-20, 0), (-1, 0))), LE.line(43, LE.seg((1, 0), (20, 0))),
           LE.line(41, LE.seg((-20, -10), (20, -10)))]
    for t in (1.0, 2.0, 2.6, 2.7):
        R.append(_le_plane_rec("FAIL3b-gap", f"(0,-{t}) under the gap", gap, (0.0, -t), 43 - 0.2 * t))
    top = [LE.line(41, LE.seg((-30, 0), (30, 0))), LE.line(42, LE.seg((-30, 5), (30, 5))), LE.line(43, LE.seg((-30, 10), (0, 10)))]
    for v in ((1.0, 11.0), (2.0, 11.0), (1.0, 12.0), (3.0, 12.0), (5.0, 15.0)):
        R.append(Rec("line-ends", "FAIL4-topend", f"{v} past the top line's end", top, v, "left or >= 43.00",
                     lambda h: h is None or h >= 43.0 - 1e-9))
    for v in ((0.10, 10.0), (0.10, 10.001), (0.10, 9.95)):
        R.append(Rec("line-ends", "FAIL4-topend", f"{v} next to the end", top, v, "42.95..43.00",
                     lambda h: h is not None and 42.95 - 1e-9 <= h <= 43.0 + 1e-9))
    w2 = [LE.line(41.00, LE.seg((-30.0, 0.0), (30.0, 0.0))), LE.line(41.30, LE.seg((-30.0, 1.5), (30.0, 1.5))),
          LE.line(43.00, LE.seg((-30.0, 8.0), (0.0, 8.0)))]
    for v in ((0.10, 8.001), (0.10, 7.999), (0.05, 7.95)):
        R.append(Rec("line-ends", "passes-unsnapped", f"{v}", w2, v, "42.95..43.00",
                     lambda h: h is not None and 42.95 - 1e-9 <= h <= 43.0 + 1e-9))
    ramp = [LE.line(41, LE.seg((-20, 0), (-1, 0))), LE.line(43, LE.seg((1, 0), (20, 0)))]
    R.append(Rec("line-ends", "passes-ramp", "(0,-1)", ramp, (0.0, -1.0), "42.00", lambda h: h is not None and abs(h - 42) <= 0.01))
    tee = [LE.line(41, LE.seg((-20, 0), (20, 0))), LE.line(42, LE.seg((0, 0), (0, 20)))]
    R.append(Rec("line-ends", "passes-tee", "(1,1)", tee, (1.0, 1.0), "41.50", lambda h: h is not None and abs(h - 41.5) <= 0.01))
    site_lines = [LE.line(41 + k, LE.seg((0.0, 5.0 * k), (40.0, 5.0 * k + 40.0 * math.tan(math.radians(30))))) for k in range(4)]
    R.append(Rec("line-ends", "passes-site-outline", "NE corner (40,50) beyond 44", site_lines, (40.0, 50.0), "left",
                 lambda h: h is None))
    chain = [LE.line(43, LE.seg((-20, 0), (-0.025, 0))), LE.line(43, LE.seg((0.025, 0), (20, 0))), LE.line(41, LE.seg((-20, -10), (20, -10)))]
    for v in ((0.0, -0.5), (0.0, -2.0)):
        R.append(_le_plane_rec("passes-chain-5cm", f"{v}", chain, v, 43 - 0.2 * -v[1]))
    topgap = [LE.line(43, LE.seg((-20, 0), (-1, 0))), LE.line(43, LE.seg((1, 0), (20, 0))), LE.line(41, LE.seg((-20, -10), (20, -10)))]
    R.append(Rec("line-ends", "passes-topgap", "(0,0.5) above the top gap", topgap, (0.0, 0.5), "left (beyond the top line)",
                 lambda h: h is None))
    R.append(_le_plane_rec("passes-topgap", "(0,-0.5) below the top gap", topgap, (0.0, -0.5), 42.9))
    shot = LE.screenshot_lines()
    R.append(Rec("line-ends", "passes-shot-edge", "midway (620.5,427.5) px", shot, LE.px((620.5, 427.5)), "about 42.0",
                 lambda h: h is not None and 41.9 <= h <= 42.1))
    return R


def recs_curves():
    R = []
    for fam, cases in (("battery", CU.battery()), ("curves", CU.attacks())):
        for c in cases:
            for p in c.points:
                def j(h, e=p.expect, tol=p.tol):
                    r = CU.verdict(e, h, tol)
                    return None if r == "-" else r == "ok"
                R.append(Rec("curves" + ("/battery" if fam == "battery" else ""), c.key, p.label, c.lines, p.xy,
                             CU.fmt_exp(p.expect, p.tol), j))
    return R


def recs_crossings():
    R = []
    for fam, cases in (("battery", CR.battery()), ("crossings", CR.attack())):
        for c in cases:
            for p in c["points"]:
                def j(h, e=p["expect"]):
                    r = CR.judge(h, e)
                    return None if r == "" else r == "ok"
                R.append(Rec("crossings" + ("/battery" if fam == "battery" else ""), c["key"], p["label"], c["lines"],
                             p["xy"], CR.fmt_exp(p["expect"]), j))
    return R


def recs_site_scale():
    R = []
    for fam, cases in (("battery", SS.battery()), ("site", SS.site_cases())):
        for name, L, pts in cases:
            for p in pts:
                def j(h, e=p.expect):
                    return SS.judge(e, h)
                R.append(Rec("site-scale" + ("/battery" if fam == "battery" else ""), name, p.label, L, p.xy,
                             SS.fmt_expect(p.expect), j))
    return R


def recs_consistency():
    R = []
    for fam, scenes in (("battery", COC.battery()), ("consistency", CO.attack_scenes())):
        for sc in scenes:
            for lab, v, exp, why in sc.points:
                def j(h, e=exp):
                    if e is None:
                        return None
                    if e == "LEFT":
                        return h is None
                    if h is None:
                        return False
                    if isinstance(e, tuple):
                        return e[0] - 1e-9 <= h <= e[1] + 1e-9
                    return abs(h - e) <= 0.01
                R.append(Rec("consistency" + ("/battery" if fam == "battery" else ""), sc.key, lab, sc.lines, v,
                             _exp_txt(exp), j))
    return R


def all_recs():
    return (recs_close_lines() + recs_line_ends() + recs_curves() + recs_crossings() + recs_site_scale()
            + recs_consistency())


# ---------------------------------------------------------------------------------------------
# Sweeps: walk a path in 1 cm steps; largest step; every step over 5 cm bisected to 0.1 mm to
# tell a true jump (the difference stays) from a steep drawn slope (it shrinks)
# ---------------------------------------------------------------------------------------------

def walk(path, step):
    pts = [tuple(map(float, path[0]))]
    for i in range(1, len(path)):
        a, b = path[i - 1], path[i]
        n = max(1, int(round(_dist(a, b) / step)))
        for k in range(1, n + 1):
            pts.append((a[0] + (b[0] - a[0]) * k / n, a[1] + (b[1] - a[1]) * k / n))
    return pts


def sweep(rule, lines, path, step=0.01, thr=0.05):
    pts = walk(path, step)
    vals = [rule(p, lines) for p in pts]
    res = {"max_step": 0.0, "step_at": None, "jump": 0.0, "jump_at": None, "switches": 0}
    for k in range(1, len(pts)):
        a, b = vals[k - 1], vals[k]
        if (a is None) != (b is None):
            res["switches"] += 1
            continue
        if a is None:
            continue
        dlt = abs(b - a)
        if dlt > res["max_step"]:
            res["max_step"], res["step_at"] = dlt, (pts[k - 1], pts[k], a, b)
        if dlt > thr:
            p, q, ha, hb = pts[k - 1], pts[k], a, b
            for _ in range(40):
                if _dist(p, q) < 1e-4:
                    break
                m = ((p[0] + q[0]) / 2, (p[1] + q[1]) / 2)
                hm = rule(m, lines)
                if hm is None:
                    break
                if abs(hm - ha) >= abs(hb - hm):
                    q, hb = m, hm
                else:
                    p, ha = m, hm
            if ha is not None and hb is not None and _dist(p, q) < 1e-3 and abs(hb - ha) > thr / 2:
                if abs(hb - ha) > res["jump"]:
                    res["jump"], res["jump_at"] = abs(hb - ha), (q, ha, hb)
    return res


def all_sweeps():
    """(src, case, label, lines, path)"""
    S = []
    for c in CL.battery() + CL.close_cases():
        for s in c.sweeps:
            S.append(("close-lines", c.key, s.label, c.lines, s.path))
    for c in CU.battery() + CU.attacks():
        for s in c.sweeps:
            S.append(("curves", c.key, s.label, c.lines, [s.a, s.b]))
    for c in CR.battery() + CR.attack():
        for s in c["sweeps"]:
            S.append(("crossings", c["key"], s["label"], c["lines"], s["path"]))
    for key, lines, path, note in SS.battery_sweeps() + SS.site_sweeps():
        S.append(("site-scale", key, note or key, lines, path))
    for sc in COC.battery() + CO.attack_scenes():
        for lab, path in sc.paths:
            S.append(("consistency", sc.key, lab, sc.lines, path))
    gap = [LE.line(45, LE.seg((-20, 10), (20, 10))), LE.line(43, LE.seg((-20, 0), (-1, 0))), LE.line(43, LE.seg((1, 0), (20, 0))),
           LE.line(41, LE.seg((-20, -10), (20, -10)))]
    S.append(("line-ends", "FAIL3b-gap", "down the gap's middle (0,-0.02)->(0,-5)", gap, [(0.0, -0.02), (0.0, -5.0)]))
    shot = LE.screenshot_lines()
    S.append(("line-ends", "FAIL5-shot", "left white area's edge, corner 1 down (354.5,201.5)->(354.5,837) px", shot,
              [LE.px((354.5, 201.5)), LE.px((354.5, 837.0))]))
    S.append(("line-ends", "passes-shot", "right white area's west edge (620.5,547.5)->(620.5,335.5) px", shot,
              [LE.px((620.5, 547.5)), LE.px((620.5, 335.5))]))
    ramp = [LE.line(41, LE.seg((-20, 0), (-1, 0))), LE.line(43, LE.seg((1, 0), (20, 0)))]
    S.append(("line-ends", "passes-ramp", "(-3,-1)->(3,-1)", ramp, [(-3.0, -1.0), (3.0, -1.0)]))
    tee = [LE.line(41, LE.seg((-20, 0), (20, 0))), LE.line(42, LE.seg((0, 0), (0, 20)))]
    S.append(("line-ends", "passes-tee", "(-3,1)->(3,1)", tee, [(-3.0, 1.0), (3.0, 1.0)]))
    top = [LE.line(41, LE.seg((-30, 0), (30, 0))), LE.line(42, LE.seg((-30, 5), (30, 5))), LE.line(43, LE.seg((-30, 10), (0, 10)))]
    S.append(("line-ends", "FAIL4-topend", "(0.5,10.5)->(6,16) past the top line's end", top, [(0.5, 10.5), (6.0, 16.0)]))
    return S


# ---------------------------------------------------------------------------------------------
# Sections
# ---------------------------------------------------------------------------------------------

def sec_screenshot():
    head("SCREENSHOT images/2.png: the three circled corners (0.02 m per pixel, y flipped)")
    px = LE.px
    variants_ = [("the three labelled lines (as in the battery)", LE.screenshot_lines()),
                 ("plus the upper-right line, read as 41.00", LE.screenshot_lines()
                  + [H.line(41.00, [px((840.2, 13.8)), px((900.2, 103.9))])]),
                 ("41.30 and 41.00 extended 10 m past the image top", CR.screenshot_lines()[0])]
    corners = [("corner 1 (354.5,201.5) px", px((354.5, 201.5)), "41.15..41.25"),
               ("corner 2 (620.5,335.5) px", px((620.5, 335.5)), "41.3..41.7"),
               ("corner 3 (620.5,547.5) px", px((620.5, 547.5)), "42.80..43.00")]
    rules = OrderedDict(list(REF.items()) + list(FIXES.items()) + list(N.items()))
    res = {}
    for vname, L in variants_:
        out(f"-- {vname}")
        out(f"   {'corner':<28}{'expected':<14}" + "".join(f"{k:>10}" for k in rules))
        for cname, v, exp in corners:
            vals = [rules[k](v, L) for k in rules]
            out(f"   {cname:<28}{exp:<14}" + "".join(f"{fmt(x):>10}" for x in vals))
            res.setdefault(vname, {})[cname] = {k: x for k, x in zip(rules, vals)}
        for cname, v, exp in corners:
            h, inf = rule_n(v, L, detail=True)
            l1 = L[inf["L1"]] if inf.get("L1") is not None and inf["L1"] < len(L) else None
            l2 = L[inf["L2"]] if inf.get("L2") is not None and inf["L2"] < len(L) else None
            out(f"   N at {cname}: L1 {l1['z'] if l1 else 'bridge'} d1 {inf.get('d1', float('nan')):.3f} m; "
                f"L2 {l2['z'] if l2 else '-'} e2 {inf.get('e2', float('nan')):.3f} m; {inf['why']}")
    return res


def sec_battery():
    head("BATTERY (close-lines' re-encoding of the brief's table; 'hand A' = the brief's hand-worked rule A value)")
    rules = OrderedDict(list(REF.items()) + list(FIXES.items()) + list(N.items()))
    misses = {k: [] for k in rules}
    n_exp = 0
    hand_mism = []
    rows = []
    for c in CL.battery():
        rev = H.reversed_lines(c.lines)
        for p in c.points:
            vals = OrderedDict((k, f(p.xy, c.lines)) for k, f in rules.items())
            j = {k: CL.judge(x, p.expect) for k, x in vals.items()}
            if p.expect is not None:
                n_exp += 1
            for k in rules:
                if j[k] == "x":
                    misses[k].append(f"{c.key} {p.label}")
            if p.handA is not None and (vals["A"] is None or abs(vals["A"] - p.handA) > 0.006):
                hand_mism.append((c.key, p.label, p.handA, vals["A"]))
            nr = rule_n(p.xy, rev)
            same = (nr is None and vals["N"] is None) or (nr is not None and vals["N"] is not None and abs(nr - vals["N"]) < 1e-9)
            rows.append((c.key, p.label, _exp_txt(p.expect), vals, j, same, p.handA))
    out(f"   {'case':<11}{'point':<30}{'expected':<16}" + "".join(f"{k:>10}" for k in rules) + "  rev(N)")
    for key, label, et, vals, j, same, hand in rows:
        out(f"   {key:<11}{label[:29]:<30}{et[:15]:<16}" + "".join(f"{fmt(vals[k]) + ('x' if j[k] == 'x' else ' '):>10}" for k in rules)
            + ("  same" if same else "  DIFF"))
    out(f"   points {len(rows)}, with an expected value {n_exp}")
    for k in rules:
        out(f"   misses {k:<10} {len(misses[k]):3d}  " + "; ".join(misses[k]))
    out(f"   rule A against the brief's hand-worked values: {'all match' if not hand_mism else hand_mism}")
    return {"rows": [(r[0], r[1], r[2], {k: v for k, v in r[3].items()}) for r in rows], "misses": misses}


def sec_cases():
    head("EVERY POINT CASE OF EVERY ATTACK FILE (each judged by its own file's judge; x = miss, - = no expected value)")
    recs = all_recs()
    rules = OrderedDict(list(REF.items()) + list(FIXES.items()) + list(N.items()))
    by_src = OrderedDict()
    for r in recs:
        by_src.setdefault(r.src, []).append(r)
    summary = OrderedDict()
    n_misses_detail = []
    t0 = time.time()
    for src, rs in by_src.items():
        out(f"-- {src}: {len(rs)} points")
        out(f"   {'case':<22}{'point':<36}{'expected':<20}" + "".join(f"{k:>9}" for k in ("A", "A-cone", "TIN", "rays", "N")))
        cnt = {k: [0, 0] for k in rules}
        for r in rs:
            vals = OrderedDict()
            for k, f in rules.items():
                try:
                    vals[k] = f(r.xy, r.lines)
                except Exception as ex:  # a reference rule failing must not stop the run
                    vals[k] = float("nan")
            js = {}
            for k, x in vals.items():
                try:
                    js[k] = r.judge(None if (isinstance(x, float) and x != x) else x)
                except Exception:
                    js[k] = None
                if js[k] is not None:
                    cnt[k][0] += 1
                    cnt[k][1] += js[k] is False
            if js["N"] is False:
                n_misses_detail.append((src, r.case, r.label, r.expect_txt, vals))
            out(f"   {r.case[:21]:<22}{r.label[:35]:<36}{r.expect_txt[:19]:<20}" +
                "".join(f"{fmt(vals[k]) + ('x' if js[k] is False else ' '):>9}" for k in ("A", "A-cone", "TIN", "rays", "N")))
        summary[src] = cnt
    out()
    out(f"   MISSES per rule (points with an expected value / misses), {time.time() - t0:.0f} s")
    out(f"   {'source':<24}" + "".join(f"{k:>11}" for k in rules))
    tot = {k: [0, 0] for k in rules}
    for src, cnt in summary.items():
        out(f"   {src:<24}" + "".join(f"{cnt[k][1]:>5}/{cnt[k][0]:<5}" for k in rules))
        for k in rules:
            tot[k][0] += cnt[k][0]
            tot[k][1] += cnt[k][1]
    out(f"   {'TOTAL':<24}" + "".join(f"{tot[k][1]:>5}/{tot[k][0]:<5}" for k in rules))
    out()
    out("   rule N's misses, with A / TIN / rays at the same point:")
    for src, case, label, et, vals in n_misses_detail:
        out(f"     {src:<20}{case[:22]:<23}{label[:40]:<41} expected {et:<18} N {fmt(vals['N'])}  A {fmt(vals['A'])}"
            f"  TIN {fmt(vals['TIN'])}  rays {fmt(vals['rays'])}")
    return {"summary": {s: {k: v for k, v in c.items()} for s, c in summary.items()}, "total": tot,
            "n_misses": [(a, b, c, d, {k: v for k, v in e.items()}) for a, b, c, d, e in n_misses_detail]}


def sec_sweeps():
    head("SWEEPS (every attack file's): largest change between 1 cm steps, and the largest TRUE jump (a step over"
         " 5 cm bisected to 0.1 mm keeps over 2.5 cm). TIN and rays at 5 cm steps, step only")
    S = all_sweeps()
    fast = OrderedDict([("A", H.rule_a), ("A-cone", H.rule_a_cone), ("N", rule_n)])
    res = []
    out(f"   {'source':<12}{'case':<18}{'sweep':<44}" + "".join(f"{k + ' step/jump':>22}" for k in fast)
        + f"{'TIN step':>10}{'rays step':>10}")
    for src, case, label, lines, path in S:
        cells, row = [], {}
        for k, f in fast.items():
            r = sweep(f, lines, path)
            row[k] = r
            cells.append(f"{r['max_step']:.3f}/{r['jump']:.3f}" + ("s" if r["switches"] else " "))
        for k in ("TIN", "rays"):
            r = sweep(REF[k], lines, path, step=0.05, thr=10.0)
            row[k] = r
        res.append((src, case, label, row))
        out(f"   {src:<12}{case[:17]:<18}{label[:43]:<44}" + "".join(f"{c:>22}" for c in cells)
            + f"{row['TIN']['max_step']:>10.3f}{row['rays']['max_step']:>10.3f}")
    out("   (s: the sweep crosses a moved/left-alone boundary)")
    worse = [(src, case, label, row["N"]["jump"], row["A"]["jump"]) for src, case, label, row in res
             if row["N"]["jump"] > row["A"]["jump"] + 0.005]
    better = [(src, case, label, row["N"]["jump"], row["A"]["jump"]) for src, case, label, row in res
              if row["N"]["jump"] < row["A"]["jump"] - 0.005]
    out(f"   sweeps {len(res)}: N's largest true jump smaller than A's in {len(better)}, larger in {len(worse)}")
    for src, case, label, nj, aj in worse:
        out(f"     N worse: {src} {case} {label}: N {nj:.3f} vs A {aj:.3f}")
    remaining = sorted([(row["N"]["jump"], src, case, label) for src, case, label, row in res if row["N"]["jump"] > 0.05], reverse=True)
    out(f"   N's true jumps over 5 cm remaining: {len(remaining)}")
    for j, src, case, label in remaining:
        out(f"     {j:.3f}  {src} {case} {label}")
    return [(src, case, label, {k: {"max_step": v["max_step"], "jump": v["jump"]} for k, v in row.items()})
            for src, case, label, row in res]


def sec_grids():
    head("GRIDS: close-lines' corner squares")
    byk = {c.key: c for c in CL.close_cases()}
    rules = OrderedDict([("A", H.rule_a), ("A-cone", H.rule_a_cone), ("N", rule_n)])
    out("   5 mm continuity grid: largest change between neighbours (natural = steepest drawn slope x 5 mm)")
    for key, box, natural in CL.GRID_BOXES:
        cells = []
        for k, f in rules.items():
            big, where = CL.grid_scan(f, byk[key].lines, box)
            cells.append(f"{k} {big:.3f}")
        out(f"   {key:<8} {str(box):<28} natural {'-' if natural is None else f'{natural:.3f}'}   " + "   ".join(cells))
    out("   corner squares on a 1-2 cm grid: worst distance outside the hip/distance range, and the share of points"
        " more than 2 cm outside it")
    G = _load("attack_close_lines_grid_defs", "attack_close-lines.py")  # fresh copy, for the reference functions below

    def convex(xc, yc, w, lo, hi):
        def f(x, y):
            a, b = xc - x, yc - y
            if not (0 < a < w and 0 < b < w):
                return None
            m = min(a, b)
            return (lo + (hi - lo) * m / w, lo + (hi - lo) * m / (m + math.hypot(w - a, w - b)))
        return f

    def concave(w, lo, hi):
        def f(x, y):
            if not (0 < x < w and 0 < y < w):
                return None
            dt, dl = min(w - x, w - y), math.hypot(x, y)
            return (lo + (hi - lo) * max(x, y) / w, hi - (hi - lo) * dt / (dt + dl))
        return f

    def cone(cx, cy, w, lo, hi):
        def f(x, y):
            r = math.hypot(x - cx, y - cy)
            if not (0 < x - cx and 0 < y - cy and r < w):
                return None
            return (hi - (hi - lo) * r / w, hi - (hi - lo) * r / w)
        return f
    checks = [("CL1 planter wall corner (0.25 m)", "CL1", (1.75, 1.25, 2.0, 1.5), convex(2.0, 1.5, 0.25, 41.0, 42.0)),
              ("CL2 bank corner, mitered (1.0 m)", "CL2", (10.0, 6.0, 11.0, 7.0), convex(11.0, 7.0, 1.0, 41.0, 42.0)),
              ("CL3 bank corner, rounded toe", "CL3", (10.0, 6.0, 11.0, 7.0), cone(10.0, 6.0, 1.0, 41.0, 42.0)),
              ("CL12 sunken court bank corner (1.0 m)", "CL12", (0.0, 0.0, 1.0, 1.0), concave(1.0, 41.0, 42.0)),
              ("CL15-05 wall cut-out, lines 5 cm off", "CL15-05", (-0.30, -0.30, 0.05, 0.05), convex(0.05, 0.05, 0.35, 41.0, 42.0)),
              ("CL15-10 wall cut-out, lines 10 cm off", "CL15-10", (-0.35, -0.35, 0.10, 0.10), convex(0.10, 0.10, 0.45, 41.0, 42.0))]
    res = {}
    for title, key, (x0, y0, x1, y1), ref in checks:
        lines = byk[key].lines
        step = 0.01 if (x1 - x0) < 0.6 else 0.02
        pts = []
        nx, ny = int(round((x1 - x0) / step)), int(round((y1 - y0) / step))
        for i in range(1, nx):
            for j in range(1, ny):
                x, y = x0 + i * step, y0 + j * step
                r = ref(x, y)
                if r is not None:
                    pts.append(((x, y), min(r) - 0.005, max(r) + 0.005))
        cells = []
        for k, f in rules.items():
            worst, bad = 0.0, 0
            for v, lo, hi in pts:
                h = f(v, lines)
                dev = 99.0 if h is None else max(lo - h, h - hi, 0.0)
                worst = max(worst, dev)
                bad += dev > 0.02
            cells.append(f"{k} {worst:.3f} ({100 * bad / len(pts):.0f}%)")
            res[(title, k)] = (worst, bad / len(pts))
        out(f"   {title:<40} {len(pts):5d} pts   " + "   ".join(cells))
    return {f"{a}|{b}": v for (a, b), v in res.items()}


def sec_random():
    head("RANDOM / STRESS TESTS from the attack files, rule N next to A (and the attackers' own fix where it applies)")
    res = {}
    # line-ends: even slopes drawn stopping at one cut-out
    out("-- line-ends: even slopes (5-30 %, contours every 0.25/0.5/1 m) stopping at one cut-out: corner error vs the plane")
    rules = OrderedDict([("A", H.rule_a), ("A+bridges", LE.rule_a_bridged), ("N", rule_n)])
    for kind in ("rect", "L", "circle"):
        rng = random.Random(7)
        errs = {k: [] for k in rules}
        for it in range(300):
            pl, L, hole, dh, heights = LE.plane_scene(rng, kind)
            for v in hole:
                hp = pl.h(v)
                if not (heights[0] < hp < heights[-1]):
                    continue
                for k, f in rules.items():
                    x = f(v, L)
                    errs[k].append(9.99 if x is None else abs(x - hp) / dh)
        for k in rules:
            e = sorted(errs[k])
            out(f"   {kind:<6} {k:<10} corners {len(e):5d}: > 0.10 dh {sum(x > 0.10 for x in e):4d}, > 0.25 dh "
                f"{sum(x > 0.25 for x in e):4d}, max {e[-1]:.3f} dh")
            res[f"slopes {kind} {k}"] = (len(e), sum(x > 0.10 for x in e), sum(x > 0.25 for x in e), e[-1])
    out("   continuity: a corner slid along every cut-out edge in 2 cm steps (any true jump is wrong on a plane)")
    for kind in ("rect", "L"):
        rng = random.Random(11)
        jumps = {k: [] for k in rules}
        for it in range(60):
            pl, L, hole, dh, heights = LE.plane_scene(rng, kind)
            for i in range(len(hole)):
                for k, f in rules.items():
                    r = sweep(f, L, [hole[i - 1], hole[i]], step=0.02, thr=0.05 * dh)
                    jumps[k].append(r["jump"] / dh)
        for k in rules:
            j = sorted(jumps[k])
            out(f"   {kind:<6} {k:<10} edges {len(j)}: edges with a true jump > 0.05 dh {sum(x > 0.05 for x in j):3d}, max {j[-1]:.3f} dh")
            res[f"edges {kind} {k}"] = (len(j), sum(x > 0.05 for x in j), j[-1])
    # line-ends: curved contours (cone hill, bowl, saddle) stopping at one cut-out
    out("-- line-ends: curved contours (cone hill, bowl, saddle) stopping at one cut-out; error vs the surface in contour intervals")
    rng = random.Random(2)
    tally = {k: [] for k in ("A", "N", "N-hermite")}
    fns = {"A": H.rule_a, "N": rule_n, "N-hermite": ABLATIONS["N-hermite"]}
    for it in range(150):
        kind = rng.choice(["cone", "bowl", "saddle"])
        hfun, cfun = LE._surface(kind)
        site = LE.rect_loop(-25, -25, 25, 25)
        cx, cy = rng.uniform(-12, 12), rng.uniform(-12, 12)
        if rng.random() < 0.6:
            w, hh, deg = rng.uniform(4, 12), rng.uniform(4, 10), rng.uniform(0, 90)
            hole = [H.rotate_point((cx + x, cy + y), deg, (cx, cy)) for x, y in ((-w / 2, -hh / 2), (w / 2, -hh / 2), (w / 2, hh / 2), (-w / 2, hh / 2))]
        else:
            hole = LE.circle_loop(cx, cy, rng.uniform(1.5, 5), 10)
        dh = rng.choice([0.5, 1.0])
        L = []
        for z in [36 + dh * k for k in range(int(12 / dh) + 1)]:
            for poly in cfun(z):
                for run in LE.ck.clip(poly, [site, hole]):
                    L.append(H.line(z, run))
        for v in hole:
            hp = hfun(v)
            a = H.rule_a(v, L)
            if a is None:
                continue
            for k, f in fns.items():
                x = f(v, L)
                tally[k].append(9.99 if x is None else abs(x - hp) / dh)
    for k, e in tally.items():
        out(f"   {k:<10} corners {len(e)}: > 0.10 dh {sum(x > 0.1 for x in e)}, > 0.25 dh {sum(x > 0.25 for x in e)}, "
            f"max {max(e):.3f} dh, mean {sum(e) / len(e):.3f} dh")
        res[f"curved {k}"] = (len(e), sum(x > 0.1 for x in e), sum(x > 0.25 for x in e), max(e))
    # curves: random mounds and pits
    out("-- curves: random mounds and pits (inner ring off-centre up to 60 %, 1/5/10 deg chords, rings in 1/2/4 pieces)")
    CUR = _load("attack_curves_random", "attack_curves_random.py")
    for seed in (7, 11):
        rng = random.Random(seed)
        rules2 = {"A": H.rule_a, "A-flat45": CU.flat(45.0), "N": rule_n}
        stats = {k: {"inside": [0, 0], "between": [0, 0, 0], "outside": [0, 0]} for k in rules2}
        worst = {k: 0.0 for k in rules2}
        for _ in range(500):
            lines, inner, outer, meta = CUR.config(rng)
            for _k in range(12):
                R_ = meta[3]
                r = rng.uniform(0, R_ * 1.15)
                a = rng.uniform(0, 2 * math.pi)
                v = (r * math.cos(a), r * math.sin(a))
                d_in, d_out = CU.dist_to_lines(v, inner), CU.dist_to_lines(v, outer)
                if min(d_in, d_out) <= 0.002:
                    continue
                if CUR.inside_poly(v, inner):
                    kind, exp = "inside", meta[4]
                elif CUR.inside_poly(v, outer):
                    kind, exp = "between", meta[5] + (meta[4] - meta[5]) * d_out / (d_out + d_in)
                else:
                    kind, exp = "outside", None
                for k, f in rules2.items():
                    h = f(v, lines)
                    st = stats[k][kind]
                    st[0] += 1
                    if kind == "outside":
                        st[1] += h is not None
                    elif kind == "inside":
                        st[1] += h is None or abs(h - exp) > 1e-6
                    else:
                        err = 9.9 if h is None else abs(h - exp)
                        st[1] += err > 0.2
                        st[2] += err > 0.05
                        worst[k] = max(worst[k], err)
        for k in rules2:
            s = stats[k]
            out(f"   seed {seed} {k:<9} inside wrong {s['inside'][1]}/{s['inside'][0]}; between > 0.20 m {s['between'][1]}, "
                f"> 0.05 m {s['between'][2]} of {s['between'][0]} (worst {worst[k]:.3f}); outside moved {s['outside'][1]}/{s['outside'][0]}")
            res[f"mounds seed{seed} {k}"] = (s, worst[k])
    # crossings: stress drawings, random 1.5 m sweeps in 1 cm steps
    out("-- crossings: stress drawings (chained pad contours, gaps, spot lines, rotated), 360 random 1.5 m sweeps per seed")
    STR = _load("attack_crossings_stress", "attack_crossings_stress.py")
    for seed in (1, 2, 3):
        rng = random.Random(seed)
        rules3 = {"A": H.rule_a, "A-SB": CR.rule_sb, "N": rule_n}
        big = {k: 0 for k in rules3}
        worst = {k: 0.0 for k in rules3}
        newN = 0
        for d in range(60):
            lines, rot, pad, spacing, n = STR.drawing(rng)
            for s in range(6):
                cx = pad[0] - rng.uniform(0, n * spacing)
                cy = pad[1] - rng.uniform(0, n * spacing) if rng.random() < 0.7 else pad[1] + rng.uniform(0, 10)
                ang = rng.uniform(0, math.pi)
                p0 = (cx, cy)
                p1 = (cx + 1.5 * math.cos(ang), cy + 1.5 * math.sin(ang))
                path = [H.rotate_point(p0, rot), H.rotate_point(p1, rot)]
                rr = {k: sweep(f, lines, path, thr=0.05) for k, f in rules3.items()}
                for k in rules3:
                    worst[k] = max(worst[k], rr[k]["jump"])
                    big[k] += rr[k]["jump"] > 0.05
                newN += rr["N"]["jump"] > 0.05 and rr["A"]["jump"] <= 0.05
        out(f"   seed {seed}: sweeps with a true jump over 5 cm: " + ", ".join(f"{k} {big[k]}" for k in rules3)
            + f"; worst: " + ", ".join(f"{k} {worst[k]:.3f}" for k in rules3) + f"; N jumps where A does not: {newN}")
        res[f"stress seed{seed}"] = (big, worst, newN)
    # site-scale: the synthetic site, sweeps every 5 m both ways
    out("-- site-scale: synthetic 160 x 100 m site, 24 lines; straight sweeps every 5 m both ways (5 cm steps, bisected)")
    L = SS.synthetic_site()
    paths = [SS.seg((0.5, y), (159.5, y)) for y in [2.5 + 5.0 * k for k in range(int(97 / 5.0) + 1)]]
    paths += [SS.seg((x, 0.5), (x, 99.5)) for x in [2.5 + 5.0 * k for k in range(int(157 / 5.0) + 1)]]
    total = sum(_dist(p[0], p[1]) for p in paths)
    for name, f in (("A", H.rule_a), ("A-lat", SS.rule_a_lat), ("N", rule_n)):
        counts = {0.05: 0, 0.2: 0, 0.5: 0, 1.0: 0}
        biggest = (0.0, None)
        t0 = time.time()
        for path in paths:
            jumps, _ = SS.find_jumps(f, L, path, coarse=0.05, thr=0.05, max_report=10 ** 6)
            for size, where, a, b in jumps:
                for th in counts:
                    counts[th] += size > th
                if size > biggest[0]:
                    biggest = (size, where)
        per = {th: c / (total / 1000) for th, c in counts.items()}
        out(f"   {name:<6} jumps per km (>5 cm / >20 cm / >50 cm / >1 m): {per[0.05]:.1f} / {per[0.2]:.1f} / {per[0.5]:.1f} / "
            f"{per[1.0]:.1f}; largest {biggest[0]:.2f} m at {tuple(round(c, 2) for c in biggest[1]) if biggest[1] else '-'}"
            f"  ({time.time() - t0:.0f} s)")
        res[f"site {name}"] = (per, biggest[0])
    # consistency: Monte Carlo cut-out plans
    out("-- consistency: Monte Carlo cut-out plans (T1 contours cut by buildings, T2 pad, T3 lines stopping 0-50 mm short,"
        " T4 line ends snapped to corners); 60 plans each")
    MC = _load("attack_consistency_montecarlo", "attack_consistency_montecarlo.py")
    for kind in ("T1", "T2", "T3", "T4"):
        st = {k: {"n": 0, "moved": 0, "j3": 0, "j10": 0, "flip": 0, "mm1": 0, "mmflip": 0, "worst": 0.0} for k in ("A", "N")}
        for p in range(60):
            seed = 1000 * ("T1", "T2", "T3", "T4").index(kind) + p
            rng = random.Random(seed)
            if kind == "T1":
                lines, corners = MC.plan_t1(rng)
            elif kind == "T2":
                lines, corners = MC.plan_t2(rng)
            elif kind == "T3":
                lines, corners = MC.plan_t1(rng, gap_mm=50.0)
            else:
                lines, corners = MC.plan_t4(rng)
            if not lines or not corners:
                continue
            rmv = random.Random(seed + 7)
            a_ = rmv.uniform(0, 2 * math.pi)
            moved = [dict(ln, poly=[(x + 0.001 * math.cos(a_), y + 0.001 * math.sin(a_)) for x, y in ln["poly"]]) for ln in lines]
            for k, f in (("A", H.rule_a), ("N", rule_n)):
                s = st[k]
                for c in corners:
                    h0 = f(c, lines)
                    s["n"] += 1
                    s["moved"] += h0 is not None
                    mv, fl = 0.0, False
                    for q in range(8):
                        an = 2 * math.pi * q / 8
                        h1 = f((c[0] + 0.01 * math.cos(an), c[1] + 0.01 * math.sin(an)), lines)
                        if (h0 is None) != (h1 is None):
                            fl = True
                        elif h0 is not None:
                            mv = max(mv, abs(h1 - h0))
                    s["j3"] += mv > 0.03
                    s["j10"] += mv > 0.10
                    s["flip"] += fl
                    s["worst"] = max(s["worst"], mv)
                    hm = f(c, moved)
                    if (h0 is None) != (hm is None):
                        s["mmflip"] += 1
                    elif h0 is not None and abs(hm - h0) > 0.01:
                        s["mm1"] += 1
        for k in ("A", "N"):
            s = st[k]
            out(f"   {kind} {k:<3} corners {s['n']:4d}, moved {s['moved']:4d}; a 1 cm move changes it by > 3 cm: {s['j3']} "
                f"({100 * s['j3'] / max(1, s['n']):.2f} %), > 10 cm: {s['j10']}; moved/left switch {s['flip']}; "
                f"lines moved 1 mm change it by > 1 cm: {s['mm1']}, switches {s['mmflip']}; worst 1 cm move {s['worst']:.3f} m")
            res[f"mc {kind} {k}"] = s
    return res


def sec_jumps2d():
    head("2D JUMP SCANS of the consistency scenes (battery and attack): grid neighbours differing by > 2 cm bisected to"
         " 0.1 mm; a true jump keeps > 1 cm. Largest true jump per scene, A and N")
    scenes = COC.battery() + CO.attack_scenes()
    res = {}
    for sc in scenes:
        x0, x1, y0, y1 = sc.bbox(3.0)
        area = (x1 - x0) * (y1 - y0)
        sp = max(0.02, math.sqrt(area / 6000.0))
        nx, ny = int((x1 - x0) / sp) + 1, int((y1 - y0) / sp) + 1
        cells = []
        for k, f in (("A", H.rule_a), ("N", rule_n)):
            G = [[f((x0 + i * sp, y0 + j * sp), sc.lines) for i in range(nx)] for j in range(ny)]
            worst = (0.0, None)
            for j in range(ny):
                for i in range(nx):
                    h = G[j][i]
                    if h is None:
                        continue
                    for di, dj in ((1, 0), (0, 1)):
                        if i + di >= nx or j + dj >= ny:
                            continue
                        g = G[j + dj][i + di]
                        if g is None or abs(g - h) <= 0.02 or abs(g - h) <= worst[0]:
                            continue
                        p, q, ha, hb = (x0 + i * sp, y0 + j * sp), (x0 + (i + di) * sp, y0 + (j + dj) * sp), h, g
                        for _ in range(40):
                            if _dist(p, q) < 1e-4:
                                break
                            m = ((p[0] + q[0]) / 2, (p[1] + q[1]) / 2)
                            hm = f(m, sc.lines)
                            if hm is None:
                                break
                            if abs(hm - ha) >= abs(hb - hm):
                                q, hb = m, hm
                            else:
                                p, ha = m, hm
                        if _dist(p, q) < 1e-3 and abs(hb - ha) > 0.01 and abs(hb - ha) > worst[0]:
                            worst = (abs(hb - ha), q)
            cells.append(f"{k} {worst[0]:.3f}" + (f" at ({worst[1][0]:.2f},{worst[1][1]:.2f})" if worst[1] else ""))
            res[f"{sc.key} {k}"] = worst[0]
        out(f"   {sc.key:<14} grid {sp * 100:4.1f} cm   " + "   ".join(cells))
    return res


def sec_order():
    head("ORDER / DIRECTION: every case point, and close-lines' corner grids, with the lines reversed in order and"
         " direction, each line drawn backwards, and shuffled")
    variants_ = OrderedDict([("reversed order and direction", H.reversed_lines),
                             ("each line backwards", lambda L: [dict(ln, poly=list(reversed(ln["poly"]))) for ln in L]),
                             ("shuffled", lambda L: random.Random(7).sample(L, len(L)))])
    recs = all_recs()
    worst = (0.0, None)
    n = 0
    flips = 0
    for r in recs:
        h = rule_n(r.xy, r.lines)
        for vname, vf in variants_.items():
            g = rule_n(r.xy, vf(r.lines))
            n += 1
            if (h is None) != (g is None):
                flips += 1
                worst = (99.0, (r.src, r.case, r.label, vname, h, g))
            elif h is not None and abs(h - g) > worst[0]:
                worst = (abs(h - g), (r.src, r.case, r.label, vname, h, g))
    out(f"   case points x 3 variants: {n} evaluations; largest difference {worst[0]:.2e} m; moved/left flips {flips}"
        + (f"; at {worst[1]}" if worst[0] > 1e-9 else ""))
    byk = {c.key: c for c in CL.close_cases()}
    boxes = [("CL1", (1.75, 1.25, 2.0, 1.5)), ("CL2", (10.0, 6.0, 11.0, 7.0)), ("CL4", (-0.25, 0.0, 0.0, 0.25)),
             ("CL5", (0.0, -0.3, 1.0, 0.5)), ("CL7", (0.9, -0.5, 1.5, 2.5)), ("CL9", (-0.3, -0.3, 0.3, 0.1)),
             ("CL12", (0.0, 0.0, 1.0, 1.0))]
    res = {"points": worst[0], "flips": flips}
    for key, (x0, y0, x1, y1) in boxes:
        L = byk[key].lines
        step = 0.01 if (x1 - x0) <= 0.6 else 0.02
        nx, ny = int(round((x1 - x0) / step)), int(round((y1 - y0) / step))
        pts = [(x0 + i * step, y0 + j * step) for i in range(nx + 1) for j in range(ny + 1)]
        w = 0.0
        for vname, vf in variants_.items():
            L2 = vf(L)
            for p in pts:
                h, g = rule_n(p, L), rule_n(p, L2)
                d = 0.0 if (h is None and g is None) else (99.0 if (h is None) != (g is None) else abs(h - g))
                w = max(w, d)
        out(f"   {key:<6} {len(pts):5d} grid points: largest difference {w:.2e} m")
        res[key] = w
    return res


def sec_timing():
    head("TIMING (plain CPython; C# runs such loops 50-100x faster)")
    rnd = random.Random(1)
    lines = []
    for k in range(40):
        cx, cy = rnd.uniform(0, 200), rnd.uniform(0, 200)
        wx, wy = rnd.uniform(5, 30), rnd.uniform(5, 30)
        lines.append(CL.line(40 + k * 0.1, CL.rect(cx - wx, cy - wy, cx + wx, cy + wy)))
        lines.append(CL.line(40 + k * 0.1 + 0.5, CL.rect(cx - wx + 0.3, cy - wy + 0.3, cx + wx - 0.3, cy + wy - 0.3)))
    for k in range(60):
        lines.append(CL.line(40 + rnd.uniform(0, 5), CL.ring(rnd.uniform(0, 200), rnd.uniform(0, 200), rnd.uniform(2, 15), 5)))
    for k in range(60):
        x0, y0 = rnd.uniform(0, 200), rnd.uniform(0, 200)
        lines.append(CL.line(40 + rnd.uniform(0, 5), [(x0 + 3 * i, y0 + rnd.uniform(-1, 1)) for i in range(20)]))
    pts = [(rnd.uniform(0, 200), rnd.uniform(0, 200)) for _ in range(2000)]
    segs = sum(len(l["poly"]) - 1 for l in lines)
    out(f"   {len(pts)} points x {len(lines)} lines ({segs} segments)")
    t = time.perf_counter()
    pr = prepare(lines)
    tb = time.perf_counter() - t
    out(f"   step 0 (bridges): {tb:.2f} s, {len(pr.bridges)} bridges")
    res = {"bridges_s": tb}
    for name, f in (("A", lambda v: H.rule_a(v, lines)), ("N", lambda v: corner_height(v, pr)),
                    ("N without the shortcut", lambda v: corner_height(v, pr, shortcut=False))):
        t = time.perf_counter()
        vals = [f(p) for p in pts]
        dt = time.perf_counter() - t
        out(f"   {name:<24} {dt:6.2f} s  ({1e6 * dt / len(pts) / segs:.2f} us per point-segment)")
        res[name] = dt
        if name == "N":
            vn = vals
        if name == "N without the shortcut":
            diff = max((0.0 if (a is None and b is None) else (99.0 if (a is None) != (b is None) else abs(a - b)))
                       for a, b in zip(vn, vals))
            out(f"   shortcut check: largest difference with and without it {diff:.2e} m")
            res["shortcut_diff"] = diff
    na = sum(1 for p in pts if True)
    differ = sum(1 for p, x in zip(pts, vn) if (H.rule_a(p, lines) is None) != (x is None) or
                 (x is not None and abs(H.rule_a(p, lines) - x) > 1e-9))
    out(f"   N differs from A at {differ} of {na} random points of this (unrealistic) site")
    # 300 lines, 400 ends: bridging cost
    rnd = random.Random(3)
    big = []
    for k in range(400):
        z = 40.0 + 0.5 * rnd.randrange(12)
        x, y = rnd.uniform(0, 200), rnd.uniform(0, 200)
        a = rnd.uniform(0, math.pi)
        big.append(H.line(z, [(x, y), (x + 20 * math.cos(a), y + 20 * math.sin(a)), (x + 30 * math.cos(a + 0.3), y + 30 * math.sin(a + 0.3))]))
    t = time.perf_counter()
    nb = len(make_bridges(big))
    out(f"   bridging 400 random 3-vertex lines (800 ends): {time.perf_counter() - t:.2f} s, {nb} bridges")
    return res


def sec_ablation():
    head("ABLATION: each part of rule N switched off in turn, on every case point (misses / points with an expected value)")
    recs = all_recs()
    rules = OrderedDict(list(N.items()) + list(ABLATIONS.items()))
    cnt = {k: [0, 0] for k in rules}
    changed = {k: [] for k in rules}
    for r in recs:
        base = rule_n(r.xy, r.lines)
        for k, f in rules.items():
            h = f(r.xy, r.lines)
            j = r.judge(h)
            if j is not None:
                cnt[k][0] += 1
                cnt[k][1] += j is False
            if k != "N":
                d = (h is None) != (base is None) or (h is not None and abs(h - base) > 1e-6)
                if d:
                    jb = r.judge(base)
                    changed[k].append((r.src, r.case, r.label, base, h, jb, j))
    for k in rules:
        out(f"   {k:<14} misses {cnt[k][1]:4d} / {cnt[k][0]}")
    for k in ABLATIONS:
        ch = changed[k]
        worse = [c for c in ch if c[5] is True and c[6] is False]
        better = [c for c in ch if c[5] is False and c[6] is True]
        out(f"   {k}: changes {len(ch)} points; right -> wrong {len(worse)}, wrong -> right {len(better)}")
        for c in (worse[:8] + better[:8]):
            out(f"      {'WORSE ' if c in worse else 'BETTER'} {c[0]} {c[1]} {c[2]}: N {fmt(c[3])} -> {fmt(c[4])}")
    return {k: v for k, v in cnt.items()}


def sec_vectors():
    """Test vectors for the C# port: every case point with its lines and rule N's value
    (final_vectors.json; null = keep the old height)."""
    head("TEST VECTORS for the C# port (final_vectors.json)")
    recs = all_recs()
    seen, scenes, pts = {}, [], []
    for r in recs:
        key = id(r.lines)
        if key not in seen:
            pr = _prepared_for(r.lines)
            seen[key] = len(scenes)
            scenes.append({"src": r.src, "case": r.case,
                           "lines": [{"z": ln["z"], "poly": [list(p) for p in ln["poly"]]} for ln in r.lines],
                           "bridges": [{"z": b["z"], "poly": [list(p) for p in b["poly"]]} for b in pr.bridges]})
        h, inf = rule_n(r.xy, r.lines, detail=True)
        pts.append({"scene": seen[key], "label": r.label, "xy": list(r.xy), "N": h, "why": inf["why"],
                    "expected": r.expect_txt, "judge": r.judge(h)})
    with open(os.path.join(HERE, "final_vectors.json"), "w", encoding="utf-8") as fh:
        json.dump({"rule": "N", "units": "metres", "scenes": scenes, "points": pts}, fh, indent=0)
    out(f"   {len(scenes)} scenes, {len(pts)} points, {sum(len(s['bridges']) for s in scenes)} bridges")
    return {"scenes": len(scenes), "points": len(pts)}


SECTIONS = OrderedDict([("screenshot", sec_screenshot), ("battery", sec_battery), ("cases", sec_cases),
                        ("sweeps", sec_sweeps), ("grids", sec_grids), ("order", sec_order), ("ablation", sec_ablation),
                        ("jumps2d", sec_jumps2d), ("random", sec_random), ("timing", sec_timing),
                        ("vectors", sec_vectors)])

if __name__ == "__main__":
    want = [a for a in sys.argv[1:] if not a.startswith("--")] or list(SECTIONS)
    tag = "_".join(want) if sys.argv[1:] else "all"
    results = {}
    t_all = time.time()
    for k in want:
        t0 = time.time()
        results[k] = SECTIONS[k]()
        out(f"   [{k}: {time.time() - t0:.0f} s]")
    out(f"[total {time.time() - t_all:.0f} s]")
    with open(os.path.join(HERE, f"final_out_{tag}.txt"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(OUT_LINES) + "\n")
    with open(os.path.join(HERE, f"final_out_{tag}.json"), "w", encoding="utf-8") as fh:
        json.dump(results, fh, indent=1, default=str)
