using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Bone-length driven cage generation.
//
// The body is deliberately coarse: ten axis-aligned rectangular rings -- crown, two arms, two
// elbows, two wrists, one across both hips, one across both knees, one across both soles -- whose
// corners are stitched into flat panels. Posts on the midline (see cage_post) -- one per ring
// across the body, plus the bottom of the neck's V and the sternum -- split the torso, head and
// leg panels into a left and a right half; the arm rings' top edges are drawn in to meet at the
// neck post, so the V parts the torso from the head. A front and back silhouette plus one quad per
// silhouette edge closes it; the quads along the crown and sole rings themselves cap the shell
// there. Past each wrist the hand is resolved finger by finger, out of posts rather than rings:
// 188 vertices, 372 triangles in all.
//
// bake() (editor) reads the rest mesh + skeleton once and distils everything the generator
// needs into cage_constants: per-joint FK data, per-ring placement, and the fixed topology.
// build() (runtime) is a pure function of the current bone lengths + those constants: it
// re-runs forward kinematics for the joint centers, re-places the rings on them and emits the
// mesh. All geometry lives in root-bone (Hips) local space; the cage GameObject is a child of
// that root, so the scene's x100 scale is inherited.

// A ring is placed by its anchor joints rather than fixed to one: n points away from the body, so
// an edge sits at the anchor farthest along it -- the lower knee, the higher hip -- and the
// rectangle spans the anchors' own spread plus the baked reach on each side.
//
// The two silhouette edges are placed independently, each from the anchors on its own side. A ring
// shared by both limbs (the knees, the soles) therefore tilts to track both legs, rather than being
// pinned along n by whichever leg is longer. A ring on a single limb lists that limb's joints on
// both sides and stays axis aligned.
[Serializable]
public class cage_ring{
    public string name;         // as the design document calls it; the debug view's tag
    public int[] anchor_hi;     // joints placing the +s edge (indices into cage_constants.joint_name)
    public int[] anchor_lo;     // joints placing the -s edge
    public Vector3 n;           // ring normal, pointing away from the body
    public Vector3 s;           // in-plane axis the front/back silhouette runs along
    public Vector3 d;           // in-plane axis separating the front and back panels
    public float along_hi, along_lo;    // each edge's offset past its farthest anchor, along n; unequal on a tilted ring
    public float s_lo, s_hi;    // reach beyond the anchors' span, on the -s and +s side
    public float hi_front, lo_front;    // each corner's reach along d past the anchors' depth span:
    public float hi_back, lo_back;      // front past its max, back past its min
}

// A post is the pair of vertices one control point owns, along one axis d. The hand needs this
// weaker unit than a ring because its branch rings meet at shared control points -- the back of the
// hand has to stay a single polygon -- and because a finger ring straddles its own bone rather than
// a cardinal axis. The body uses it for the midline: the vertex a ring's front and back edges leave
// where they cross the middle, so the panels either side of it stay two halves.
//
// The in-plane position is an affine combination of joint centers plus a baked offset: one joint
// for a finger ring, two halves for a valley between fingers or for the middle of a shared ring, and
// (1+f, -f) to reach a virtual end bone past the last phalanx, which is how that ring follows the
// phalanx it extends. Along d the two ends are placed like a ring's edges, each off its own anchors:
// the wrist alone for a hand, so every post of a hand straddles the one thickness; the ring's own
// anchors for a midline post, so it stays on the ring's edges however those move.
[Serializable]
public class cage_post{
    public string name;         // as the design document calls it; the two posts of a finger ring share one
    public int[] anchor;        // joints the in-plane base is an affine combination of
    public float[] weight;      // their weights, summing to 1
    public Vector3 reach;       // baked in-plane offset from that base
    public Vector3 d;           // the axis the two vertices lie along
    public int[] d_lo_anchor;   // joints placing the -d end: it sits d_lo below the lowest of them along d
    public int[] d_hi_anchor;   // joints placing the +d end: d_hi above the highest
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
    public cage_post[] posts;       // the midline, then the hands, after the ring corners in the vertex order
    public int[] tris;              // indices into 4*rings.Length + 2*posts.Length vertices
}

#if UNITY_EDITOR
// Recipe values still being found. The inspector's tuning sliders write here and rebake, so the
// cage follows while the value is searched for; once settled, a value moves into the recipe table
// in bake() and into the design document, and its slider goes. Scene units, like the recipes.
[Serializable]
public class cage_tune{
    public float arm_hi = 0.05f;    // hi reach of both arm rings: how far their top edge clears the shoulder
    public float arm_outward_hi = 0.05f;    // outward of that top edge alone; negative draws it in over the trapezius
    public float arm_lo = 0f;               // lo reach of both arm rings: their bottom edge below the armpit
    public float arm_outward_lo = 0.05f;    // outward of that bottom edge alone; negative draws it into the armpit
    public float arm_hi_front = 0f, arm_hi_back = 0f;   // depth reach of the arm rings' top edge, across the trapezius
    public float arm_lo_front = 0f, arm_lo_back = 0f;   // and of their bottom edge, across the armpit
    public float crown_front = 0f;  // depth reach of the crown ring: the chest and belly (front) and the
    public float crown_back = 0f;   // shoulder blades (back) sit under the torso panel these two rings span
    public float hip_front = 0f;    // the same on the hip ring
    public float hip_back = 0f;
}
#endif

public static class cage{
    // Corner layout inside a ring: the s axis gives the silhouette side (hi/lo), the d axis the
    // front/back side. Vertex index is ring * 4 + corner.
    const int hi_front = 0, hi_back = 1, lo_back = 2, lo_front = 3;

    // The two ends of a post, along its plate axis. Vertex index is rings * 4 + post * 2 + end.
    const int post_hi = 0, post_lo = 1;

    // The cage control points for the given lengths, in rig root local space: the joint centers
    // reconstructed from those lengths with the baked rings re-placed on them. Lengths are native
    // (joint.localPosition.magnitude), keyed by joint name; joints nobody edits (fingers, toes)
    // fall back to their baked rest length, so an empty table yields the rest cage.
    public static Vector3[] points(IReadOnlyDictionary<string, float> lengths, cage_constants k){
        return control_points(k, joint_centers(lengths, k));
    }

    // Ring corners first, then post ends: the vertex order the topology tables are written against.
    static Vector3[] control_points(cage_constants k, Vector3[] jc){
        return ring_corners(k, jc).Concat(post_ends(k, jc)).ToArray();
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

            var plane_hi = r.n * (a_hi.Max(p => Vector3.Dot(p, r.n)) + r.along_hi);
            var plane_lo = r.n * (a_lo.Max(p => Vector3.Dot(p, r.n)) + r.along_lo);
            var edge_hi = r.s * (a_hi.Max(p => Vector3.Dot(p, r.s)) + r.s_hi);
            var edge_lo = r.s * (a_lo.Min(p => Vector3.Dot(p, r.s)) - r.s_lo);

            var a = a_hi.Concat(a_lo);
            var front = a.Max(p => Vector3.Dot(p, r.d));
            var back = a.Min(p => Vector3.Dot(p, r.d));

            verts[i * 4 + hi_front] = plane_hi + edge_hi + r.d * (front + r.hi_front);
            verts[i * 4 + hi_back] = plane_hi + edge_hi + r.d * (back - r.hi_back);
            verts[i * 4 + lo_back] = plane_lo + edge_lo + r.d * (back - r.lo_back);
            verts[i * 4 + lo_front] = plane_lo + edge_lo + r.d * (front + r.lo_front);
        }
        return verts;
    }

    // A post sits where its anchors put it across d, and each end along d at its own anchors'
    // coordinate -- not the post's own: a hand's posts all read the wrist, so the hand keeps one flat
    // back and palm, and a midline post reads its ring's anchors, so it stays on the ring's edges.
    static Vector3[] post_ends(cage_constants k, Vector3[] jc){
        var verts = new Vector3[k.posts.Length * 2];
        for(var i = 0; i < k.posts.Length; i++){
            var p = k.posts[i];
            var at = p.anchor.Select((j, a) => jc[j] * p.weight[a]).Aggregate((x, y) => x + y) + p.reach;
            var flat = at - p.d * Vector3.Dot(at, p.d);

            verts[i * 2 + post_hi] = flat + p.d * (p.d_hi_anchor.Max(j => Vector3.Dot(jc[j], p.d)) + p.d_hi);
            verts[i * 2 + post_lo] = flat + p.d * (p.d_lo_anchor.Min(j => Vector3.Dot(jc[j], p.d)) - p.d_lo);
        }
        return verts;
    }

#if UNITY_EDITOR
    // Ring slots. The topology tables index these directly. The torso meets the arms at the arm
    // rings and the legs at the hip ring; the elbow, wrist, knee and sole rings hang off those.
    const int crown = 0,
        arm_hi = 1, elbow_hi = 2, wrist_hi = 3,
        arm_lo = 4, elbow_lo = 5, wrist_lo = 6,
        hip = 7, knee = 8, sole = 9;

    // Midline stations that are not rings -- they carry a mid and nothing else -- on the spine
    // between the arm rings' top edges: the bottom of the neck's V, and the sternum level with the
    // armpits.
    const int neck = 10, sternum = 11;

    // A body control point: one silhouette edge of a ring, or the midline post its front and back
    // edges leave in the middle. hi/lo name the two sides of the silhouette axis, so an "hi" limb
    // ring is the one on the +side (character's left).
    enum edge{ hi, lo, mid }

    // Panel outlines as (ring, side) pairs, all traced in the same sense. Each is emitted twice:
    // once on the front corners, once reversed on the back. The torso, head and legs come as two
    // halves meeting on the midline, so an edit on one side stays on that side's half; the ladder
    // of a half starts on the silhouette and returns along the midline, one rung per station.
    static readonly (int ring, edge e)[][] panels = {
        // The torso, its top edge one arm of the neck's V, rungs level across the chest and belly.
        new[]{ (arm_hi, edge.hi), (arm_hi, edge.lo), (hip, edge.hi), (hip, edge.mid), (sternum, edge.mid), (neck, edge.mid) },
        new[]{ (neck, edge.mid), (sternum, edge.mid), (hip, edge.mid), (hip, edge.lo), (arm_lo, edge.lo), (arm_lo, edge.hi) },
        // The head and neck, from the V up to the crown.
        new[]{ (crown, edge.mid), (crown, edge.hi), (arm_hi, edge.hi), (neck, edge.mid) },
        new[]{ (neck, edge.mid), (arm_lo, edge.hi), (crown, edge.lo), (crown, edge.mid) },
        new[]{ (arm_hi, edge.hi), (elbow_hi, edge.hi), (elbow_hi, edge.lo), (arm_hi, edge.lo) },
        new[]{ (elbow_hi, edge.hi), (wrist_hi, edge.hi), (wrist_hi, edge.lo), (elbow_hi, edge.lo) },
        new[]{ (arm_lo, edge.lo), (elbow_lo, edge.lo), (elbow_lo, edge.hi), (arm_lo, edge.hi) },
        new[]{ (elbow_lo, edge.lo), (wrist_lo, edge.lo), (wrist_lo, edge.hi), (elbow_lo, edge.hi) },
        new[]{ (hip, edge.mid), (hip, edge.hi), (knee, edge.hi), (knee, edge.mid) },
        new[]{ (knee, edge.mid), (knee, edge.lo), (hip, edge.lo), (hip, edge.mid) },
        new[]{ (knee, edge.mid), (knee, edge.hi), (sole, edge.hi), (sole, edge.mid) },
        new[]{ (sole, edge.mid), (sole, edge.lo), (knee, edge.lo), (knee, edge.mid) },
    };

    // The silhouette boundary of those panels, traced in the same sense: every consecutive pair
    // spans a quad joining the front outline to the back. It used to be one closed loop, but a
    // wrist ring is where the arm hands over to a hand, whose own panels are the back of the hand
    // and the palm -- so the arm spends its front and back edges on panels and its top and bottom
    // here, and the hand does the opposite. No quad closes across a wrist, which breaks the loop
    // into three chains. The runs walking along one ring -- one edge, its midline post, the other
    // edge -- are its own rectangle in two quads: the shell caps there, at the crown and the soles.
    static readonly (int ring, edge e)[][] perimeter = {
        new[]{ (crown, edge.hi), (arm_hi, edge.hi), (elbow_hi, edge.hi), (wrist_hi, edge.hi) },
        new[]{ (wrist_hi, edge.lo), (elbow_hi, edge.lo), (arm_hi, edge.lo),
               (hip, edge.hi), (knee, edge.hi), (sole, edge.hi), (sole, edge.mid), (sole, edge.lo), (knee, edge.lo), (hip, edge.lo),
               (arm_lo, edge.lo), (elbow_lo, edge.lo), (wrist_lo, edge.lo) },
        new[]{ (wrist_lo, edge.hi), (elbow_lo, edge.hi), (arm_lo, edge.hi), (crown, edge.lo), (crown, edge.mid), (crown, edge.hi) },
    };

    // The five fingers, thumb first. A hand's silhouette axis runs from the thumb (+s) to the pinky
    // (-s), and the six palm control points interleave with them.
    static readonly string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

    // How far past the knuckle line a valley control point sits, in scene units, so the web between
    // two fingers falls inside the shell. Editable constant: no joint of the rig marks it.
    const float valley_reach = 0.01f;

    // How far below the hand's plate the wrist ring's palm side reaches, in scene units. It is the
    // same kind of slack as a recipe's reach, but the wrist ring takes its silhouette extent from
    // the hand rather than from measure(), so it belongs here: the forearm is far thicker than the
    // hand, and without it the arm panel pinches to the palm's thickness at the wrist. Editable.
    const float wrist_drop = 0.01f;

    // Extra girth for one finger ring, in scene units, across the palm plane -- the width read off
    // the back of the hand, which is the only one a finger ring has. Rings are numbered as the hand
    // is described: the branch ring a finger shares with its neighbours is 1, so ring 2 is the first
    // one standing on a joint of its own. hi is the thumb side, lo the pinky side. Editable; a ring
    // not listed keeps the width measured off the flesh.
    static readonly (string finger, int ring, float hi, float lo)[] finger_reach = {
        ("Index", 3, 0.001f, 0.001f),
        ("Middle", 2, 0f, 0.001f),
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
        public string name;
        public int[] anchor;
        public int[] wrap;          // subtree roots whose flesh the ring must enclose
        public Vector3 n, s, d;
        public bool terminal;
        public (float hi, float lo) front, back;    // extra depth reach past the flesh, in scene units, per
                                                    // silhouette edge -- the arm rings' two edges sit at
                                                    // different depths of the torso once tilted
        public float hi;            // the same on the +silhouette side: up on the arm, elbow and
                                    // wrist rings, the character's left on the others
        public float lo;            // and on the -silhouette side; negative draws that edge in
        public float outward_hi;    // the same along n, per edge: it moves that edge's plane out, or
        public float outward_lo;    // in when negative. The cross-section is still measured at the
                                    // anchor, so an edge moved along a limb keeps the girth it had
                                    // there. Unequal values tilt the ring, as the arm rings' hi edge
                                    // is drawn in to sit on the trapezius.
    }

    public static cage_constants bake(SkinnedMeshRenderer source, cage_tune tune){
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

        // The reach fields pull a panel out over flesh the rings themselves do not see, and belong
        // to whichever ring bounds that panel there. The torso panels split at the midline, so their
        // depth interpolates crown to hip: the chest and back are the crown and hip rings' business,
        // and depth reach on the arm rings would only bulge the side of the torso. Editable.
        var recipes = new recipe[10];
        recipes[crown] = new recipe{ name = "crown", anchor = js("Head"), wrap = js("Head"), n = up, s = side, d = depth, terminal = true, front = (tune.crown_front, tune.crown_front), back = (tune.crown_back, tune.crown_back) };
        recipes[arm_hi] = new recipe{ name = "L arm", anchor = js("LeftArm"), wrap = js("LeftShoulder"), n = side, s = up, d = depth, terminal = false, hi = tune.arm_hi, lo = tune.arm_lo, outward_hi = tune.arm_outward_hi, outward_lo = tune.arm_outward_lo, front = (tune.arm_hi_front, tune.arm_lo_front), back = (tune.arm_hi_back, tune.arm_lo_back) };
        recipes[elbow_hi] = new recipe{ name = "L elbow", anchor = js("LeftForeArm"), wrap = js("LeftArm"), n = side, s = up, d = depth, terminal = false, hi = 0.05f };
        // The wrist rings hand the arms over to the hands, which measure them: their extents are
        // overwritten below, since both need flesh windows the generic measure cannot express.
        recipes[wrist_hi] = new recipe{ name = "L wrist", anchor = js("LeftHand"), wrap = js("LeftHand"), n = side, s = up, d = depth, terminal = false };
        recipes[arm_lo] = new recipe{ name = "R arm", anchor = js("RightArm"), wrap = js("RightShoulder"), n = -side, s = up, d = depth, terminal = false, hi = tune.arm_hi, lo = tune.arm_lo, outward_hi = tune.arm_outward_hi, outward_lo = tune.arm_outward_lo, front = (tune.arm_hi_front, tune.arm_lo_front), back = (tune.arm_hi_back, tune.arm_lo_back) };
        recipes[elbow_lo] = new recipe{ name = "R elbow", anchor = js("RightForeArm"), wrap = js("RightArm"), n = -side, s = up, d = depth, terminal = false, hi = 0.05f };
        recipes[wrist_lo] = new recipe{ name = "R wrist", anchor = js("RightHand"), wrap = js("RightHand"), n = -side, s = up, d = depth, terminal = false };
        // The hip ring stands on the higher of the two hip joints (per side, so each edge follows
        // its own), and wraps whatever crosses that height -- pelvis and the top of the thighs.
        recipes[hip] = new recipe{ name = "hip", anchor = js("LeftUpLeg", "RightUpLeg"), wrap = js("Hips"), n = up, s = side, d = depth, terminal = false, front = (tune.hip_front, tune.hip_front), back = (tune.hip_back, tune.hip_back) };
        recipes[knee] = new recipe{ name = "knee", anchor = js("LeftLeg", "RightLeg"), wrap = js("LeftUpLeg", "RightUpLeg"), n = -up, s = side, d = depth, terminal = false, back = (0.1f, 0.1f) };
        recipes[sole] = new recipe{ name = "sole", anchor = js("LeftFoot", "LeftToeBase", "RightFoot", "RightToeBase"), wrap = js("LeftFoot", "RightFoot"), n = -up, s = side, d = depth, terminal = true };

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

            // A terminal ring's plane is pushed past all the flesh it wraps; a joint ring's stays on
            // its anchors.
            var past = r.terminal ? (wrap.Max(p => Vector3.Dot(p, r.n)) - plane) * (1f + margin) : 0f;
            return new cage_ring{
                name = r.name,
                anchor_hi = hi,
                anchor_lo = lo,
                n = r.n,
                s = r.s,
                d = r.d,
                along_hi = past + r.outward_hi / scale,
                along_lo = past + r.outward_lo / scale,
                s_lo = lo.Min(j => Vector3.Dot(rest[j], r.s)) - lo_s + r.lo / scale,
                s_hi = hi_s - hi.Max(j => Vector3.Dot(rest[j], r.s)) + r.hi / scale,
                hi_front = hi_d - anchors.Max(p => Vector3.Dot(p, r.d)) + r.front.hi / scale,
                lo_front = hi_d - anchors.Max(p => Vector3.Dot(p, r.d)) + r.front.lo / scale,
                hi_back = anchors.Min(p => Vector3.Dot(p, r.d)) - lo_d + r.back.hi / scale,
                lo_back = anchors.Min(p => Vector3.Dot(p, r.d)) - lo_d + r.back.lo / scale,
            };
        }

        var rings = recipes.Select(measure).ToArray();
        var posts = new List<cage_post>();
        var plates = new List<(int hi, int lo)[]>();
        var walls = new List<(int hi, int lo)[]>();

        // A post's two vertices, after all the ring corners.
        (int hi, int lo) pair(int p){
            var v = rings.Length * 4 + p * 2;
            return (v + post_hi, v + post_lo);
        }

        // The midline: one post wherever a front or back panel's rungs cross the middle, so the
        // torso, head and legs come as two halves. A post's ends take the depth anchors and reach of
        // the ring whose band it closes, so it stays level with that ring's edges however they move.
        var mids = new Dictionary<int, int>();
        int mid_post(string name, int[] anchor, Vector3 reach, int[] d_anchor, float d_lo, float d_hi){
            posts.Add(new cage_post{
                name = name, anchor = anchor, weight = anchor.Select(_ => 1f / anchor.Length).ToArray(), reach = reach,
                d = depth, d_lo_anchor = d_anchor, d_hi_anchor = d_anchor, d_lo = d_lo, d_hi = d_hi,
            });
            return posts.Count - 1;
        }

        // On a ring across the body: exactly at the midpoint of its edges, anchored on the mean of
        // the joints that place them and offset by whatever the rest midpoint is off that mean.
        var rest_corners = ring_corners(new cage_constants{ rings = rings }, rest);
        void midline(int slot, params string[] names){
            var r = rings[slot];
            var anchor = js(names);
            var mean = anchor.Aggregate(Vector3.zero, (a, j) => a + rest[j]) / anchor.Length;
            var mid = (rest_corners[slot * 4 + hi_front] + rest_corners[slot * 4 + lo_front]) * 0.5f;
            var reach = mid - mean - r.d * Vector3.Dot(mid - mean, r.d);
            mids[slot] = mid_post(r.name + " mid", anchor, reach, r.anchor_hi.Concat(r.anchor_lo).Distinct().ToArray(),
                (r.hi_back + r.lo_back) * 0.5f, (r.hi_front + r.lo_front) * 0.5f);
        }
        midline(crown, "Head");
        // Between the arm rings' top edges: the bottom of the neck's V on the Neck joint itself, and
        // level with the armpits the sternum, on Spine3. Each closes a rung of the arm rings -- the
        // top edges, the bottom edges -- so it takes that edge's depth, spread over both shoulders.
        var shoulders = js("LeftArm", "RightArm");
        var arm = rings[arm_hi];
        mids[neck] = mid_post("neck mid", js("Neck"), Vector3.zero, shoulders, arm.hi_back, arm.hi_front);
        mids[sternum] = mid_post("sternum mid", js("Spine3"), Vector3.zero, shoulders, arm.lo_back, arm.lo_front);
        midline(hip, "LeftUpLeg", "RightUpLeg");
        midline(knee, "LeftLeg", "RightLeg");
        // The toes, not the ankles: they are what the sole plane stands on.
        midline(sole, "LeftToeBase", "RightToeBase");

        // A body control point as a vertex pair, front then back: a ring's silhouette edge, or the
        // midline post between its two edges.
        (int hi, int lo) ends((int ring, edge e) c){
            return c.e == edge.mid ? pair(mids[c.ring]) : (corner(c.ring, c.e, true), corner(c.ring, c.e, false));
        }

        // One hand, past its wrist ring. n points out along the arm, s runs from the thumb (+) to
        // the pinky (-), and d is the plate -- the back of the hand and the palm. mirror flips the
        // trace for the hand whose frame comes out left handed against the ring frames. tag is the
        // side as the design document abbreviates it, prefixed to every name of this hand.
        void hand(string prefix, string tag, int slot, Vector3 n, bool mirror){
            var s = depth;
            var d = up;
            var wrist = index[prefix];
            var skin = subtree(wrist, parent).SelectMany(j => flesh[j]).ToArray();

            // One thickness for the whole hand, measured over all of its flesh: every post straddles
            // it, which is what keeps the side panels axis aligned and equally tall.
            var (plate_lo, plate_hi) = inflate(skin.Min(p => Vector3.Dot(p, d)), skin.Max(p => Vector3.Dot(p, d)));
            var seat = Vector3.Dot(rest[wrist], d);

            int add(string name, int[] anchor, float[] weight, Vector3 reach){
                posts.Add(new cage_post{
                    name = name, anchor = anchor, weight = weight, reach = reach,
                    d = d, d_lo_anchor = new[]{ wrist }, d_hi_anchor = new[]{ wrist }, d_lo = seat - plate_lo, d_hi = plate_hi - seat,
                });
                return posts.Count - 1;
            }

            // The wrist ring takes the hand's plate on its silhouette axis, and on its depth axis
            // the palm at the wrist -- measured within half a metacarpal of the ring plane, since
            // its own slab is scaled to the forearm and would take the width across spread fingers.
            var slice = skin.Where(p => Mathf.Abs(Vector3.Dot(p - rest[wrist], n)) <= rest_len[index[prefix + "Middle1"]] * 0.5f);
            var (palm_lo, palm_hi) = inflate(slice.Min(p => Vector3.Dot(p, s)), slice.Max(p => Vector3.Dot(p, s)));
            // Only the ring drops; the hand's own posts keep the plate, so the palm slopes up to it
            // from the wrist instead of the whole hand fattening.
            rings[slot].s_hi = plate_hi - seat;
            rings[slot].s_lo = seat - plate_lo + wrist_drop / scale;
            rings[slot].hi_front = rings[slot].lo_front = palm_hi - Vector3.Dot(rest[wrist], s);
            rings[slot].hi_back = rings[slot].lo_back = Vector3.Dot(rest[wrist], s) - palm_lo;

            // The six control points that carve the palm outline into finger branches. The thumb
            // and pinky ends come from the hand's own width; the four valleys sit halfway between
            // neighbouring finger roots, pushed away from the wrist past the knuckle line.
            var (wide_lo, wide_hi) = inflate(skin.Min(p => Vector3.Dot(p, s)), skin.Max(p => Vector3.Dot(p, s)));
            var thumb = index[prefix + "Thumb2"];
            var pinky = index[prefix + "Pinky1"];

            var cp = new int[6];
            cp[0] = add($"{tag} thumb out", new[]{ thumb }, new[]{ 1f }, s * (wide_hi - Vector3.Dot(rest[thumb], s)));
            for(var f = 0; f < 4; f++){
                // The thumb branches off at its own second joint, the rest at their roots.
                var a = f == 0 ? thumb : index[prefix + fingers[f] + "1"];
                var b = index[prefix + fingers[f + 1] + "1"];
                var span = (rest[a] + rest[b]) * 0.5f - rest[wrist];
                var away = (span - d * Vector3.Dot(span, d)).normalized;
                cp[f + 1] = add($"{tag} {fingers[f].ToLower()}|{fingers[f + 1].ToLower()}", new[]{ a, b }, new[]{ 0.5f, 0.5f }, away * (valley_reach / scale));
            }
            cp[5] = add($"{tag} pinky out", new[]{ pinky }, new[]{ 1f }, s * (wide_lo - Vector3.Dot(rest[pinky], s)));

            // Rings up one finger, past the branch ring it shares with its neighbours: one on every
            // joint out from the second, then one more on a virtual end bone, since the rig stops at
            // the last phalanx. Each ring straddles its own bone direction rather than the s axis,
            // so a splayed finger is still enclosed. The thumb is one ring short: its branch ring
            // already sits at the knuckle.
            (int hi, int lo)[] climb(int f){
                var last = index[prefix + fingers[f] + "3"];
                var tip = subtree(last, parent).SelectMany(j => flesh[j]);
                var over = tip.Max(p => Vector3.Dot(p - rest[last], dir[last])) * (1f + margin);

                var joints = f == 0
                    ? new[]{ (j: last, past: 0f) }
                    : new[]{ (j: index[prefix + fingers[f] + "2"], past: 0f), (j: last, past: 0f) };

                return joints.Append((j: last, past: over / rest_len[last])).ToArray().Select((e, i) => {
                    var along = (dir[e.j] - d * Vector3.Dot(dir[e.j], d)).normalized;
                    var perp = s * Vector3.Dot(along, n) - n * Vector3.Dot(along, s);
                    var at = Vector3.Dot(rest[e.j] + dir[e.j] * (rest_len[e.j] * e.past), perp);

                    var meat = subtree(e.j, parent).SelectMany(j => flesh[j]);
                    var (r_lo, r_hi) = inflate(meat.Min(p => Vector3.Dot(p, perp)), meat.Max(p => Vector3.Dot(p, perp)));

                    // The branch ring is 1 and this chain starts at 2, which is how the table names
                    // these rings; an unlisted ring finds no entry and reads back zeroes.
                    var extra = finger_reach.FirstOrDefault(x => x.finger == fingers[f] && x.ring == i + 2);

                    // A ring on a virtual end bone extrapolates past its joint, so it keeps
                    // following that phalanx when the phalanx is lengthened.
                    var anchor = e.past > 0f ? new[]{ e.j, parent[e.j] } : new[]{ e.j };
                    var weight = e.past > 0f ? new[]{ 1f + e.past, -e.past } : new[]{ 1f };
                    var name = $"{tag} {fingers[f].ToLower()} {i + 2}";
                    return (hi: add(name, anchor, weight, perp * (r_hi - at + extra.hi / scale)),
                            lo: add(name, anchor, weight, perp * (r_lo - at - extra.lo / scale)));
                }).ToArray();
            }

            var climbs = Enumerable.Range(0, fingers.Length).Select(climb).ToArray();

            // The wrist ring's own two posts, as the hand reads them: its silhouette sides are the
            // plate, its depth sides the thumb and pinky ends.
            (int hi, int lo) wrist_end(bool front){
                return (corner(slot, edge.hi, front), corner(slot, edge.lo, front));
            }

            // Out from a control point along one side of a finger and back down the other.
            IEnumerable<(int hi, int lo)> finger(int f){
                return new[]{ pair(cp[f]) }
                    .Concat(climbs[f].Select(r => pair(r.hi)))
                    .Concat(climbs[f].Reverse().Select(r => pair(r.lo)));
            }

            var digits = Enumerable.Range(0, fingers.Length);
            var outline = new[]{ wrist_end(true) }
                .Concat(digits.SelectMany(finger))
                .Append(pair(cp[5]))
                .Append(wrist_end(false))
                .ToArray();

            // The back of the hand and the palm are one polygon spanning the wrist and all six
            // control points -- an octagon -- plus one polygon per finger.
            var loops = new[]{ new[]{ wrist_end(true) }.Concat(cp.Select(pair)).Append(wrist_end(false)).ToArray() }
                .Concat(digits.Select(f => finger(f).Append(pair(cp[f + 1])).ToArray()));

            plates.AddRange(mirror ? loops.Select(l => l.Reverse().ToArray()) : loops);
            walls.Add(mirror ? outline.Reverse().ToArray() : outline);
        }

        plates.AddRange(panels.Select(p => p.Select(ends).ToArray()));
        walls.AddRange(perimeter.Select(c => c.Select(ends).ToArray()));
        hand("LeftHand", "L", wrist_hi, side, true);
        hand("RightHand", "R", wrist_lo, -side, false);

        var k = new cage_constants{
            joint_name = bones.Select(t => t.name).ToArray(),
            joint_parent = parent,
            joint_dir = dir,
            joint_rest_len = rest_len,
            rings = rings,
            posts = posts.ToArray(),
            tris = topology(plates, walls),
        };

        // The panels are traced in one consistent sense, but which sense faces outward depends on
        // the rig's axes. The enclosed volume settles it: the root sits inside the cage.
        if(volume(control_points(k, rest), k.tris) < 0.0){
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

    // Fixed topology. Every face of the shell is either a plate -- a closed outline of posts, filled
    // once on each of their two vertices -- or a wall, a chain of posts spanning one quad per
    // consecutive pair. A post is the vertex pair one control point owns: a ring's silhouette side
    // for the body, a cage_post for a hand.
    static int[] topology(IEnumerable<(int hi, int lo)[]> plates, IEnumerable<(int hi, int lo)[]> walls){
        var tris = new List<int>();

        foreach(var plate in plates){
            strip(tris, plate.Select(e => e.hi));
            strip(tris, plate.Reverse().Select(e => e.lo));
        }

        foreach(var wall in walls){
            for(var i = 0; i + 1 < wall.Length; i++){
                strip(tris, new[]{ wall[i + 1].hi, wall[i].hi, wall[i].lo, wall[i + 1].lo });
            }
        }

        // Every directed edge appears exactly once and its opposite exists: the shell is closed and
        // every face is traced the same way round. This is what catches a mistraced table, and what
        // holds the arm and the hand to opposite senses where they share a wrist rectangle.
        var edges = Enumerable.Range(0, tris.Count / 3)
            .SelectMany(t => Enumerable.Range(0, 3).Select(e => (a: tris[t * 3 + e], b: tris[t * 3 + (e + 1) % 3])))
            .ToArray();
        Debug.Assert(edges.Distinct().Count() == edges.Length, "cage: faces overlap or are traced against each other");
        Debug.Assert(edges.All(e => edges.Contains((e.b, e.a))), "cage: faces do not close the shell");
        return tris.ToArray();
    }

    static int corner(int ring, edge e, bool front){
        Debug.Assert(e != edge.mid && ring <= sole, "cage: not a ring corner");
        return ring * 4 + (e == edge.hi ? (front ? hi_front : hi_back) : (front ? lo_front : lo_back));
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

    // Debug view: the vertices behind each name the constants carry, ring corners and post ends
    // alike. The two posts of a finger ring share a name, so a finger ring reads as one group too.
    public static IEnumerable<(string name, int[] verts)> named(cage_constants k){
        return k.rings.SelectMany((r, i) => Enumerable.Range(i * 4, 4).Select(v => (r.name, v)))
            .Concat(k.posts.SelectMany((p, i) => Enumerable.Range(k.rings.Length * 4 + i * 2, 2).Select(v => (p.name, v))))
            .GroupBy(e => e.name, e => e.v)
            .Select(g => (g.Key, g.ToArray()));
    }

    // Debug view: which joint places what, as a line from the joint center to the feature it
    // places -- each silhouette edge of a ring by its own anchors, a post group by its center. The
    // weight is the post's affine weight; a ring on a virtual end bone reads (1+f, -f) on its two.
    public static IEnumerable<(string name, string joint, float weight, Vector3 from, Vector3 to)> anchors(IReadOnlyDictionary<string, float> lengths, cage_constants k){
        var jc = joint_centers(lengths, k);
        var v = control_points(k, jc);

        var rings = k.rings.SelectMany((r, i) => {
            var hi = (v[i * 4 + hi_front] + v[i * 4 + hi_back]) * 0.5f;
            var lo = (v[i * 4 + lo_front] + v[i * 4 + lo_back]) * 0.5f;
            return r.anchor_hi.Select(j => (name: r.name, joint: k.joint_name[j], weight: 1f, from: jc[j], to: hi))
                .Concat(r.anchor_lo.Select(j => (name: r.name, joint: k.joint_name[j], weight: 1f, from: jc[j], to: lo)));
        });

        var posts = k.posts.Select((p, i) => (p, i)).GroupBy(e => e.p.name).SelectMany(g => {
            var ends = g.SelectMany(e => new[]{ v[k.rings.Length * 4 + e.i * 2 + post_hi], v[k.rings.Length * 4 + e.i * 2 + post_lo] }).ToArray();
            var center = ends.Aggregate((x, y) => x + y) / ends.Length;
            return g.SelectMany(e => e.p.anchor.Select((j, a) => (j, w: e.p.weight[a]))).Distinct()
                .Select(e => (name: g.Key, joint: k.joint_name[e.j], weight: e.w, from: jc[e.j], to: center));
        });

        return rings.Concat(posts);
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
