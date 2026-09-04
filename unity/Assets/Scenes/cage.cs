using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Bone-length driven cage generation.
//
// The body is deliberately coarse: seventeen rectangular rings -- crown, head, two arms, two
// elbows, two wrists, three across the spine, two knees, two ankles, two toes -- whose corners
// are stitched into flat panels. Posts on the midline (see cage_post) -- one per ring across the
// body, plus the bottom of the neck's V and the sternum -- split the torso and head panels into a
// left and a right half; the arm rings' top edges are drawn in to meet at the neck post, so the V
// parts the torso from the head. A post on top of each upper arm, where the deltoid ends, makes a
// second V with the arm ring at the armpit: the wedge between them is the shoulder, and the upper
// arm hangs from the tilted pair armpit-deltoid. Below the spine ring the pelvis branches into the legs the way a
// palm branches into fingers: three posts -- the crotch and the two outer hips -- span a pentagon
// with the spine ring, and each leg hangs from the tilted pair crotch-hip, meeting the other at
// the crotch. Down a leg the ring frames turn with it: the ankle ring leans back through the
// heel, the toe ring stands upright across the ball of the foot, so the front panel runs shin to
// instep and the back panel calf to heel to sole; the toes end on a post pair standing on a
// virtual end bone, a fingertip's. A front and back silhouette plus one quad per silhouette edge
// closes it; the quads along the crown ring and the tip posts themselves cap the shell there.
// Past each wrist the hand is resolved finger by finger, out of posts rather than rings: 236
// vertices, 468 triangles in all.
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
// shared by both limbs (as the knees and soles once were) therefore tilts to track both legs,
// rather than being pinned along n by whichever leg is longer. A ring on a single limb -- every
// ring now -- lists that limb's joints on both sides and stays axis aligned. The two depth sides
// likewise each have their own anchors, normally all of the ring's; the toe ring hangs its bottom
// on the Foot joint instead, so the sole stays level with the heel.
[Serializable]
public class cage_ring{
    public string name;         // as the design document calls it; the debug view's tag
    public int[] anchor_hi;     // joints placing the +s edge (indices into cage_constants.joint_name)
    public int[] anchor_lo;     // joints placing the -s edge
    public int[] d_hi_anchor;   // joints placing the front side: it sits hi_front / lo_front past the farthest of them along d
    public int[] d_lo_anchor;   // joints placing the back side: hi_back / lo_back past the nearest
    public Vector3 n;           // ring normal, pointing away from the body
    public Vector3 s;           // in-plane axis the front/back silhouette runs along
    public Vector3 d;           // in-plane axis separating the front and back panels
    public float along_hi, along_lo;    // each edge's offset past its farthest anchor, along n; unequal on a tilted ring
    public int[] hold_hi, hold_lo;      // posts each edge stays level with along n at the very least, so a
                                        // ring whose anchors sink past them opens into a V rather than
                                        // crossing them; empty when nothing holds that edge
    public float s_lo, s_hi;    // reach beyond the anchors' span, on the -s and +s side
    public float hi_front, lo_front;    // each corner's reach along d past its depth anchors:
    public float hi_back, lo_back;      // front past their max, back past their min
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

    // Vertex pairs: the edges the topology tables declare, which tris alone cannot give back. Each
    // consecutive pair of posts along a plate's outline or a wall's chain is a ring of the shell --
    // the tilted hip ring is the crotch post beside an outer hip post, the shoulder ring is a
    // deltoid post beside an arm ring's armpit edge -- while what the ladder adds to fill the panels
    // between them, its rungs and the diagonal splitting each quad, no row of any table names.
    // Triangulating loses the difference, so the pairs are kept. Read by the debug wire (frame).
    public int[] grid;
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
    public float head_tilt = 25f;       // degrees the head ring's plane leans forward about the side axis, chin down
    public float head_offset = 0.023f;  // how far above the Head joint that plane sits, along its own normal
    public float head_front = 0f, head_back = 0f;   // depth reach of the head ring: chin and occiput
    public float neck_front = 0f;   // how far ahead of the arm rings' top edge the V's floor stands, so
                                    // drawing that edge in over the chest does not drag the throat in with it
    public float sternum_front = 0f;    // the same on the rung below, at the armpits: how far ahead of the
                                        // arm rings' bottom edge the sternum stands
    public float crown_front = 0f;  // depth reach of the crown ring: the chest and belly (front) and the
    public float crown_back = 0f;   // shoulder blades (back) sit under the torso panel these two rings span
    public float spine_front = 0f;  // the same on the spine ring, the torso panel's bottom edge
    public float spine_back = 0f;
    public float spine1_front = 0f, spine1_back = 0f;   // and on the two spine rings above it, the belly
    public float spine2_front = 0f, spine2_back = 0f;   // and the lower chest
    public float crotch_drop = 0.15f;   // how far below the Hips joint the crotch post sits, along up
    public float hip_out = 1f;          // ratio: an outer hip post is this many crotch->UpLeg spans past its UpLeg
    public float pelvis_front = 0f;     // depth reach of the three pelvis posts past the pelvis flesh:
    public float pelvis_back = 0f;      // the belly and pubis (front), the buttocks (back)
    public float knee_out = 0f;         // reach of both knee rings' outer edge, away from the other leg
    public float knee_back = 0.1f;      // and of their back edge, past the hamstring and calf
    public float ankle_tilt = 45f;      // degrees the ankle rings' plane leans back from horizontal about the side axis: heel down, instep up
    public float ankle_front = 0f;      // depth reach of the ankle rings along their tilted d: up the instep (front),
    public float ankle_back = 0f;       // down behind the heel (back) -- which is also the height the sole is levelled to
    public float delt_along = 0.4f;     // ratio: where along the upper arm (Arm -> ForeArm) the deltoid post stands
    public float delt_up = 0f;          // its reach above the upper arm's flesh there
    public float elbow_hi = 0.05f;      // hi reach of both elbow rings: how far their top edge clears the elbow
    public float wrist_thumb = 0f;      // reach of both wrist rings across the palm, past the measured width: thumb side
    public float wrist_pinky = 0f;      // and pinky side; negative draws the ring in over the wrist
    public float thumb_out = 0f;        // reach of the palm octagon's outer posts past the hand's width: the thumb side
    public float pinky_out = 0f;        // and the pinky side
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
        // Posts first: a post reads nothing but the joint centers, while a ring edge can be held
        // level with one, so the dependency runs one way and no cycle is possible.
        var posts = post_ends(k, jc);
        return ring_corners(k, jc, posts).Concat(posts).ToArray();
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

    // The ring axes are orthonormal, so summing the three components rebuilds a corner exactly.
    // Each silhouette edge is placed along n by its own anchors; the depth extent is shared by both
    // edges, which keeps the four corners planar however far the two edges drift apart.
    static Vector3[] ring_corners(cage_constants k, Vector3[] jc, Vector3[] held){
        var verts = new Vector3[k.rings.Length * 4];
        for(var i = 0; i < k.rings.Length; i++){
            var r = k.rings[i];
            var a_hi = r.anchor_hi.Select(j => jc[j]).ToArray();
            var a_lo = r.anchor_lo.Select(j => jc[j]).ToArray();

            // An edge sits at its own anchors plus its reach, but no nearer along n than the posts
            // holding it -- both ends of such a post count, so a tilted one holds by its far end.
            // With nothing holding it the anchors' own reach stands.
            float outermost(int[] hold, float reach){
                return hold.Aggregate(reach, (m, p) => Mathf.Max(m, Mathf.Max(
                    Vector3.Dot(held[p * 2 + post_hi], r.n), Vector3.Dot(held[p * 2 + post_lo], r.n))));
            }

            var plane_hi = r.n * outermost(r.hold_hi, a_hi.Max(p => Vector3.Dot(p, r.n)) + r.along_hi);
            var plane_lo = r.n * outermost(r.hold_lo, a_lo.Max(p => Vector3.Dot(p, r.n)) + r.along_lo);
            var edge_hi = r.s * (a_hi.Max(p => Vector3.Dot(p, r.s)) + r.s_hi);
            var edge_lo = r.s * (a_lo.Min(p => Vector3.Dot(p, r.s)) - r.s_lo);

            var front = r.d_hi_anchor.Max(j => Vector3.Dot(jc[j], r.d));
            var back = r.d_lo_anchor.Min(j => Vector3.Dot(jc[j], r.d));

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
    // rings and the pelvis at the spine ring, with a ring on each spine joint between; the elbow
    // and wrist rings hang off the arms, the knee, ankle and toe rings off the pelvis posts.
    const int crown = 0,
        arm_hi = 1, elbow_hi = 2, wrist_hi = 3,
        arm_lo = 4, elbow_lo = 5, wrist_lo = 6,
        spine = 7, spine1 = 8, spine2 = 9,
        knee_hi = 10, ankle_hi = 11, toe_hi = 12,
        knee_lo = 13, ankle_lo = 14, toe_lo = 15,
        head = 16;

    // Stations that are not rings but posts. neck and sternum carry a mid and nothing else, on the
    // spine between the arm rings' top edges: the bottom of the neck's V, and the sternum level
    // with the armpits. hip is the pelvis: its hi and lo are the outer hip posts, its mid the
    // crotch, so crotch-hip reads as a tilted ring the way a finger's branch ring is two palm posts.
    // The tips are the ends of the toes, a post on each side standing on a virtual end bone. delt
    // is one post on top of the upper arm, where the deltoid ends: with the arm ring's bottom
    // (armpit) edge it is the ring the upper arm hangs from, so the station has a hi and no lo.
    const int neck = 17, sternum = 18, hip = 19, tip_hi = 20, tip_lo = 21, delt_hi = 22, delt_lo = 23;

    // A body control point: one silhouette edge of a ring, or the midline post its front and back
    // edges leave in the middle. hi/lo name the two sides of the silhouette axis, so an "hi" limb
    // ring is the one on the +side (character's left).
    enum edge{ hi, lo, mid }

    // Panel outlines as (ring, side) pairs, all traced in the same sense. Each is emitted twice:
    // once on the front corners, once reversed on the back. The torso, head and pelvis come as two
    // halves meeting on the midline, so an edit on one side stays on that side's half; the ladder
    // of a half starts on the silhouette and returns along the midline, one rung per station.
    static readonly (int ring, edge e)[][] panels = {
        // The torso, its top edge one arm of the neck's V, a rung level across the body at the
        // sternum and at every spine ring.
        new[]{ (arm_hi, edge.hi), (arm_hi, edge.lo), (spine2, edge.hi), (spine1, edge.hi), (spine, edge.hi),
               (spine, edge.mid), (spine1, edge.mid), (spine2, edge.mid), (sternum, edge.mid), (neck, edge.mid) },
        new[]{ (neck, edge.mid), (sternum, edge.mid), (spine2, edge.mid), (spine1, edge.mid), (spine, edge.mid),
               (spine, edge.lo), (spine1, edge.lo), (spine2, edge.lo), (arm_lo, edge.lo), (arm_lo, edge.hi) },
        // The neck, from the V up to the head ring, and the head, from there to the crown. The neck
        // panel is a quad, and where its crease runs matters: the ladder joins the outline's first
        // control point to its third (see strip), so tracing from the jaw's own corner rather than
        // from the midline folds it along the jaw instead of from the seam to the chin. Shorten the
        // neck and the jaw sinks past the seam, twisting this quad whichever way it is cut; the
        // crease along the jaw is the one that does not then cut into the head panels. `[N3]`
        new[]{ (head, edge.hi), (arm_hi, edge.hi), (neck, edge.mid), (head, edge.mid) },
        new[]{ (arm_lo, edge.hi), (head, edge.lo), (head, edge.mid), (neck, edge.mid) },
        new[]{ (crown, edge.mid), (crown, edge.hi), (head, edge.hi), (head, edge.mid) },
        new[]{ (head, edge.mid), (head, edge.lo), (crown, edge.lo), (crown, edge.mid) },
        // The shoulder: the wedge between the arm ring and the deltoid ring, which share the armpit
        // edge -- a triangle on each face. Then the upper arm from the deltoid ring to the elbow.
        new[]{ (arm_hi, edge.hi), (delt_hi, edge.hi), (arm_hi, edge.lo) },
        new[]{ (arm_lo, edge.lo), (delt_lo, edge.hi), (arm_lo, edge.hi) },
        new[]{ (delt_hi, edge.hi), (elbow_hi, edge.hi), (elbow_hi, edge.lo), (arm_hi, edge.lo) },
        new[]{ (elbow_hi, edge.hi), (wrist_hi, edge.hi), (wrist_hi, edge.lo), (elbow_hi, edge.lo) },
        new[]{ (arm_lo, edge.lo), (elbow_lo, edge.lo), (elbow_lo, edge.hi), (delt_lo, edge.hi) },
        new[]{ (elbow_lo, edge.lo), (wrist_lo, edge.lo), (wrist_lo, edge.hi), (elbow_lo, edge.hi) },
        // The pelvis: the pentagon spine.hi - L hip - crotch - R hip - spine.lo, the back of the
        // hand this palm is, split on the midline like the torso above it.
        new[]{ (spine, edge.mid), (spine, edge.hi), (hip, edge.hi), (hip, edge.mid) },
        new[]{ (hip, edge.mid), (hip, edge.lo), (spine, edge.lo), (spine, edge.mid) },
        // Each leg from its tilted hip ring -- the crotch and its outer hip -- down to its own knee,
        // ankle, toe and tip; the two thighs share only the crotch. The frames turn down the leg,
        // so the front panel is the shin, then the instep, then the top of the toes, and the back
        // panel the calf, the heel, the sole.
        new[]{ (hip, edge.mid), (hip, edge.hi), (knee_hi, edge.hi), (knee_hi, edge.lo) },
        new[]{ (knee_lo, edge.hi), (knee_lo, edge.lo), (hip, edge.lo), (hip, edge.mid) },
        new[]{ (knee_hi, edge.lo), (knee_hi, edge.hi), (ankle_hi, edge.hi), (ankle_hi, edge.lo) },
        new[]{ (ankle_lo, edge.hi), (ankle_lo, edge.lo), (knee_lo, edge.lo), (knee_lo, edge.hi) },
        new[]{ (ankle_hi, edge.lo), (ankle_hi, edge.hi), (toe_hi, edge.hi), (toe_hi, edge.lo) },
        new[]{ (toe_lo, edge.hi), (toe_lo, edge.lo), (ankle_lo, edge.lo), (ankle_lo, edge.hi) },
        new[]{ (toe_hi, edge.lo), (toe_hi, edge.hi), (tip_hi, edge.hi), (tip_hi, edge.lo) },
        new[]{ (tip_lo, edge.hi), (tip_lo, edge.lo), (toe_lo, edge.lo), (toe_lo, edge.hi) },
    };

    // The silhouette boundary of those panels, traced in the same sense: every consecutive pair
    // spans a quad joining the front outline to the back. It used to be one closed loop, but a
    // wrist ring is where the arm hands over to a hand, whose own panels are the back of the hand
    // and the palm -- so the arm spends its front and back edges on panels and its top and bottom
    // here, and the hand does the opposite. No quad closes across a wrist, which breaks the loop
    // into three chains. The runs walking along one station -- edge to edge, through the midline
    // post on the crown, post to post at a tip -- are its own rectangle: the shell caps there, at
    // the crown and the toes. The run down one leg's inner side, through the crotch and up the
    // other is the wall between the thighs.
    static readonly (int ring, edge e)[][] perimeter = {
        new[]{ (crown, edge.hi), (head, edge.hi), (arm_hi, edge.hi), (delt_hi, edge.hi), (elbow_hi, edge.hi), (wrist_hi, edge.hi) },
        new[]{ (wrist_hi, edge.lo), (elbow_hi, edge.lo), (arm_hi, edge.lo), (spine2, edge.hi), (spine1, edge.hi), (spine, edge.hi),
               (hip, edge.hi), (knee_hi, edge.hi), (ankle_hi, edge.hi), (toe_hi, edge.hi), (tip_hi, edge.hi),
               (tip_hi, edge.lo), (toe_hi, edge.lo), (ankle_hi, edge.lo), (knee_hi, edge.lo),
               (hip, edge.mid),
               (knee_lo, edge.hi), (ankle_lo, edge.hi), (toe_lo, edge.hi), (tip_lo, edge.hi),
               (tip_lo, edge.lo), (toe_lo, edge.lo), (ankle_lo, edge.lo), (knee_lo, edge.lo), (hip, edge.lo),
               (spine, edge.lo), (spine1, edge.lo), (spine2, edge.lo), (arm_lo, edge.lo), (elbow_lo, edge.lo), (wrist_lo, edge.lo) },
        new[]{ (wrist_lo, edge.hi), (elbow_lo, edge.hi), (delt_lo, edge.hi), (arm_lo, edge.hi), (head, edge.lo), (crown, edge.lo), (crown, edge.mid), (crown, edge.hi) },
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

    // How a ring is fitted to the flesh it wraps, n pointing away from the body. cap: pushed past
    // all of it, its rectangle capping the shell. joint: on its anchors, taking the cross-section
    // of the flesh crossing that plane. split: on its anchors plus the outward offset, taking the
    // extents of all the flesh beyond that plane -- what the panels past it must hold.
    enum fit{ joint, cap, split }

    class recipe{
        public string name;
        public int[] anchor;
        public int[] wrap;          // subtree roots whose flesh the ring must enclose
        public Vector3 n, s, d;
        public fit kind;
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
        // depth interpolates crown to spine: the chest and back are the crown and spine rings'
        // business, and depth reach on the arm rings would only bulge the side of the torso. Editable.
        var recipes = new recipe[17];
        recipes[crown] = new recipe{ name = "crown", anchor = js("Head"), wrap = js("Head"), n = up, s = side, d = depth, kind = fit.cap, front = (tune.crown_front, tune.crown_front), back = (tune.crown_back, tune.crown_back) };
        // The head ring parts the head from the neck. The chin hangs ahead of and below the skull
        // base, so the parting plane leans forward about the side axis: its frame is up and depth
        // turned by that tilt, and it sits a little above the Head joint along its own normal.
        var tilt = tune.head_tilt * Mathf.Deg2Rad;
        recipes[head] = new recipe{ name = "head", anchor = js("Head"), wrap = js("Head"), n = Mathf.Cos(tilt) * up + Mathf.Sin(tilt) * depth, s = side, d = Mathf.Cos(tilt) * depth - Mathf.Sin(tilt) * up, kind = fit.split, outward_hi = tune.head_offset, outward_lo = tune.head_offset, front = (tune.head_front, tune.head_front), back = (tune.head_back, tune.head_back) };
        recipes[arm_hi] = new recipe{ name = "L arm", anchor = js("LeftArm"), wrap = js("LeftShoulder"), n = side, s = up, d = depth, kind = fit.joint, hi = tune.arm_hi, lo = tune.arm_lo, outward_hi = tune.arm_outward_hi, outward_lo = tune.arm_outward_lo, front = (tune.arm_hi_front, tune.arm_lo_front), back = (tune.arm_hi_back, tune.arm_lo_back) };
        recipes[elbow_hi] = new recipe{ name = "L elbow", anchor = js("LeftForeArm"), wrap = js("LeftArm"), n = side, s = up, d = depth, kind = fit.joint, hi = tune.elbow_hi };
        // The wrist rings hand the arms over to the hands, which measure them: their extents are
        // overwritten below, since both need flesh windows the generic measure cannot express.
        recipes[wrist_hi] = new recipe{ name = "L wrist", anchor = js("LeftHand"), wrap = js("LeftHand"), n = side, s = up, d = depth, kind = fit.joint };
        recipes[arm_lo] = new recipe{ name = "R arm", anchor = js("RightArm"), wrap = js("RightShoulder"), n = -side, s = up, d = depth, kind = fit.joint, hi = tune.arm_hi, lo = tune.arm_lo, outward_hi = tune.arm_outward_hi, outward_lo = tune.arm_outward_lo, front = (tune.arm_hi_front, tune.arm_lo_front), back = (tune.arm_hi_back, tune.arm_lo_back) };
        recipes[elbow_lo] = new recipe{ name = "R elbow", anchor = js("RightForeArm"), wrap = js("RightArm"), n = -side, s = up, d = depth, kind = fit.joint, hi = tune.elbow_hi };
        recipes[wrist_lo] = new recipe{ name = "R wrist", anchor = js("RightHand"), wrap = js("RightHand"), n = -side, s = up, d = depth, kind = fit.joint };
        // The spine ring is the torso panel's bottom edge, level across the Spine joint; it wraps
        // whatever crosses that height, so the waist. The pelvis below it is posts, not a ring. The
        // two rings above it, on Spine1 and Spine2, section the belly and the lower chest the same
        // way, so the torso panel gets a rung at every spine joint up to the sternum.
        recipes[spine] = new recipe{ name = "spine", anchor = js("Spine"), wrap = js("Hips"), n = up, s = side, d = depth, kind = fit.joint, front = (tune.spine_front, tune.spine_front), back = (tune.spine_back, tune.spine_back) };
        recipes[spine1] = new recipe{ name = "spine1", anchor = js("Spine1"), wrap = js("Hips"), n = up, s = side, d = depth, kind = fit.joint, front = (tune.spine1_front, tune.spine1_front), back = (tune.spine1_back, tune.spine1_back) };
        recipes[spine2] = new recipe{ name = "spine2", anchor = js("Spine2"), wrap = js("Hips"), n = up, s = side, d = depth, kind = fit.joint, front = (tune.spine2_front, tune.spine2_front), back = (tune.spine2_back, tune.spine2_back) };
        // Each leg's rings see only that leg's flesh, so the two legs' rings sit clear of each other
        // however close the legs stand. s is side on both, so the outer edge is hi on the left knee
        // and lo on the right.
        recipes[knee_hi] = new recipe{ name = "L knee", anchor = js("LeftLeg"), wrap = js("LeftUpLeg"), n = -up, s = side, d = depth, kind = fit.joint, hi = tune.knee_out, back = (tune.knee_back, tune.knee_back) };
        recipes[knee_lo] = new recipe{ name = "R knee", anchor = js("RightLeg"), wrap = js("RightUpLeg"), n = -up, s = side, d = depth, kind = fit.joint, lo = tune.knee_out, back = (tune.knee_back, tune.knee_back) };
        // The ankle ring leans back through the Foot joint -- heel down and behind, the crease of
        // the instep up and ahead -- so its frame is the knee's turned about the side axis by the
        // tilt, part way toward the toe ring's. That one stands upright across the ball of the
        // foot: n along the foot, d up, so its front is the top of the foot and its back the sole.
        var lean = tune.ankle_tilt * Mathf.Deg2Rad;
        var ankle_n = -Mathf.Cos(lean) * up + Mathf.Sin(lean) * depth;
        var ankle_d = Mathf.Cos(lean) * depth + Mathf.Sin(lean) * up;
        recipes[ankle_hi] = new recipe{ name = "L ankle", anchor = js("LeftFoot"), wrap = js("LeftLeg"), n = ankle_n, s = side, d = ankle_d, kind = fit.joint, front = (tune.ankle_front, tune.ankle_front), back = (tune.ankle_back, tune.ankle_back) };
        recipes[toe_hi] = new recipe{ name = "L toe", anchor = js("LeftToeBase"), wrap = js("LeftFoot"), n = depth, s = side, d = up, kind = fit.joint };
        recipes[ankle_lo] = new recipe{ name = "R ankle", anchor = js("RightFoot"), wrap = js("RightLeg"), n = ankle_n, s = side, d = ankle_d, kind = fit.joint, front = (tune.ankle_front, tune.ankle_front), back = (tune.ankle_back, tune.ankle_back) };
        recipes[toe_lo] = new recipe{ name = "R toe", anchor = js("RightToeBase"), wrap = js("RightFoot"), n = depth, s = side, d = up, kind = fit.joint };

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

            // A cap ring is sized by all the flesh it wraps; a joint ring only by the flesh crossing
            // its plane, within a window scaled to the bone it sits on; a split ring by the flesh
            // beyond its offset plane.
            Debug.Assert(r.kind != fit.split || r.outward_hi == r.outward_lo, "cage: a split ring's plane is one offset");
            var window = slab * r.anchor.Max(j => rest_len[j]);
            var cut = plane + r.outward_hi / scale;
            var pts = r.kind == fit.cap ? wrap
                : r.kind == fit.split ? wrap.Where(p => Vector3.Dot(p, r.n) >= cut).ToArray()
                : wrap.Where(p => Mathf.Abs(Vector3.Dot(p, r.n) - plane) <= window).ToArray();
            var (lo_s, hi_s) = inflate(pts.Min(p => Vector3.Dot(p, r.s)), pts.Max(p => Vector3.Dot(p, r.s)));
            var (lo_d, hi_d) = inflate(pts.Min(p => Vector3.Dot(p, r.d)), pts.Max(p => Vector3.Dot(p, r.d)));

            // Which anchors place which silhouette edge, by the side of the ring they rest on. The
            // two legs of a shared ring separate here; a single-limb ring lands on both sides.
            var mid = (anchors.Min(p => Vector3.Dot(p, r.s)) + anchors.Max(p => Vector3.Dot(p, r.s))) * 0.5f;
            var hi = r.anchor.Where(j => Vector3.Dot(rest[j], r.s) >= mid).ToArray();
            var lo = r.anchor.Where(j => Vector3.Dot(rest[j], r.s) <= mid).ToArray();

            // A cap ring's plane is pushed past all the flesh it wraps; the others stay on their anchors.
            var past = r.kind == fit.cap ? (wrap.Max(p => Vector3.Dot(p, r.n)) - plane) * (1f + margin) : 0f;
            return new cage_ring{
                name = r.name,
                anchor_hi = hi,
                anchor_lo = lo,
                d_hi_anchor = r.anchor,
                d_lo_anchor = r.anchor,
                n = r.n,
                s = r.s,
                d = r.d,
                along_hi = past + r.outward_hi / scale,
                along_lo = past + r.outward_lo / scale,
                hold_hi = new int[0],
                hold_lo = new int[0],
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

        // Body control points that are posts rather than ring corners, by the (station, side) the
        // topology tables name them with.
        var at = new Dictionary<(int ring, edge e), int>();
        int post(string name, int[] anchor, float[] weight, Vector3 reach, Vector3 d, int[] d_anchor, float d_lo, float d_hi){
            posts.Add(new cage_post{
                name = name, anchor = anchor, weight = weight, reach = reach,
                d = d, d_lo_anchor = d_anchor, d_hi_anchor = d_anchor, d_lo = d_lo, d_hi = d_hi,
            });
            return posts.Count - 1;
        }

        // The midline: one post wherever a front or back panel's rungs cross the middle, so the
        // torso and head come as two halves. A post's ends take the depth anchors and reach of the
        // ring whose band it closes, so it stays level with that ring's edges however they move.
        // On a ring across the body: exactly at the midpoint of its edges, anchored on the joint
        // that places them and offset by whatever the rest midpoint is off that joint.
        // No ring is held off a post yet -- the pelvis attaches that below, once the posts it names
        // have been made -- so the rest corners need none.
        var rest_corners = ring_corners(new cage_constants{ rings = rings }, rest, new Vector3[0]);
        void midline(int slot, string joint){
            var r = rings[slot];
            var anchor = js(joint);
            var mid = (rest_corners[slot * 4 + hi_front] + rest_corners[slot * 4 + lo_front]) * 0.5f;
            var off = mid - rest[anchor[0]];
            at[(slot, edge.mid)] = post(r.name + " mid", anchor, new[]{ 1f }, off - r.d * Vector3.Dot(off, r.d), r.d, r.anchor_hi.Concat(r.anchor_lo).Distinct().ToArray(),
                (r.hi_back + r.lo_back) * 0.5f, (r.hi_front + r.lo_front) * 0.5f);
        }
        midline(crown, "Head");
        midline(head, "Head");
        // Between the arm rings' top edges: the bottom of the neck's V on the Neck joint itself, and
        // level with the armpits the sternum, on Spine3. Each closes a rung of the arm rings -- the
        // top edges, the bottom edges -- so it takes that edge's depth, spread over both shoulders.
        // Both keep a reach of their own on top of that: the arm rings' edges are drawn in over the
        // chest, and without it the throat and the sternum would come back with them.
        var shoulders = js("LeftArm", "RightArm");
        var arm = rings[arm_hi];
        at[(neck, edge.mid)] = post("neck mid", js("Neck"), new[]{ 1f }, Vector3.zero, depth, shoulders, arm.hi_back, arm.hi_front + tune.neck_front / scale);
        at[(sternum, edge.mid)] = post("sternum mid", js("Spine3"), new[]{ 1f }, Vector3.zero, depth, shoulders, arm.lo_back, arm.lo_front + tune.sternum_front / scale);
        midline(spine, "Spine");
        midline(spine1, "Spine1");
        midline(spine2, "Spine2");

        // The pelvis, a palm the legs branch from. The crotch hangs below the Hips joint; each outer
        // hip post continues the crotch->UpLeg line past its UpLeg by hip_out of that span, as the
        // (1+f, -f) combination of UpLeg and Hips plus that share of the drop -- so widening one hip
        // carries its post outward and tilts that leg's ring, while the crotch stays put. The three
        // share one depth, the pelvis flesh's, the way a hand's posts share the plate: anchored on
        // Hips so the pelvis panels stay a flat slab between the waist ring and the thighs.
        var hips = index["Hips"];
        var pelvis = js("Hips", "LeftUpLeg", "RightUpLeg").SelectMany(j => flesh[j]).ToArray();
        var (pelvis_back, pelvis_front) = inflate(pelvis.Min(p => Vector3.Dot(p, depth)), pelvis.Max(p => Vector3.Dot(p, depth)));
        var pelvis_seat = Vector3.Dot(rest[hips], depth);
        int pelvis_post(string name, int[] anchor, float[] weight, Vector3 reach){
            return post(name, anchor, weight, reach, depth, new[]{ hips },
                pelvis_seat - pelvis_back + tune.pelvis_back / scale, pelvis_front - pelvis_seat + tune.pelvis_front / scale);
        }
        var drop = up * (tune.crotch_drop / scale);
        var f = tune.hip_out;
        at[(hip, edge.mid)] = pelvis_post("crotch", new[]{ hips }, new[]{ 1f }, -drop);
        at[(hip, edge.hi)] = pelvis_post("L hip", js("LeftUpLeg", "Hips"), new[]{ 1f + f, -f }, drop * f);
        at[(hip, edge.lo)] = pelvis_post("R hip", js("RightUpLeg", "Hips"), new[]{ 1f + f, -f }, drop * f);

        // The spine ring hangs on the Spine joint, which the pelvis bone carries down; the hips hang
        // on UpLeg and Hips, which that bone does not move. Shorten the pelvis far enough and the
        // ring's two sides sink past the hips beside them, and the pelvis panel folds up through the
        // torso -- what the length sweep found first. So each side is held level with the hip the
        // tables pair it with, while the midline post goes on following the joint: seen from the
        // front the ring is a level bar, then flattens onto the hips, then opens into a V with
        // spine mid at its floor. The hips themselves are untouched. `[N16]`
        rings[spine].hold_hi = new[]{ at[(hip, edge.hi)] };
        rings[spine].hold_lo = new[]{ at[(hip, edge.lo)] };

        // One foot, past its ankle ring. The sole is level: the toe ring's bottom edge and the tips'
        // lower ends sit at the height of the ankle ring's bottom -- the heel -- and hang on the
        // Foot joint, so they follow the heel whatever the foot bone does; only their tops are read
        // off the flesh. The toes end past ToeBase with no joint to stand on, so the tip is a
        // fingertip's ring: a post on each side of the toes on a virtual end bone -- (1+f, -f) of
        // ToeBase and Foot, f the toes' reach beyond ToeBase as a share of the foot bone -- so
        // lengthening the foot carries the toes out with it.
        void foot(string prefix, string tag, int ankle, int toe, int station){
            var joint = index[prefix + "Foot"];
            var ball = index[prefix + "ToeBase"];
            var floor = Vector3.Dot(rest[joint] - rest_corners[ankle * 4 + lo_back], up);
            rings[toe].d_lo_anchor = new[]{ joint };
            rings[toe].hi_back = rings[toe].lo_back = floor;

            var meat = subtree(ball, parent).SelectMany(j => flesh[j]).ToArray();
            var over = meat.Max(p => Vector3.Dot(p - rest[ball], dir[ball])) * (1f + margin);
            var share = over / rest_len[ball];
            var anchor = new[]{ ball, joint };
            var weight = new[]{ 1f + share, -share };
            var end = rest[ball] * (1f + share) - rest[joint] * share;
            var (wide_lo, wide_hi) = inflate(meat.Min(p => Vector3.Dot(p, side)), meat.Max(p => Vector3.Dot(p, side)));
            var (_, top) = inflate(meat.Min(p => Vector3.Dot(p, up)), meat.Max(p => Vector3.Dot(p, up)));

            int add(Vector3 reach){
                posts.Add(new cage_post{
                    name = $"{tag} tip", anchor = anchor, weight = weight, reach = reach, d = up,
                    d_lo_anchor = new[]{ joint }, d_hi_anchor = new[]{ ball }, d_lo = floor, d_hi = top - Vector3.Dot(rest[ball], up),
                });
                return posts.Count - 1;
            }
            at[(station, edge.hi)] = add(side * (wide_hi - Vector3.Dot(end, side)));
            at[(station, edge.lo)] = add(side * (wide_lo - Vector3.Dot(end, side)));
        }
        foot("Left", "L", ankle_hi, toe_hi, tip_hi);
        foot("Right", "R", ankle_lo, toe_lo, tip_lo);

        // The deltoid post: on top of the upper arm, delt_along of the way from the shoulder joint
        // to the elbow, up at the top of the arm's flesh there. With the arm ring's armpit edge it
        // makes the ring the upper arm hangs from, tilted from the armpit up and out -- a V with the
        // arm ring seen from the front, the shoulder in the wedge between. Sitting on the bone as an
        // affine combination, it slides along when the upper arm is lengthened. Its depth is the
        // arm's there, hung on the two joints it stands between.
        void delt(string prefix, string tag, int station){
            var shoulder = index[prefix + "Arm"];
            var elbow = index[prefix + "ForeArm"];
            var g = tune.delt_along;
            var anchor = new[]{ shoulder, elbow };
            var weight = new[]{ 1f - g, g };
            var seat = rest[shoulder] * (1f - g) + rest[elbow] * g;

            var along = dir[elbow];
            var window = slab * rest_len[elbow];
            var meat = flesh[shoulder].Where(p => Mathf.Abs(Vector3.Dot(p - seat, along)) <= window).ToArray();
            var (_, top) = inflate(meat.Min(p => Vector3.Dot(p, up)), meat.Max(p => Vector3.Dot(p, up)));
            var (back, front) = inflate(meat.Min(p => Vector3.Dot(p, depth)), meat.Max(p => Vector3.Dot(p, depth)));

            posts.Add(new cage_post{
                name = $"{tag} delt", anchor = anchor, weight = weight, reach = up * (top - Vector3.Dot(seat, up) + tune.delt_up / scale), d = depth,
                d_lo_anchor = anchor, d_hi_anchor = anchor,
                d_lo = anchor.Min(j => Vector3.Dot(rest[j], depth)) - back, d_hi = front - anchor.Max(j => Vector3.Dot(rest[j], depth)),
            });
            at[(station, edge.hi)] = posts.Count - 1;
        }
        delt("Left", "L", delt_hi);
        delt("Right", "R", delt_lo);

        // A body control point as a vertex pair, front then back: a post where the station has one,
        // otherwise a ring's silhouette edge.
        (int hi, int lo) ends((int ring, edge e) c){
            return at.TryGetValue(c, out var p) ? pair(p) : (corner(c.ring, c.e, true), corner(c.ring, c.e, false));
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
            rings[slot].hi_front = rings[slot].lo_front = palm_hi - Vector3.Dot(rest[wrist], s) + tune.wrist_thumb / scale;
            rings[slot].hi_back = rings[slot].lo_back = Vector3.Dot(rest[wrist], s) - palm_lo + tune.wrist_pinky / scale;

            // The six control points that carve the palm outline into finger branches. The thumb
            // and pinky ends come from the hand's own width; the four valleys sit halfway between
            // neighbouring finger roots, pushed away from the wrist past the knuckle line.
            var (wide_lo, wide_hi) = inflate(skin.Min(p => Vector3.Dot(p, s)), skin.Max(p => Vector3.Dot(p, s)));
            var thumb = index[prefix + "Thumb2"];
            var pinky = index[prefix + "Pinky1"];

            var cp = new int[6];
            cp[0] = add($"{tag} thumb out", new[]{ thumb }, new[]{ 1f }, s * (wide_hi - Vector3.Dot(rest[thumb], s) + tune.thumb_out / scale));
            for(var f = 0; f < 4; f++){
                // The thumb branches off at its own second joint, the rest at their roots.
                var a = f == 0 ? thumb : index[prefix + fingers[f] + "1"];
                var b = index[prefix + fingers[f + 1] + "1"];
                var span = (rest[a] + rest[b]) * 0.5f - rest[wrist];
                var away = (span - d * Vector3.Dot(span, d)).normalized;
                cp[f + 1] = add($"{tag} {fingers[f].ToLower()}|{fingers[f + 1].ToLower()}", new[]{ a, b }, new[]{ 0.5f, 0.5f }, away * (valley_reach / scale));
            }
            cp[5] = add($"{tag} pinky out", new[]{ pinky }, new[]{ 1f }, s * (wide_lo - Vector3.Dot(rest[pinky], s) - tune.pinky_out / scale));

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
        };
        var rest_points = control_points(k, rest);
        (k.tris, k.grid) = topology(plates, walls, rest_points, side);

        // The panels are traced in one consistent sense, but which sense faces outward depends on
        // the rig's axes. The enclosed volume settles it: the root sits inside the cage.
        if(volume(rest_points, k.tris) < 0.0){
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

    public static int dominant_bone(BoneWeight w){
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
    //
    // The shell is mirror symmetric: a face on the character's right is traced as the reverse of its
    // left twin's mirror image (the reversal keeps every face wound the same way), which on its own
    // would put the ladder's diagonals across the other corners, so the right side splits its quads
    // the other way. So does the back of a plate against its front, which is the same reversal --
    // then a quad folds along the same two control points on both faces. Which side a face is on is
    // read off its rest centroid; no face straddles the midline, since every plate splits there.
    static (int[] tris, int[] grid) topology(IEnumerable<(int hi, int lo)[]> plates, IEnumerable<(int hi, int lo)[]> walls, Vector3[] at, Vector3 side){
        var tris = new List<int>();

        bool mirrored(IEnumerable<int> face){
            var c = face.Average(v => Vector3.Dot(at[v], side));
            Debug.Assert(Mathf.Abs(c) > 1e-6f, "cage: a face straddles the midline, so it cannot have a mirror twin");
            return c < 0f;
        }

        foreach(var plate in plates){
            var m = mirrored(plate.SelectMany(e => new[]{ e.hi, e.lo }));
            strip(tris, plate.Select(e => e.hi), m);
            strip(tris, plate.Reverse().Select(e => e.lo), !m);
        }

        foreach(var wall in walls){
            for(var i = 0; i + 1 < wall.Length; i++){
                var quad = new[]{ wall[i + 1].hi, wall[i].hi, wall[i].lo, wall[i + 1].lo };
                strip(tris, quad, mirrored(quad));
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

        // The tables' own edges, kept because triangulating loses them (see cage_constants.grid):
        // consecutive posts along a chain, joined at each of their two ends. A plate's outline is a
        // closed loop, a wall's chain runs open. A post across is a ring's own side or a post's
        // segment, which the rings and posts already give, so it stays out.
        IEnumerable<(int, int)> chain((int hi, int lo)[] posts, bool closed){
            return Enumerable.Range(0, closed ? posts.Length : posts.Length - 1).SelectMany(i => {
                var next = posts[(i + 1) % posts.Length];
                return new[]{ (posts[i].hi, next.hi), (posts[i].lo, next.lo) };
            });
        }
        var grid = plates.SelectMany(p => chain(p, true)).Concat(walls.SelectMany(w => chain(w, false)))
            .Select(e => (a: Mathf.Min(e.Item1, e.Item2), b: Mathf.Max(e.Item1, e.Item2))).Distinct()
            .SelectMany(e => new[]{ e.a, e.b }).ToArray();

        return (tris.ToArray(), grid);
    }

    static int corner(int ring, edge e, bool front){
        Debug.Assert(e != edge.mid && ring <= head, "cage: not a ring corner");
        return ring * 4 + (e == edge.hi ? (front ? hi_front : hi_back) : (front ? lo_front : lo_back));
    }

    // Triangulate an outline as a ladder between its two halves, preserving the traced sense: each
    // rung pairs a corner from one side with the one facing it. A fan would pivot the whole panel on
    // its first corner, which skews a non-planar panel -- every ring carries its own depth, so the
    // torso would run straight from the crown to the knees and bypass the elbow rings. An outline
    // with an odd count ends in a single triangle where the two halves meet, so a three-point
    // outline is just that triangle. Each rung's quad is split along one diagonal or, flipped,
    // the other; the sense of the triangles is the outline's either way.
    static void strip(List<int> tris, IEnumerable<int> loop, bool flip){
        var v = loop.ToArray();
        for(var i = 0; i + 1 < v.Length - 1 - i; i++){
            var j = v.Length - 1 - i;
            if(j - 1 == i + 1){
                tris.Add(v[i]); tris.Add(v[i + 1]); tris.Add(v[j]);
            }
            else if(flip){
                tris.Add(v[i]); tris.Add(v[i + 1]); tris.Add(v[j]);
                tris.Add(v[i + 1]); tris.Add(v[j - 1]); tris.Add(v[j]);
            }
            else{
                tris.Add(v[i]); tris.Add(v[i + 1]); tris.Add(v[j - 1]);
                tris.Add(v[i]); tris.Add(v[j - 1]); tris.Add(v[j]);
            }
        }
    }

    // Signed volume of a closed triangle soup; positive when the winding puts the normals out.
    static double volume(Vector3[] v, int[] tris){
        return Enumerable.Range(0, tris.Length / 3)
            .Sum(t => (double)Vector3.Dot(v[tris[t * 3]], Vector3.Cross(v[tris[t * 3 + 1]], v[tris[t * 3 + 2]]))) / 6.0;
    }

    // Containment: which of the given points fall outside the shell. The body at a set of bone
    // lengths is the rest mesh mapped through the cage those lengths build, so both arguments come
    // out of the same lengths and the check is a pure function of them -- the inspector button runs
    // it on the current lengths, the sweep in tools/cage_sweep headless over thousands of them.
    // This coarse a cage escapes at rest already, so what a sweep reads is the change from that.
    public static List<int> outside(Vector3[] pts, Vector3[] cage_verts, int[] tris){
        return Enumerable.Range(0, pts.Length).Where(i => !inside(pts[i], cage_verts, tris)).ToList();
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

    // Debug view: the edges this document itself declares -- every ring, every post, and the grid
    // the topology tables lay between them (cage_constants.grid). A ring is a rectangle over four
    // vertices, and the corner constants run round it in order; a finger ring is two posts under one
    // name; a branch ring -- hip beside crotch, deltoid beside armpit -- is two posts the tables put
    // side by side, which is why the grid is needed to see it. What the ladder adds to fill the
    // panels, its rungs and one diagonal per quad, answers to no row of any table and stays out, so
    // a wire drawn from this reads as the recipe rather than as the mesh.
    public static IEnumerable<(int a, int b)> frame(cage_constants k){
        // Consecutive pairs round a closed outline; two vertices are the one edge between them.
        IEnumerable<(int, int)> loop(int[] v){
            return Enumerable.Range(0, v.Length == 2 ? 1 : v.Length).Select(i => (v[i], v[(i + 1) % v.Length]));
        }

        int end(int post, int e){
            return k.rings.Length * 4 + post * 2 + e;
        }

        var rings = k.rings.SelectMany((r, i) => loop(Enumerable.Range(i * 4, 4).ToArray()));

        // A finger ring is two posts under one name, so its four vertices close a rectangle the way
        // a body ring's do -- across each post, then along the ring's two sides. The midline, the
        // pelvis, the shoulders and the palm carry one post each, which is an edge on its own.
        var posts = Enumerable.Range(0, k.posts.Length).GroupBy(i => k.posts[i].name).SelectMany(g => {
            var p = g.ToArray();
            return loop(p.Length == 1
                ? new[]{ end(p[0], post_hi), end(p[0], post_lo) }
                : new[]{ end(p[0], post_hi), end(p[0], post_lo), end(p[1], post_lo), end(p[1], post_hi) });
        });

        var grid = Enumerable.Range(0, k.grid.Length / 2).Select(i => (k.grid[i * 2], k.grid[i * 2 + 1]));

        return rings.Concat(posts).Concat(grid)
            .Select(e => (a: Mathf.Min(e.Item1, e.Item2), b: Mathf.Max(e.Item1, e.Item2))).Distinct();
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
    // places -- each silhouette edge of a ring by its own anchors, a depth side by any anchor of its
    // own that the edges do not already show, a post group by its center. The weight is the post's
    // affine weight; a ring on a virtual end bone reads (1+f, -f) on its two.
    public static IEnumerable<(string name, string joint, float weight, Vector3 from, Vector3 to)> anchors(IReadOnlyDictionary<string, float> lengths, cage_constants k){
        var jc = joint_centers(lengths, k);
        var v = control_points(k, jc);

        var rings = k.rings.SelectMany((r, i) => {
            var hi = (v[i * 4 + hi_front] + v[i * 4 + hi_back]) * 0.5f;
            var lo = (v[i * 4 + lo_front] + v[i * 4 + lo_back]) * 0.5f;
            var front = (v[i * 4 + hi_front] + v[i * 4 + lo_front]) * 0.5f;
            var back = (v[i * 4 + hi_back] + v[i * 4 + lo_back]) * 0.5f;
            var edges = r.anchor_hi.Concat(r.anchor_lo);
            return r.anchor_hi.Select(j => (name: r.name, joint: k.joint_name[j], weight: 1f, from: jc[j], to: hi))
                .Concat(r.anchor_lo.Select(j => (name: r.name, joint: k.joint_name[j], weight: 1f, from: jc[j], to: lo)))
                .Concat(r.d_hi_anchor.Except(edges).Select(j => (name: r.name, joint: k.joint_name[j], weight: 1f, from: jc[j], to: front)))
                .Concat(r.d_lo_anchor.Except(edges).Select(j => (name: r.name, joint: k.joint_name[j], weight: 1f, from: jc[j], to: back)));
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
