using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Bone-length driven cage generation.
//
// The cage is deliberately coarse: seven axis-aligned rectangular rings -- crown, two elbows,
// two fingertips, one across both knees, one across both soles -- whose corners are stitched
// into flat panels. A front and back silhouette (torso, two arms, legs) plus one quad per
// silhouette edge closes it: 28 vertices, 52 triangles. Four of those quads are the rings
// themselves, capping the shell at the crown, the fingertips and the soles.
//
// bake() (editor) reads the rest mesh + skeleton once and distils everything the generator
// needs into cage_constants: per-joint FK data, per-ring placement, and the fixed topology.
// build() (runtime) is a pure function of the current bone lengths + those constants: it
// re-runs forward kinematics for the joint centers, re-places the rings on them and emits the
// mesh. All geometry lives in root-bone (Hips) local space; the cage GameObject is a child of
// that root, so the scene's x100 scale is inherited.

// A ring is placed by its anchor joints rather than fixed to one: n points away from the body, so
// an edge sits at the anchor farthest along it -- the lower knee, the outer fingertip -- and the
// rectangle spans the anchors' own spread plus the baked reach on each side.
//
// The two silhouette edges are placed independently, each from the anchors on its own side. A ring
// shared by both limbs (the knees, the soles) therefore tilts to track both legs, rather than being
// pinned along n by whichever leg is longer. A ring on a single limb lists that limb's joints on
// both sides and stays axis aligned.
[Serializable]
public class cage_ring{
    public int[] anchor_hi;     // joints placing the +s edge (indices into cage_constants.joint_name)
    public int[] anchor_lo;     // joints placing the -s edge
    public Vector3 n;           // ring normal, pointing away from the body
    public Vector3 s;           // in-plane axis the front/back silhouette runs along
    public Vector3 d;           // in-plane axis separating the front and back panels
    public float along;         // edge offset past its farthest anchor, along n
    public float s_lo, s_hi;    // reach beyond the anchors' span, on the -s and +s side
    public float d_lo, d_hi;
}

[Serializable]
public class cage_constants{
    // Joints, in parent-before-child order, for forward kinematics.
    public string[] joint_name;
    public int[] joint_parent;      // index into joint_name; -1 for the root
    public Vector3[] joint_dir;     // unit rest direction parent->joint (Hips space, invariant to length edits)
    public float[] joint_rest_len;  // native rest distance parent->joint

    public cage_ring[] rings;
    public int[] tris;              // indices into the 4*rings.Length corner vertices
}

public static class cage{
    // Corner layout inside a ring: the s axis gives the silhouette side (hi/lo), the d axis the
    // front/back side. Vertex index is ring * 4 + corner.
    const int hi_front = 0, hi_back = 1, lo_back = 2, lo_front = 3;

    // The cage control points for the given lengths, in rig root local space: the joint centers
    // reconstructed from those lengths with the baked rings re-placed on them. Lengths are native
    // (joint.localPosition.magnitude), keyed by joint name; joints nobody edits (fingers, toes)
    // fall back to their baked rest length, so an empty table yields the rest cage.
    public static Vector3[] points(IReadOnlyDictionary<string, float> lengths, cage_constants k){
        return ring_corners(k, joint_centers(lengths, k));
    }

    // The same control points wrapped in the fixed-topology mesh, for display.
    public static Mesh build(IReadOnlyDictionary<string, float> lengths, cage_constants k){
        var mesh = new Mesh{ name = "cage" };
        mesh.vertices = points(lengths, k);
        mesh.triangles = k.tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Forward kinematics: place every joint from its parent along the baked (invariant) rest
    // direction, scaled to the current bone length. Directions never change under length edits,
    // so this reproduces the live skeleton exactly.
    static Vector3[] joint_centers(IReadOnlyDictionary<string, float> lengths, cage_constants k){
        var jc = new Vector3[k.joint_name.Length];
        for(var j = 0; j < jc.Length; j++){
            var p = k.joint_parent[j];
            if(p < 0){
                jc[j] = Vector3.zero;
            }
            else{
                var len = lengths.TryGetValue(k.joint_name[j], out var v) ? v : k.joint_rest_len[j];
                jc[j] = jc[p] + k.joint_dir[j] * len;
            }
        }
        return jc;
    }

    // The ring axes are orthonormal and cardinal, so summing the three components rebuilds a
    // corner exactly. Each silhouette edge is placed along n by its own anchors; the depth extent
    // is shared, which keeps the four corners planar however far the two edges drift apart.
    static Vector3[] ring_corners(cage_constants k, Vector3[] jc){
        var verts = new Vector3[k.rings.Length * 4];
        for(var i = 0; i < k.rings.Length; i++){
            var r = k.rings[i];
            var a_hi = r.anchor_hi.Select(j => jc[j]).ToArray();
            var a_lo = r.anchor_lo.Select(j => jc[j]).ToArray();

            var plane_hi = r.n * (a_hi.Max(p => Vector3.Dot(p, r.n)) + r.along);
            var plane_lo = r.n * (a_lo.Max(p => Vector3.Dot(p, r.n)) + r.along);
            var edge_hi = r.s * (a_hi.Max(p => Vector3.Dot(p, r.s)) + r.s_hi);
            var edge_lo = r.s * (a_lo.Min(p => Vector3.Dot(p, r.s)) - r.s_lo);

            var a = a_hi.Concat(a_lo);
            var lo_d = r.d * (a.Min(p => Vector3.Dot(p, r.d)) - r.d_lo);
            var hi_d = r.d * (a.Max(p => Vector3.Dot(p, r.d)) + r.d_hi);

            verts[i * 4 + hi_front] = plane_hi + edge_hi + hi_d;
            verts[i * 4 + hi_back] = plane_hi + edge_hi + lo_d;
            verts[i * 4 + lo_back] = plane_lo + edge_lo + lo_d;
            verts[i * 4 + lo_front] = plane_lo + edge_lo + hi_d;
        }
        return verts;
    }

#if UNITY_EDITOR
    // Ring slots. The topology tables index these directly. hi/lo name the two sides of a
    // ring's silhouette axis, so an "hi" limb ring is the one on the +side (character's left).
    const int crown = 0, elbow_hi = 1, tip_hi = 2, elbow_lo = 3, tip_lo = 4, knee = 5, sole = 6;

    // Panel outlines as (ring, silhouette side) pairs, all traced in the same sense. Each is
    // emitted twice: once on the front corners, once reversed on the back.
    static readonly (int ring, bool hi)[][] panels = {
        new[]{ (crown, true), (elbow_hi, true), (elbow_hi, false), (knee, true),
               (knee, false), (elbow_lo, false), (elbow_lo, true), (crown, false) },
        new[]{ (elbow_hi, true), (tip_hi, true), (tip_hi, false), (elbow_hi, false) },
        new[]{ (elbow_lo, false), (tip_lo, false), (tip_lo, true), (elbow_lo, true) },
        new[]{ (knee, true), (sole, true), (sole, false), (knee, false) },
    };

    // The silhouette boundary of those panels, traced in the same sense. Every consecutive pair
    // spans a quad joining the front outline to the back; the four pairs that name one ring
    // twice are its own rectangle, which is where the shell caps.
    static readonly (int ring, bool hi)[] perimeter = {
        (crown, true), (elbow_hi, true), (tip_hi, true), (tip_hi, false), (elbow_hi, false),
        (knee, true), (sole, true), (sole, false), (knee, false),
        (elbow_lo, false), (tip_lo, false), (tip_lo, true), (elbow_lo, true), (crown, false),
    };

    // Fractional slack added to every measured extent so the shell clears the flesh instead of
    // touching it. Editable constant.
    const float margin = 0.05f;

    // Cross-section window of a joint ring, as a fraction of its anchor bone's rest length: only
    // flesh within this distance of the ring plane sets that ring's thickness. Editable constant.
    const float slab = 0.25f;

    // What a ring is fitted to. n points away from the body; a terminal ring is pushed past the
    // flesh it wraps (and its rectangle caps the shell), while a joint ring stays on its anchors
    // and takes the cross-section of the flesh crossing that plane.
    class recipe{
        public int[] anchor;
        public int[] wrap;          // subtree roots whose flesh the ring must enclose
        public Vector3 n, s, d;
        public bool terminal;
        public float front, back;   // extra depth reach past the flesh, in scene units
        public float hi;            // the same on the +silhouette side: up on the elbow and
                                    // fingertip rings, the character's left on the others
    }

    public static cage_constants bake(SkinnedMeshRenderer source){
        var root = source.rootBone;
        var bones = root.GetComponentsInChildren<Transform>(true);
        var index = bones.Select((t, i) => (t, i)).ToDictionary(e => e.t.name, e => e.i);

        var rest = bones.Select(t => root.InverseTransformPoint(t.position)).ToArray();
        var parent = bones.Select(t => t == root ? -1 : index[t.parent.name]).ToArray();
        var rest_len = parent.Select((p, j) => p < 0 ? 0f : (rest[j] - rest[p]).magnitude).ToArray();
        var dir = parent.Select((p, j) => p < 0 ? Vector3.zero : (rest[j] - rest[p]).normalized).ToArray();

        // The rig root's local space is not world aligned, so the ring axes come from the rest
        // skeleton itself: up along the spine, side across the arms (toward the character's left),
        // depth completing the frame. Snapping each to a cardinal axis keeps the rings axis aligned.
        // The cross product picks the depth axis but not which way the body faces, so the toes
        // settle it: +depth is the front, which is what the per-ring front/back reach below means.
        var up = cardinal(rest[index["Head"]] - rest[index["Hips"]]);
        var side = cardinal(rest[index["LeftArm"]] - rest[index["RightArm"]]);
        var across = Vector3.Cross(up, side);
        var toe = rest[index["LeftToeBase"]] - rest[index["LeftFoot"]];
        var depth = across * Mathf.Sign(Vector3.Dot(toe, across));

        var flesh = gather_flesh(source, root, index);

        int[] js(params string[] names){
            return names.Select(n => index[n]).ToArray();
        }

        // The fingertip ring hangs off the most distal finger bone, whichever of them that is.
        int far_finger(string hand){
            var h = index[hand];
            return subtree(h, parent).OrderByDescending(j => (rest[j] - rest[h]).sqrMagnitude).First();
        }

        // The reach fields are what pull the panels out over flesh the rings themselves do not see:
        // the face, the chest and belly, the buttocks, the shoulders above the elbows. Editable.
        var recipes = new recipe[7];
        recipes[crown] = new recipe{ anchor = js("Head"), wrap = js("Head"), n = up, s = side, d = depth, terminal = true, front = 0.1f };
        recipes[elbow_hi] = new recipe{ anchor = js("LeftForeArm"), wrap = js("LeftArm"), n = side, s = up, d = depth, terminal = false, front = 0.2f, back = 0.1f, hi = 0.05f };
        recipes[tip_hi] = new recipe{ anchor = new[]{ far_finger("LeftHand") }, wrap = js("LeftHand"), n = side, s = up, d = depth, terminal = true };
        recipes[elbow_lo] = new recipe{ anchor = js("RightForeArm"), wrap = js("RightArm"), n = -side, s = up, d = depth, terminal = false, front = 0.2f, back = 0.1f, hi = 0.05f };
        recipes[tip_lo] = new recipe{ anchor = new[]{ far_finger("RightHand") }, wrap = js("RightHand"), n = -side, s = up, d = depth, terminal = true };
        recipes[knee] = new recipe{ anchor = js("LeftLeg", "RightLeg"), wrap = js("LeftUpLeg", "RightUpLeg"), n = -up, s = side, d = depth, terminal = false, back = 0.1f };
        recipes[sole] = new recipe{ anchor = js("LeftFoot", "LeftToeBase", "RightFoot", "RightToeBase"), wrap = js("LeftFoot", "RightFoot"), n = -up, s = side, d = depth, terminal = true };

        // Widen a measured span by the margin, about its middle.
        static (float lo, float hi) inflate(float lo, float hi){
            var mid = (lo + hi) * 0.5f;
            var half = (hi - lo) * 0.5f * (1f + margin);
            return (mid - half, mid + half);
        }

        // Ring geometry is native (rig root local), so the recipes' scene-unit reach converts here.
        var scale = root.lossyScale.x;

        // Fit one ring to the rest flesh it must enclose: how far past its anchors the plane sits,
        // and how far the rectangle reaches beyond the anchors' span on each side.
        cage_ring measure(recipe r){
            var anchors = r.anchor.Select(j => rest[j]).ToArray();
            var wrap = r.wrap.SelectMany(a => subtree(a, parent)).SelectMany(j => flesh[j]).ToArray();
            var plane = anchors.Max(p => Vector3.Dot(p, r.n));

            // A terminal ring is sized by all the flesh it wraps; a joint ring only by the flesh
            // crossing its plane, within a window scaled to the bone it sits on.
            var window = slab * r.anchor.Max(j => rest_len[j]);
            var pts = r.terminal ? wrap : wrap.Where(p => Mathf.Abs(Vector3.Dot(p, r.n) - plane) <= window).ToArray();
            var (lo_s, hi_s) = inflate(pts.Min(p => Vector3.Dot(p, r.s)), pts.Max(p => Vector3.Dot(p, r.s)));
            var (lo_d, hi_d) = inflate(pts.Min(p => Vector3.Dot(p, r.d)), pts.Max(p => Vector3.Dot(p, r.d)));

            // Which anchors place which silhouette edge, by the side of the ring they rest on. The
            // two legs of a shared ring separate here; a single-limb ring lands on both sides.
            var mid = (anchors.Min(p => Vector3.Dot(p, r.s)) + anchors.Max(p => Vector3.Dot(p, r.s))) * 0.5f;
            var hi = r.anchor.Where(j => Vector3.Dot(rest[j], r.s) >= mid).ToArray();
            var lo = r.anchor.Where(j => Vector3.Dot(rest[j], r.s) <= mid).ToArray();

            return new cage_ring{
                anchor_hi = hi,
                anchor_lo = lo,
                n = r.n,
                s = r.s,
                d = r.d,
                along = r.terminal ? (wrap.Max(p => Vector3.Dot(p, r.n)) - plane) * (1f + margin) : 0f,
                s_lo = lo.Min(j => Vector3.Dot(rest[j], r.s)) - lo_s,
                s_hi = hi_s - hi.Max(j => Vector3.Dot(rest[j], r.s)) + r.hi / scale,
                d_lo = anchors.Min(p => Vector3.Dot(p, r.d)) - lo_d + r.back / scale,
                d_hi = hi_d - anchors.Max(p => Vector3.Dot(p, r.d)) + r.front / scale,
            };
        }

        var k = new cage_constants{
            joint_name = bones.Select(t => t.name).ToArray(),
            joint_parent = parent,
            joint_dir = dir,
            joint_rest_len = rest_len,
            rings = recipes.Select(measure).ToArray(),
            tris = topology(),
        };

        // The panels are traced in one consistent sense, but which sense faces outward depends on
        // the rig's axes. The enclosed volume settles it: the root sits inside the cage.
        if(volume(ring_corners(k, rest), k.tris) < 0.0){
            for(var t = 0; t < k.tris.Length; t += 3){
                (k.tris[t + 1], k.tris[t + 2]) = (k.tris[t + 2], k.tris[t + 1]);
            }
        }
        return k;
    }

    // Nearest signed unit axis.
    static Vector3 cardinal(Vector3 v){
        var axes = new[]{ Vector3.right, Vector3.up, Vector3.forward };
        var a = axes.OrderByDescending(x => Mathf.Abs(Vector3.Dot(v, x))).First();
        return a * Mathf.Sign(Vector3.Dot(v, a));
    }

    static IEnumerable<int> ancestors(int j, int[] parent){
        for(var c = j; c >= 0; c = parent[c]){
            yield return c;
        }
    }

    // Joint indices in the subtree rooted at a, a included.
    static IEnumerable<int> subtree(int a, int[] parent){
        return Enumerable.Range(0, parent.Length).Where(j => ancestors(j, parent).Contains(a));
    }

    // Group each skinned vertex (in Hips space, at rest) under its dominant bone. This is the
    // flesh the rings are measured against.
    static List<Vector3>[] gather_flesh(SkinnedMeshRenderer source, Transform root, Dictionary<string, int> index){
        var mesh = source.sharedMesh;
        var bones = source.bones;
        var binds = mesh.bindposes;
        var weights = mesh.boneWeights;
        var verts = mesh.vertices;

        var slot = bones.Select(b => index[b.name]).ToArray();

        var flesh = Enumerable.Range(0, index.Count).Select(_ => new List<Vector3>()).ToArray();
        for(var i = 0; i < verts.Length; i++){
            var b = dominant_bone(weights[i]);
            var world = bones[b].localToWorldMatrix.MultiplyPoint3x4(binds[b].MultiplyPoint3x4(verts[i]));
            flesh[slot[b]].Add(root.InverseTransformPoint(world));
        }
        return flesh;
    }

    static int dominant_bone(BoneWeight w){
        var b = w.boneIndex0;
        var m = w.weight0;
        if(w.weight1 > m){ b = w.boneIndex1; m = w.weight1; }
        if(w.weight2 > m){ b = w.boneIndex2; m = w.weight2; }
        if(w.weight3 > m){ b = w.boneIndex3; }
        return b;
    }

    // Fixed topology: every panel on the front and mirrored on the back, then one quad per
    // silhouette edge. The rig-independent part of the cage.
    static int[] topology(){
        var tris = new List<int>();

        foreach(var panel in panels){
            strip(tris, panel.Select(c => corner(c, true)));
            strip(tris, panel.Reverse().Select(c => corner(c, false)));
        }

        for(var i = 0; i < perimeter.Length; i++){
            var a = perimeter[i];
            var b = perimeter[(i + 1) % perimeter.Length];
            strip(tris, new[]{ corner(b, true), corner(a, true), corner(a, false), corner(b, false) });
        }

        // Every directed edge appears exactly once and its opposite exists: the shell is closed and
        // every panel is traced the same way round. The tables above are easy to mistrace by hand.
        var edges = Enumerable.Range(0, tris.Count / 3)
            .SelectMany(t => Enumerable.Range(0, 3).Select(e => (a: tris[t * 3 + e], b: tris[t * 3 + (e + 1) % 3])))
            .ToArray();
        Debug.Assert(edges.Distinct().Count() == edges.Length, "cage: panels overlap or are traced against each other");
        Debug.Assert(edges.All(e => edges.Contains((e.b, e.a))), "cage: panels do not close the shell");
        return tris.ToArray();
    }

    static int corner((int ring, bool hi) c, bool front){
        return c.ring * 4 + (c.hi ? (front ? hi_front : hi_back) : (front ? lo_front : lo_back));
    }

    // Triangulate an outline as a ladder between its two halves, preserving the traced sense: each
    // rung pairs a corner from one side with the one facing it. A fan would pivot the whole panel on
    // its first corner, which skews a non-planar panel -- every ring carries its own depth, so the
    // torso would run straight from the crown to the knees and bypass the elbow rings.
    static void strip(List<int> tris, IEnumerable<int> loop){
        var v = loop.ToArray();
        for(var i = 0; i < v.Length / 2 - 1; i++){
            var j = v.Length - 1 - i;
            tris.Add(v[i]); tris.Add(v[i + 1]); tris.Add(v[j - 1]);
            tris.Add(v[i]); tris.Add(v[j - 1]); tris.Add(v[j]);
        }
    }

    // Signed volume of a closed triangle soup; positive when the winding puts the normals out.
    static double volume(Vector3[] v, int[] tris){
        return Enumerable.Range(0, tris.Length / 3)
            .Sum(t => (double)Vector3.Dot(v[tris[t * 3]], Vector3.Cross(v[tris[t * 3 + 1]], v[tris[t * 3 + 2]]))) / 6.0;
    }

    // Containment check against the current (deformed) cage: the rig-space positions of every
    // skinned mesh vertex that falls outside the shell. The mesh is skinned to the live bones, so
    // both mesh and cage follow the current lengths. (Until cage-based mesh deformation exists the
    // mesh skins by LBS, which the cage does not match -- so escapes here are expected, and this
    // coarse a cage escapes a lot.) Geometry (bind-space verts/weights/bindposes) is read from geo
    // -- always readable -- while the live bones come from pose; the two are identical-topology
    // clones sharing bone order.
    public static List<Vector3> find_outside(SkinnedMeshRenderer geo, SkinnedMeshRenderer pose, IReadOnlyDictionary<string, float> lengths, cage_constants k){
        var root = pose.rootBone;
        var mesh = geo.sharedMesh;
        var bones = pose.bones;
        var binds = mesh.bindposes;
        var weights = mesh.boneWeights;
        var verts = mesh.vertices;

        var cage_verts = points(lengths, k);

        var outside = new List<Vector3>();
        for(var i = 0; i < verts.Length; i++){
            var p = root.InverseTransformPoint(skinned(verts[i], weights[i], bones, binds));
            if(!inside(p, cage_verts, k.tris)){
                outside.Add(p);
            }
        }
        return outside;
    }

    // Linear blend skinning of one vertex to the live pose, in world space.
    static Vector3 skinned(Vector3 v, BoneWeight w, Transform[] bones, Matrix4x4[] binds){
        Vector3 bone(int b){
            return bones[b].localToWorldMatrix.MultiplyPoint3x4(binds[b].MultiplyPoint3x4(v));
        }
        return bone(w.boneIndex0) * w.weight0 + bone(w.boneIndex1) * w.weight1
            + bone(w.boneIndex2) * w.weight2 + bone(w.boneIndex3) * w.weight3;
    }

    // The cage is a closed shell, so an odd crossing count along any ray means the point is in.
    // The direction is arbitrary, skewed off the cardinal axes the panels are aligned to.
    static bool inside(Vector3 p, Vector3[] v, int[] tris){
        var dir = new Vector3(0.5773f, 0.3313f, 0.7449f).normalized;
        var hits = Enumerable.Range(0, tris.Length / 3)
            .Count(t => pierces(p, dir, float.PositiveInfinity, v[tris[t * 3]], v[tris[t * 3 + 1]], v[tris[t * 3 + 2]]));
        return hits % 2 == 1;
    }

    // Cage triangles that pierce a triangle they share no corner with. The shell is closed and
    // stays clean as long as the rings keep their order, so a hit here means a length edit pushed
    // one panel through another.
    public static List<int> self_overlaps(IReadOnlyDictionary<string, float> lengths, cage_constants k){
        var v = points(lengths, k);
        var count = k.tris.Length / 3;

        bool shares_corner(int a, int b){
            var ca = k.tris.Skip(a * 3).Take(3);
            return k.tris.Skip(b * 3).Take(3).Any(i => ca.Contains(i));
        }

        // One triangle's edge ending inside the other is a genuine intersection; coplanar overlap
        // is not reachable here since every panel spans a distinct pair of rings.
        bool crosses(int a, int b){
            bool edge_pierces(int t, int e, int other){
                var o = v[k.tris[t * 3 + e]];
                var span = v[k.tris[t * 3 + (e + 1) % 3]] - o;
                return pierces(o, span, 1f, v[k.tris[other * 3]], v[k.tris[other * 3 + 1]], v[k.tris[other * 3 + 2]]);
            }
            var edges = Enumerable.Range(0, 3);
            return edges.Any(e => edge_pierces(a, e, b)) || edges.Any(e => edge_pierces(b, e, a));
        }

        var hit = new HashSet<int>();
        for(var a = 0; a < count; a++){
            for(var b = a + 1; b < count; b++){
                if(!shares_corner(a, b) && crosses(a, b)){
                    hit.Add(a);
                    hit.Add(b);
                }
            }
        }
        return hit.ToList();
    }

    // Moller-Trumbore: does the ray o + dir * t, t in (0, max], pierce the triangle?
    static bool pierces(Vector3 o, Vector3 dir, float max, Vector3 a, Vector3 b, Vector3 c){
        var e1 = b - a;
        var e2 = c - a;
        var h = Vector3.Cross(dir, e2);
        var det = Vector3.Dot(e1, h);

        var hit = false;
        if(Mathf.Abs(det) > 1e-15f){
            var inv = 1f / det;
            var g = o - a;
            var u = Vector3.Dot(g, h) * inv;
            var q = Vector3.Cross(g, e1);
            var w = Vector3.Dot(dir, q) * inv;
            var t = Vector3.Dot(e2, q) * inv;
            hit = u >= 0f && w >= 0f && u + w <= 1f && t > 0f && t <= max;
        }
        return hit;
    }
#endif
}
