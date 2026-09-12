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
// parts the torso from the head. Below the spine ring the pelvis branches into the legs the way a
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
    public float s_lo, s_hi;    // reach beyond the anchors' span, on the -s and +s side
    public cage_span[] girth;   // the span whose current over rest length scales the four values above --
                                // the ring's silhouette follows it; empty and it keeps its rest size. A limb's
                                // rings all follow its root bone: the arm, elbow and wrist the clavicle, the
                                // knee, ankle and toe the hip `[N22]`; the head rings follow the stature `[N23]`.
    public cage_span[] girth_d; // the same for the four depth reaches below: what the ring's thickness across
                                // d follows. The wrist ring's is the palm's breadth, thumb to pinky `[N25]`;
                                // empty and the depth keeps its rest reach.
    public int[] between;       // eight placed control points, two per corner in corner order: the corner
                                // lies on the segment between its two where the ring's plane crosses it,
                                // all three coordinates, and its own reach goes unused. The spine1 and
                                // spine2 rings hang on the lines from the armpits to the spine ring. `[N27]`
    public int[] hi_between, lo_between;    // two placed control points the edge lies between: its s
                                            // coordinate is theirs, taken where the ring's plane falls
                                            // between their n coordinates; empty and the edge keeps its
                                            // own reach. The torso's sides run armpit to hip. `[N21]`
    public float hi_front, lo_front;    // each corner's reach along d past its depth anchors:
    public float hi_back, lo_back;      // front past their max, back past their min
}

// A length read off the skeleton, as a ratio to its rest value: what a ring's silhouette scales
// with. The farthest of a along the axis to the nearest of b, so a bone is (joint, parent) along
// its rest direction -- exactly its length, since FK keeps that direction -- and the stature is
// the Head joint over the lower of the two toe bases, along up. Never a difference of two
// control points: girth reads joints only, so no ring waits on another. `[N16]`
[Serializable]
public class cage_span{
    public int[] a, b;          // joints: the span runs from the least of b to the greatest of a, along axis
    public Vector3 axis;
    public float rest;          // the same span at rest
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
    public cage_span[] girth;   // as a ring's: the span whose current over rest length scales reach. The toe
                                // tips follow the hip, so the cap widens with the leg `[N22]`; a midline post
                                // follows its ring, so a section scales whole `[N23]`; empty elsewhere.
    public cage_span[] girth_d; // the same for d_lo and d_hi: what the post's thickness follows. A hand's
                                // posts follow its wrist ring's silhouette, so the plate is as thick as the
                                // arm it ends `[N25]`; empty elsewhere.
    public int[] hi_between, lo_between;    // two placed control points the end lies between: the end is
                                            // where their segment crosses the plane through the post's
                                            // anchors perpendicular to between_axis, all three coordinates.
                                            // The sternum is the armpits' line crossing the midline, and a
                                            // spine ring's midline post its own front and back edges'. `[N27]`
    public Vector3 between_axis;
    public int[] hi_mean, lo_mean;  // placed vertices whose mean d coordinate the end takes once the sections
                                    // are restored, so a post closing a rung stays on the corners it closes:
                                    // the V's bottom and the sternum on the two arm rings' edges, each spine
                                    // ring's post on its own, the crotch on the two hip posts. `[N29]`
}

// A correction applied once every ring and post is placed, because it reads one part of the cage
// against another and so cannot run while that part is still being built. The moved vertices are
// carried together, which keeps whatever shape they make; the floor is what they may not pass on
// the axis. See cage.md `[N20]`.
[Serializable]
public class cage_gate{
    public string name;         // as the design document calls it
    public int[] moved;         // vertices lifted together
    public int[] probe;         // the place on them that is read: the least of these along the axis
    public int[] floor;         // vertices the probe must stay on the +axis side of
    public float slack;         // how far past the floor the probe may go before the gate acts, rig units
    public Vector3 axis;
}

// A section whose depth is restored once the cage is placed. What the recipe deforms is the
// silhouette -- the seam follows the clavicle, the torso's sides the hips and the armpits, a
// limb's rings its root bone -- while the depth reaches stay what the rest measured, so a body
// with wide shoulders would keep a rest-thick chest. The restore reads the section's width off
// the placed cage and multiplies every depth reach by width over rest width, each reach from the
// joint it was measured from: the ring formula's g_d, taken from the section's own width instead
// of a declared span. The two sides may hang on different joints -- the toe ring's top on ToeBase
// and its sole on the Foot -- which is why each has its own seat: a longer foot bone drops
// ToeBase, and the top must follow it with its own reach while the sole stays level with the
// heel. Each section stands alone: the trunk shares its depth at rest and parts wherever an edit
// lands. See cage.md `[N29]`.
[Serializable]
public class cage_section{
    public string name;         // as the design document calls it
    public int[] hi, lo;        // the two silhouette edges as vertex groups: the width is the distance between their centers across d
    public float rest_width;    // that distance at rest
    public int[] front, back;   // the vertices whose depth reach is scaled: the front's past front_seat, the back's before back_seat
    public int front_seat, back_seat;   // the joints those reaches are measured from -- the Hips for the trunk, a limb
                                        // ring's own joint, ToeBase over the Foot for the toe ring and the tips
    public Vector3 d;
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
    public cage_section[] sections; // depth restored from width, in this order, once the rest is placed and before the gates
    public cage_gate[] gates;       // corrections, applied in this order once the rest is placed

    // Vertex pairs: the edges the topology tables declare, which tris alone cannot give back. Each
    // consecutive pair of posts along a plate's outline or a wall's chain is a ring of the shell --
    // the tilted hip ring is the crotch post beside an outer hip post -- while what the ladder adds to fill the panels
    // between them, its rungs and the diagonal splitting each quad, no row of any table names.
    // Triangulating loses the difference, so the pairs are kept. Read by the debug wire (frame).
    public int[] grid;

    // How far inside the shell the mesh must stay, in rig units. Being outside is not the whole
    // problem: a vertex sitting on a cage face breaks the coordinate kernel, and the error is
    // already 6 mm a tenth of a millimetre off it. See cage.md `[N18]`.
    public float clearance;
}

#if UNITY_EDITOR
// Recipe values still being found. The inspector's tuning sliders write here and rebake, so the
// cage follows while the value is searched for; once settled, a value moves into the recipe table
// in bake() and into the design document, and its slider goes. Scene units, like the recipes.
[Serializable]
public class cage_tune{
    public float arm_tilt = 7f;     // degrees the arm rings lean in at the top about the depth axis, seen from
                                    // the front: the raglan seam from the armpit up over the trapezius
    public float arm_length = 0.16f;    // the seam's length, armpit edge to top edge, the shoulder joint at its middle
    public float body_front = 0f, body_back = 0f;   // depth reach of the trunk past its flesh, off the Hips joint:
                                                    // the chest and belly (front), the buttocks and shoulder blades
                                                    // (back). The arm rings, the neck post, the spine ring and the
                                                    // pelvis posts all share it, so the torso is a box from the side `[N28]`
    public float head_tilt = 25f;       // degrees the head ring's plane leans forward about the side axis, chin down
    public float head_offset = 0.023f;  // how far above the Head joint that plane sits, along its own normal
    public float head_front = 0f, head_back = 0f;   // depth reach of the head ring: chin and occiput
    public float head_gate_slack = 0.038f;  // how far the head ring's lowest corner may sink below the
                                            // arm rings' top edges before the gate lifts the head off
                                            // them: all of it, and the neck can hardly shorten `[N20]`
    public float arm_gate_slack = 0f;   // how far inside the head's silhouette an arm ring's top edge may
                                        // come before the gate stops the whole ring coming in `[N20]`
    public float spine_gate_slack = -0.005f;  // how far past the hips the spine ring's bottom may sink
                                             // before the gate stops the torso coming down on them;
                                             // negative demands a gap, as it must here `[N20]`
    public float knee_gate_slack = -0.01f;   // how far past the crotch a knee ring's inner edge may come
                                             // before the gate stops the whole ring; negative demands a gap
                                             // instead, and here it must -- stopped flush both rings stand
                                             // on the midline and the panels still graze `[N20]`
    public float crown_front = 0f;  // depth reach of the crown ring: the chest and belly (front) and the
    public float crown_back = 0f;   // shoulder blades (back) sit under the torso panel these two rings span
    public float crotch_drop = 0.15f;   // how far below the Hips joint the crotch post sits, along up
    public float hip_out = 1f;          // ratio: an outer hip post is this many crotch->UpLeg spans past its UpLeg
    public float knee_out = 0f;         // reach of both knee rings' outer edge, away from the other leg
    public float knee_back = 0.1f;      // and of their back edge, past the hamstring and calf
    public float ankle_tilt = 45f;      // degrees the ankle rings' plane leans back from horizontal about the side axis: heel down, instep up
    public float ankle_front = 0f;      // depth reach of the ankle rings along their tilted d: up the instep (front),
    public float ankle_back = 0f;       // down behind the heel (back) -- which is also the height the sole is levelled to
    public float elbow_hi = 0.05f;      // hi reach of both elbow rings: how far their top edge clears the elbow
    public float wrist_thumb = 0f;      // reach of both wrist rings across the palm, past the measured width: thumb side
    public float wrist_pinky = 0f;      // and pinky side; negative draws the ring in over the wrist
    public float thumb_out = 0f;        // reach of the palm octagon's outer posts past the hand's width: the thumb side
    public float pinky_out = 0f;        // and the pinky side
    public float finger_out = 0f;       // reach of every finger ring on both sides, across its own bone: the one
                                        // knob for all the fingers at once, over the per-ring finger_reach table
    public float valley_reach = 0.01f;  // how far past the knuckle line a valley control point sits, so the web
                                        // between two fingers falls inside the shell; no joint of the rig marks it
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
        // Both read nothing but the joint centers, so neither waits on the other.
        var verts = ring_corners(k, jc).Concat(post_ends(k, jc)).ToArray();
        between(k, verts);
        restore(k, verts, jc);

        // Then the gates, which read the placed cage against itself. Declaration order is their
        // priority: a later one moves vertices an earlier one may already have moved.
        foreach(var g in k.gates){
            var lift = Mathf.Max(0f, g.floor.Max(i => Vector3.Dot(verts[i], g.axis))
                - g.probe.Min(i => Vector3.Dot(verts[i], g.axis)) - g.slack);
            foreach(var i in g.moved){
                verts[i] += g.axis * lift;
            }
        }
        return verts;
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
    public static Vector3[] joint_centers(IReadOnlyDictionary<string, float> lengths, cage_constants k){
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

    // Control points that lie between two placed control points. This is the one place a control
    // point reads another during placement, and it runs one way, in three stages that each read
    // only what came before: edges (the spine ring's sides, off the hip posts and the armpits),
    // then whole corners (the spine1 and spine2 rings, off the armpits and the spine ring), then
    // post ends (the sternum and those rings' midline posts, off the corners). `[N21]` `[N27]`
    static void between(cage_constants k, Vector3[] verts){
        // Where the segment a-b crosses the plane at `plane` along `axis`, held at the nearer end
        // past it.
        Vector3 crossing(Vector3 a, Vector3 b, Vector3 axis, float plane){
            return Vector3.Lerp(a, b, Mathf.Clamp01((plane - Vector3.Dot(a, axis)) / Vector3.Dot(b - a, axis)));
        }

        // An edge takes only its s coordinate from the segment, where the ring's plane crosses it;
        // its own plane and depth stay.
        for(var i = 0; i < k.rings.Length; i++){
            var r = k.rings[i];
            void slide(int[] on, int front, int back){
                if(on.Length == 0){
                    return;
                }
                var at = r.s * Vector3.Dot(crossing(verts[on[0]], verts[on[1]], r.n, Vector3.Dot(verts[i * 4 + front], r.n)), r.s);
                foreach(var c in new[]{ front, back }){
                    verts[i * 4 + c] += at - r.s * Vector3.Dot(verts[i * 4 + c], r.s);
                }
            }
            slide(r.hi_between, hi_front, hi_back);
            slide(r.lo_between, lo_front, lo_back);
        }

        // A corner is the crossing itself: the ring keeps only its plane.
        for(var i = 0; i < k.rings.Length; i++){
            var r = k.rings[i];
            if(r.between.Length == 0){
                continue;
            }
            for(var c = 0; c < 4; c++){
                var v = i * 4 + c;
                verts[v] = crossing(verts[r.between[2 * c]], verts[r.between[2 * c + 1]], r.n, Vector3.Dot(verts[v], r.n));
            }
        }

        // A post end likewise, where the segment crosses the plane through the post's anchored
        // position across between_axis -- the midline, for the torso.
        for(var i = 0; i < k.posts.Length; i++){
            var p = k.posts[i];
            void cross(int[] on, int end){
                if(on.Length == 0){
                    return;
                }
                var v = k.rings.Length * 4 + i * 2 + end;
                verts[v] = crossing(verts[on[0]], verts[on[1]], p.between_axis, Vector3.Dot(verts[v], p.between_axis));
            }
            cross(p.hi_between, post_hi);
            cross(p.lo_between, post_lo);
        }
    }

    // A section's width as placed: the distance between its two silhouette edges' centers across d.
    static float width(cage_section s, Vector3[] verts){
        Vector3 center(int[] v){
            return v.Aggregate(Vector3.zero, (a, i) => a + verts[i]) / v.Length;
        }
        var across = center(s.hi) - center(s.lo);
        return (across - s.d * Vector3.Dot(across, s.d)).magnitude;
    }

    // Depth restored from width, once everything is placed and before the gates: each section's
    // depth reaches, still what the rest measured, are multiplied by its width over its rest width,
    // each from the joint it hangs on -- so depth over width is what it was at rest. Then every
    // post closing a rung takes the mean depth of the corners it closes, so it moves with them.
    // This touches depth alone and the gates up and side alone, so neither reads what the other
    // moves. `[N29]`
    static void restore(cage_constants k, Vector3[] verts, Vector3[] jc){
        foreach(var s in k.sections){
            var by = width(s, verts) / s.rest_width;
            void scale(int[] at, int seat){
                var from = Vector3.Dot(jc[seat], s.d);
                foreach(var i in at){
                    verts[i] += s.d * ((Vector3.Dot(verts[i], s.d) - from) * (by - 1f));
                }
            }
            scale(s.front, s.front_seat);
            scale(s.back, s.back_seat);
        }

        for(var i = 0; i < k.posts.Length; i++){
            var p = k.posts[i];
            void level(int[] of, int end){
                if(of.Length == 0){
                    return;
                }
                var v = k.rings.Length * 4 + i * 2 + end;
                verts[v] += p.d * (of.Average(j => Vector3.Dot(verts[j], p.d)) - Vector3.Dot(verts[v], p.d));
            }
            level(p.hi_mean, post_hi);
            level(p.lo_mean, post_lo);
        }
    }

    // How much a silhouette follows its girth span: its current length over its rest length. No span,
    // no change.
    static float girth(Vector3[] jc, cage_span[] span){
        return span.Length == 0 ? 1f
            : span.Select(g => (g.a.Max(j => Vector3.Dot(jc[j], g.axis)) - g.b.Min(j => Vector3.Dot(jc[j], g.axis))) / g.rest).Single();
    }

    // The ring axes are orthonormal, so summing the three components rebuilds a corner exactly.
    // Each silhouette edge is placed along n by its own anchors; the depth extent is shared by both
    // edges, which keeps the four corners planar however far the two edges drift apart.
    static Vector3[] ring_corners(cage_constants k, Vector3[] jc){
        var verts = new Vector3[k.rings.Length * 4];
        for(var i = 0; i < k.rings.Length; i++){
            var r = k.rings[i];
            var a_hi = r.anchor_hi.Select(j => jc[j]).ToArray();
            var a_lo = r.anchor_lo.Select(j => jc[j]).ToArray();

            var by = girth(jc, r.girth);

            var plane_hi = r.n * (a_hi.Max(p => Vector3.Dot(p, r.n)) + r.along_hi * by);
            var plane_lo = r.n * (a_lo.Max(p => Vector3.Dot(p, r.n)) + r.along_lo * by);
            var edge_hi = r.s * (a_hi.Max(p => Vector3.Dot(p, r.s)) + r.s_hi * by);
            var edge_lo = r.s * (a_lo.Min(p => Vector3.Dot(p, r.s)) - r.s_lo * by);

            var deep = girth(jc, r.girth_d);
            var front = r.d_hi_anchor.Max(j => Vector3.Dot(jc[j], r.d));
            var back = r.d_lo_anchor.Min(j => Vector3.Dot(jc[j], r.d));

            verts[i * 4 + hi_front] = plane_hi + edge_hi + r.d * (front + r.hi_front * deep);
            verts[i * 4 + hi_back] = plane_hi + edge_hi + r.d * (back - r.hi_back * deep);
            verts[i * 4 + lo_back] = plane_lo + edge_lo + r.d * (back - r.lo_back * deep);
            verts[i * 4 + lo_front] = plane_lo + edge_lo + r.d * (front + r.lo_front * deep);
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
            var at = p.anchor.Select((j, a) => jc[j] * p.weight[a]).Aggregate((x, y) => x + y) + p.reach * girth(jc, p.girth);
            var flat = at - p.d * Vector3.Dot(at, p.d);
            var deep = girth(jc, p.girth_d);

            verts[i * 2 + post_hi] = flat + p.d * (p.d_hi_anchor.Max(j => Vector3.Dot(jc[j], p.d)) + p.d_hi * deep);
            verts[i * 2 + post_lo] = flat + p.d * (p.d_lo_anchor.Min(j => Vector3.Dot(jc[j], p.d)) - p.d_lo * deep);
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
    // The tips are the ends of the toes, a post on each side standing on a virtual end bone.
    const int neck = 17, sternum = 18, hip = 19, tip_hi = 20, tip_lo = 21;

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
        // The arms: the upper arm straight from the arm ring to the elbow, then the forearm.
        new[]{ (arm_hi, edge.hi), (elbow_hi, edge.hi), (elbow_hi, edge.lo), (arm_hi, edge.lo) },
        new[]{ (elbow_hi, edge.hi), (wrist_hi, edge.hi), (wrist_hi, edge.lo), (elbow_hi, edge.lo) },
        new[]{ (arm_lo, edge.lo), (elbow_lo, edge.lo), (elbow_lo, edge.hi), (arm_lo, edge.hi) },
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
        new[]{ (crown, edge.hi), (head, edge.hi), (arm_hi, edge.hi), (elbow_hi, edge.hi), (wrist_hi, edge.hi) },
        new[]{ (wrist_hi, edge.lo), (elbow_hi, edge.lo), (arm_hi, edge.lo), (spine2, edge.hi), (spine1, edge.hi), (spine, edge.hi),
               (hip, edge.hi), (knee_hi, edge.hi), (ankle_hi, edge.hi), (toe_hi, edge.hi), (tip_hi, edge.hi),
               (tip_hi, edge.lo), (toe_hi, edge.lo), (ankle_hi, edge.lo), (knee_hi, edge.lo),
               (hip, edge.mid),
               (knee_lo, edge.hi), (ankle_lo, edge.hi), (toe_lo, edge.hi), (tip_lo, edge.hi),
               (tip_lo, edge.lo), (toe_lo, edge.lo), (ankle_lo, edge.lo), (knee_lo, edge.lo), (hip, edge.lo),
               (spine, edge.lo), (spine1, edge.lo), (spine2, edge.lo), (arm_lo, edge.lo), (elbow_lo, edge.lo), (wrist_lo, edge.lo) },
        new[]{ (wrist_lo, edge.hi), (elbow_lo, edge.hi), (arm_lo, edge.hi), (head, edge.lo), (crown, edge.lo), (crown, edge.mid), (crown, edge.hi) },
    };

    // The five fingers, thumb first. A hand's silhouette axis runs from the thumb (+s) to the pinky
    // (-s), and the six palm control points interleave with them.
    static readonly string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

    // How far below the hand's plate the wrist ring's palm side reaches, in scene units. It is the
    // same kind of slack as a recipe's reach, but the wrist ring takes its silhouette extent from
    // the hand rather than from measure(), so it belongs here: the forearm is far thicker than the
    // hand, and without it the arm panel pinches to the palm's thickness at the wrist. Editable.
    const float wrist_drop = 0.01f;

    // Extra girth for one finger ring, in scene units, across the palm plane -- the width read off
    // the back of the hand, which is the only one a finger ring has. Rings are numbered as the hand
    // is described: the branch ring a finger shares with its neighbours is 1, so ring 2 is the first
    // one standing on a joint of its own. hi is the thumb side, lo the pinky side. Editable; a ring
    // not listed keeps the width measured off the flesh, plus whatever the tune's finger_out adds to
    // every ring alike.
    static readonly (string finger, int ring, float hi, float lo)[] finger_reach = {
        ("Index", 3, 0.001f, 0.001f),
        ("Middle", 2, 0f, 0.001f),
    };

    // Fractional slack added to every measured extent so the shell clears the flesh instead of
    // touching it. Editable constant.
    const float margin = 0.05f;

    // How far inside the shell every mesh vertex must stay, in scene units. The coordinate kernel
    // degrades as a point nears a face and breaks on it; this is where the error goes flat, measured
    // in `[N18]`. Editable constant.
    const float clearance = 0.0005f;

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
                                    // there. Unequal values tilt the ring.
        public cage_span[] girth = new cage_span[0];    // the span the silhouette scales with (cage_ring.girth)
        public bool body;           // depth is the trunk's, off the Hips joint (body front / back), not
                                    // measured off this ring's own flesh `[N28]`
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

        // What a silhouette may scale with: a bone, as its length over rest; or the stature, the Head
        // joint's height over the lower toe base -- the standing height, and the same whichever leg
        // was edited. Linear for now; a child's proportions would want a curve here. `[N23]`
        cage_span[] bone(string joint){
            var j = index[joint];
            return new[]{ new cage_span{ a = new[]{ j }, b = new[]{ parent[j] }, axis = dir[j], rest = rest_len[j] } };
        }
        var stature = new[]{ new cage_span{
            a = js("Head"), b = js("LeftToeBase", "RightToeBase"), axis = up,
            rest = Vector3.Dot(rest[index["Head"]], up) - js("LeftToeBase", "RightToeBase").Min(j => Vector3.Dot(rest[j], up)),
        } };

        // The reach fields pull a panel out over flesh the rings themselves do not see, and belong
        // to whichever ring bounds that panel there. The torso's depth is one value for the whole
        // trunk (body, below), so every ring and post on it stands on the same front and back lines;
        // the head and the limbs measure their own. Editable.
        var recipes = new recipe[17];
        recipes[crown] = new recipe{ name = "crown", anchor = js("Head"), wrap = js("Head"), n = up, s = side, d = depth, kind = fit.cap, girth = stature, front = (tune.crown_front, tune.crown_front), back = (tune.crown_back, tune.crown_back) };
        // The head ring parts the head from the neck. The chin hangs ahead of and below the skull
        // base, so the parting plane leans forward about the side axis: its frame is up and depth
        // turned by that tilt, and it sits a little above the Head joint along its own normal. Both
        // head rings scale with the stature: a taller body has a bigger head, as wide at the jaw
        // and the skull as it is tall above the joint. `[N23]`
        var tilt = tune.head_tilt * Mathf.Deg2Rad;
        recipes[head] = new recipe{ name = "head", anchor = js("Head"), wrap = js("Head"), n = Mathf.Cos(tilt) * up + Mathf.Sin(tilt) * depth, s = side, d = Mathf.Cos(tilt) * depth - Mathf.Sin(tilt) * up, kind = fit.split, girth = stature, outward_hi = tune.head_offset, outward_lo = tune.head_offset, front = (tune.head_front, tune.head_front), back = (tune.head_back, tune.head_back) };
        // The arm rings' silhouette edges are not measured: they are set below as a line through the
        // shoulder joint. Only their depth comes from the flesh.
        recipes[arm_hi] = new recipe{ name = "L arm", anchor = js("LeftArm"), wrap = js("LeftShoulder"), n = side, s = up, d = depth, kind = fit.joint, girth = bone("LeftArm"), body = true };
        recipes[elbow_hi] = new recipe{ name = "L elbow", anchor = js("LeftForeArm"), wrap = js("LeftArm"), n = side, s = up, d = depth, kind = fit.joint, girth = bone("LeftArm"), hi = tune.elbow_hi };
        // The wrist rings hand the arms over to the hands, which measure them: their extents are
        // overwritten below, since both need flesh windows the generic measure cannot express.
        recipes[wrist_hi] = new recipe{ name = "L wrist", anchor = js("LeftHand"), wrap = js("LeftHand"), n = side, s = up, d = depth, kind = fit.joint, girth = bone("LeftArm") };
        recipes[arm_lo] = new recipe{ name = "R arm", anchor = js("RightArm"), wrap = js("RightShoulder"), n = -side, s = up, d = depth, kind = fit.joint, girth = bone("RightArm"), body = true };
        recipes[elbow_lo] = new recipe{ name = "R elbow", anchor = js("RightForeArm"), wrap = js("RightArm"), n = -side, s = up, d = depth, kind = fit.joint, girth = bone("RightArm"), hi = tune.elbow_hi };
        recipes[wrist_lo] = new recipe{ name = "R wrist", anchor = js("RightHand"), wrap = js("RightHand"), n = -side, s = up, d = depth, kind = fit.joint, girth = bone("RightArm") };
        // The spine ring is the torso panel's bottom edge, level across the Spine joint; it wraps
        // whatever crosses that height, so the waist. The pelvis below it is posts, not a ring. The
        // two rings above it, on Spine1 and Spine2, section the belly and the lower chest the same
        // way, so the torso panel gets a rung at every spine joint up to the sternum.
        recipes[spine] = new recipe{ name = "spine", anchor = js("Spine"), wrap = js("Hips"), n = up, s = side, d = depth, kind = fit.joint, body = true };
        // The two are intermediate rings: they keep only their plane, at their joint, and take their
        // corners off the lines from the armpits down to the spine ring (below, between). What
        // measure() reads off the flesh for them goes unused.
        recipes[spine1] = new recipe{ name = "spine1", anchor = js("Spine1"), wrap = js("Hips"), n = up, s = side, d = depth, kind = fit.joint };
        recipes[spine2] = new recipe{ name = "spine2", anchor = js("Spine2"), wrap = js("Hips"), n = up, s = side, d = depth, kind = fit.joint };
        // Each leg's rings see only that leg's flesh, so the two legs' rings sit clear of each other
        // however close the legs stand. s is side on both, so the outer edge is hi on the left knee
        // and lo on the right. Their width follows the hip bone, the one the hip post follows: a
        // wider pelvis is a thicker leg, down to the toes. `[N22]`
        recipes[knee_hi] = new recipe{ name = "L knee", anchor = js("LeftLeg"), wrap = js("LeftUpLeg"), n = -up, s = side, d = depth, kind = fit.joint, girth = bone("LeftUpLeg"), hi = tune.knee_out, back = (tune.knee_back, tune.knee_back) };
        recipes[knee_lo] = new recipe{ name = "R knee", anchor = js("RightLeg"), wrap = js("RightUpLeg"), n = -up, s = side, d = depth, kind = fit.joint, girth = bone("RightUpLeg"), lo = tune.knee_out, back = (tune.knee_back, tune.knee_back) };
        // The ankle ring leans back through the Foot joint -- heel down and behind, the crease of
        // the instep up and ahead -- so its frame is the knee's turned about the side axis by the
        // tilt, part way toward the toe ring's. That one stands upright across the ball of the
        // foot: n along the foot, d up, so its front is the top of the foot and its back the sole.
        var lean = tune.ankle_tilt * Mathf.Deg2Rad;
        var ankle_n = -Mathf.Cos(lean) * up + Mathf.Sin(lean) * depth;
        var ankle_d = Mathf.Cos(lean) * depth + Mathf.Sin(lean) * up;
        recipes[ankle_hi] = new recipe{ name = "L ankle", anchor = js("LeftFoot"), wrap = js("LeftLeg"), n = ankle_n, s = side, d = ankle_d, kind = fit.joint, girth = bone("LeftUpLeg"), front = (tune.ankle_front, tune.ankle_front), back = (tune.ankle_back, tune.ankle_back) };
        recipes[toe_hi] = new recipe{ name = "L toe", anchor = js("LeftToeBase"), wrap = js("LeftFoot"), n = depth, s = side, d = up, kind = fit.joint, girth = bone("LeftUpLeg") };
        recipes[ankle_lo] = new recipe{ name = "R ankle", anchor = js("RightFoot"), wrap = js("RightLeg"), n = ankle_n, s = side, d = ankle_d, kind = fit.joint, girth = bone("RightUpLeg"), front = (tune.ankle_front, tune.ankle_front), back = (tune.ankle_back, tune.ankle_back) };
        recipes[toe_lo] = new recipe{ name = "R toe", anchor = js("RightToeBase"), wrap = js("RightFoot"), n = depth, s = side, d = up, kind = fit.joint, girth = bone("RightUpLeg") };

        // Widen a measured span by the margin, about its middle.
        static (float lo, float hi) inflate(float lo, float hi){
            var mid = (lo + hi) * 0.5f;
            var half = (hi - lo) * 0.5f * (1f + margin);
            return (mid - half, mid + half);
        }

        // Ring geometry is native (rig root local), so the recipes' scene-unit reach converts here.
        var scale = root.lossyScale.x;

        // The trunk's depth, one pair of lines for the whole torso: the flesh of the Hips subtree
        // less the limbs and the head, its depth extents inflated, off the Hips joint. Seen from the
        // side the torso is a box -- the arm rings, the neck post, the spine ring and the pelvis
        // posts stand on these two lines, and the rings and posts between them follow by
        // construction (between). `[N28]`
        var hips = index["Hips"];
        var trunk = subtree(hips, parent)
            .Except(js("LeftUpLeg", "RightUpLeg", "LeftArm", "RightArm", "Head").SelectMany(j => subtree(j, parent)))
            .SelectMany(j => flesh[j]).ToArray();
        var (trunk_back, trunk_front) = inflate(trunk.Min(p => Vector3.Dot(p, depth)), trunk.Max(p => Vector3.Dot(p, depth)));
        var trunk_seat = Vector3.Dot(rest[hips], depth);
        var body_front = trunk_front - trunk_seat + tune.body_front / scale;
        var body_back = trunk_seat - trunk_back + tune.body_back / scale;

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
                d_hi_anchor = r.body ? new[]{ hips } : r.anchor,
                d_lo_anchor = r.body ? new[]{ hips } : r.anchor,
                n = r.n,
                s = r.s,
                d = r.d,
                along_hi = past + r.outward_hi / scale,
                along_lo = past + r.outward_lo / scale,
                s_lo = lo.Min(j => Vector3.Dot(rest[j], r.s)) - lo_s + r.lo / scale,
                s_hi = hi_s - hi.Max(j => Vector3.Dot(rest[j], r.s)) + r.hi / scale,
                girth = r.girth,
                girth_d = new cage_span[0],
                between = new int[0],
                hi_between = new int[0],
                lo_between = new int[0],
                hi_front = r.body ? body_front : hi_d - anchors.Max(p => Vector3.Dot(p, r.d)) + r.front.hi / scale,
                lo_front = r.body ? body_front : hi_d - anchors.Max(p => Vector3.Dot(p, r.d)) + r.front.lo / scale,
                hi_back = r.body ? body_back : anchors.Min(p => Vector3.Dot(p, r.d)) - lo_d + r.back.hi / scale,
                lo_back = r.body ? body_back : anchors.Min(p => Vector3.Dot(p, r.d)) - lo_d + r.back.lo / scale,
            };
        }

        var rings = recipes.Select(measure).ToArray();

        // Seen from the front, an arm ring is a line through the shoulder joint: the raglan seam,
        // leaning in at the top so it runs from the armpit up over the trapezius, of a declared
        // length with the joint at its middle. The two edges are its ends -- up and in for the top,
        // down and out for the armpit -- so the ring is not measured across, only through (depth).
        // The seam's length follows the clavicle, the bone ending on the ring's own anchor (the
        // recipe's girth): a wider shoulder girdle is a thicker shoulder, and the elbow and wrist
        // rings below carry the same ratio down the arm.
        var raglan = tune.arm_tilt * Mathf.Deg2Rad;
        var seam = tune.arm_length * 0.5f / scale;
        foreach(var slot in new[]{ arm_hi, arm_lo }){
            rings[slot].s_hi = seam * Mathf.Cos(raglan);
            rings[slot].s_lo = seam * Mathf.Cos(raglan);
            rings[slot].along_hi = -seam * Mathf.Sin(raglan);
            rings[slot].along_lo = seam * Mathf.Sin(raglan);
        }

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
        int post(string name, int[] anchor, float[] weight, Vector3 reach, Vector3 d, int[] d_anchor, float d_lo, float d_hi, cage_span[] girth){
            posts.Add(new cage_post{
                name = name, anchor = anchor, weight = weight, reach = reach,
                d = d, d_lo_anchor = d_anchor, d_hi_anchor = d_anchor, d_lo = d_lo, d_hi = d_hi, girth = girth, girth_d = new cage_span[0],
                hi_between = new int[0], lo_between = new int[0], hi_mean = new int[0], lo_mean = new int[0],
            });
            return posts.Count - 1;
        }

        // The midline: one post wherever a front or back panel's rungs cross the middle, so the
        // torso and head come as two halves. A post's ends take the depth anchors and reach of the
        // ring whose band it closes, so it stays level with that ring's edges however they move.
        // On a ring across the body: exactly at the midpoint of its edges, anchored on the joint
        // that places them and offset by whatever the rest midpoint is off that joint. It scales
        // with the ring's girth, since that offset is the ring's own reach along n: scale the ring
        // alone and the midline stays behind, folding the cap. `[N23]`
        var rest_corners = ring_corners(new cage_constants{ rings = rings }, rest);
        void midline(int slot, string joint){
            var r = rings[slot];
            var anchor = js(joint);
            var mid = (rest_corners[slot * 4 + hi_front] + rest_corners[slot * 4 + lo_front]) * 0.5f;
            var off = mid - rest[anchor[0]];
            at[(slot, edge.mid)] = post(r.name + " mid", anchor, new[]{ 1f }, off - r.d * Vector3.Dot(off, r.d), r.d, r.anchor_hi.Concat(r.anchor_lo).Distinct().ToArray(),
                (r.hi_back + r.lo_back) * 0.5f, (r.hi_front + r.lo_front) * 0.5f, r.girth);
        }
        midline(crown, "Head");
        midline(head, "Head");
        // The bottom of the neck's V, on the Neck joint itself, closing the rung of the arm rings'
        // top edges: as deep as the trunk, like those edges. `[N28]`
        at[(neck, edge.mid)] = post("neck mid", js("Neck"), new[]{ 1f }, Vector3.zero, depth, new[]{ hips }, body_back, body_front, new cage_span[0]);

        // A midline post with no depth of its own: each end is where the line between two placed
        // control points crosses the midline plane through its joint. The sternum is the armpits'
        // line -- the arm rings' bottom edges, front to front and back to back -- so the chest band
        // is flat by construction, and the spine1 and spine2 posts are their own ring's front and
        // back edges, which those rings take off the armpit-to-spine lines below. `[N27]`
        int crossing(string name, string joint, int[] front, int[] back){
            var p = post(name, js(joint), new[]{ 1f }, Vector3.zero, depth, js(joint), 0f, 0f, new cage_span[0]);
            posts[p].hi_between = front;
            posts[p].lo_between = back;
            posts[p].between_axis = side;
            return p;
        }
        at[(sternum, edge.mid)] = crossing("sternum mid", "Spine3",
            new[]{ arm_hi * 4 + lo_front, arm_lo * 4 + lo_front }, new[]{ arm_hi * 4 + lo_back, arm_lo * 4 + lo_back });
        midline(spine, "Spine");
        foreach(var (slot, joint) in new[]{ (spine1, "Spine1"), (spine2, "Spine2") }){
            at[(slot, edge.mid)] = crossing(rings[slot].name + " mid", joint,
                new[]{ slot * 4 + hi_front, slot * 4 + lo_front }, new[]{ slot * 4 + hi_back, slot * 4 + lo_back });
        }

        // The pelvis, a palm the legs branch from. The crotch hangs below the Hips joint; each outer
        // hip post continues the crotch->UpLeg line past its UpLeg by hip_out of that span, as the
        // (1+f, -f) combination of UpLeg and Hips plus that share of the drop -- so widening one hip
        // carries its post outward and tilts that leg's ring, while the crotch stays put. The three
        // share one depth, the trunk's, the way a hand's posts share the plate: anchored on Hips so
        // the pelvis panels stay a flat slab between the waist ring and the thighs. `[N28]`
        // The crotch hangs as far below the Hips joint as the hips are wide: its drop follows the
        // span between the two UpLeg joints across side. The hip bones are purely lateral, so that
        // span is the two bones' sum and its ratio to rest the mean of their two ratios -- one hip
        // edited alone moves it half way. The outer hip posts take the same ratio on their own reach,
        // which is f times the drop, so they stay on the crotch->UpLeg line extended. `[N26]`
        var hip_width = new[]{ new cage_span{
            a = js("LeftUpLeg"), b = js("RightUpLeg"), axis = side,
            rest = Vector3.Dot(rest[index["LeftUpLeg"]] - rest[index["RightUpLeg"]], side),
        } };
        int pelvis_post(string name, int[] anchor, float[] weight, Vector3 reach){
            return post(name, anchor, weight, reach, depth, new[]{ hips }, body_back, body_front, hip_width);
        }
        var drop = up * (tune.crotch_drop / scale);
        var f = tune.hip_out;
        at[(hip, edge.mid)] = pelvis_post("crotch", new[]{ hips }, new[]{ 1f }, -drop);
        at[(hip, edge.hi)] = pelvis_post("L hip", js("LeftUpLeg", "Hips"), new[]{ 1f + f, -f }, drop * f);
        at[(hip, edge.lo)] = pelvis_post("R hip", js("RightUpLeg", "Hips"), new[]{ 1f + f, -f }, drop * f);

        // The torso's sides are straight lines from the armpits down to the hips, and the spine
        // ring's edges lie on them: each edge takes the side coordinate of that line where the ring's
        // plane crosses it, instead of the reach measured off its own flesh. The flanks are concave,
        // so the line clears them with room to spare (measured: 4 to 6 cm a side), and the torso
        // widens or narrows as one piece with whatever the shoulders and hips do -- no width of its
        // own to tune. What it gives up is the waist as a shape the cage knows. `[N21]`
        rings[spine].hi_between = new[]{ pair(at[(hip, edge.hi)]).hi, arm_hi * 4 + lo_front };
        rings[spine].lo_between = new[]{ pair(at[(hip, edge.lo)]).hi, arm_lo * 4 + lo_front };

        // Above the spine ring the torso is a frustum: four straight lines from the armpit corners
        // down to the spine ring's corners, and the spine1 and spine2 rings are where their planes
        // cut it -- no width or depth of their own. `[N27]`
        foreach(var slot in new[]{ spine1, spine2 }){
            rings[slot].between = new[]{
                arm_hi * 4 + lo_front, spine * 4 + hi_front,
                arm_hi * 4 + lo_back, spine * 4 + hi_back,
                arm_lo * 4 + lo_back, spine * 4 + lo_back,
                arm_lo * 4 + lo_front, spine * 4 + lo_front,
            };
        }

        // One foot, past its ankle ring. Its heights are all the Foot joint's: the sole is level --
        // the toe ring's bottom edge and the tips' lower ends sit at the height of the ankle ring's
        // bottom, the heel -- and the tops, read off the flesh, hang on the same joint rather than
        // on ToeBase. The foot bone points down as well as forward, so a longer one carries ToeBase
        // toward the sole (it would reach it at x1.62); a top that followed it would squeeze the
        // section against the level sole, while off the Foot the foot only lengthens. `[N14]` `[N30]`
        // The toes end past ToeBase with no joint to stand on, so the tip is a
        // fingertip's ring: a post on each side of the toes on a virtual end bone -- (1+f, -f) of
        // ToeBase and Foot, f the toes' reach beyond ToeBase as a share of the foot bone -- so
        // lengthening the foot carries the toes out with it. Across, the tips follow the hip bone
        // as the leg's rings do, so the cap stays as wide as the toe ring it closes. `[N22]`
        void foot(string prefix, string tag, int ankle, int toe, int station){
            var joint = index[prefix + "Foot"];
            var ball = index[prefix + "ToeBase"];
            var hip_bone = bone(prefix + "UpLeg");
            var floor = Vector3.Dot(rest[joint] - rest_corners[ankle * 4 + lo_back], up);
            var below = Vector3.Dot(rest[joint] - rest[ball], up);    // how far ToeBase sits under the Foot joint
            rings[toe].d_lo_anchor = new[]{ joint };
            rings[toe].hi_back = rings[toe].lo_back = floor;
            rings[toe].d_hi_anchor = new[]{ joint };
            rings[toe].hi_front -= below;
            rings[toe].lo_front -= below;

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
                    d_lo_anchor = new[]{ joint }, d_hi_anchor = new[]{ joint }, d_lo = floor, d_hi = top - Vector3.Dot(rest[joint], up),
                    girth = hip_bone, girth_d = new cage_span[0], hi_between = new int[0], lo_between = new int[0], hi_mean = new int[0], lo_mean = new int[0],
                });
                return posts.Count - 1;
            }
            at[(station, edge.hi)] = add(side * (wide_hi - Vector3.Dot(end, side)));
            at[(station, edge.lo)] = add(side * (wide_lo - Vector3.Dot(end, side)));
        }
        foot("Left", "L", ankle_hi, toe_hi, tip_hi);
        foot("Right", "R", ankle_lo, toe_lo, tip_lo);

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
            // it, which is what keeps the side panels axis aligned and equally tall. It follows what
            // the wrist ring's silhouette follows -- the clavicle, down the arm -- so the hand is as
            // thick as the arm it ends, with no step at the wrist. `[N25]`
            var (plate_lo, plate_hi) = inflate(skin.Min(p => Vector3.Dot(p, d)), skin.Max(p => Vector3.Dot(p, d)));
            var seat = Vector3.Dot(rest[wrist], d);
            var thick = rings[slot].girth;

            int add(string name, int[] anchor, float[] weight, Vector3 reach, cage_span[] girth){
                posts.Add(new cage_post{
                    name = name, anchor = anchor, weight = weight, reach = reach,
                    d = d, d_lo_anchor = new[]{ wrist }, d_hi_anchor = new[]{ wrist }, d_lo = seat - plate_lo, d_hi = plate_hi - seat,
                    girth = girth, girth_d = thick, hi_between = new int[0], lo_between = new int[0], hi_mean = new int[0], lo_mean = new int[0],
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
            // Across, the wrist follows the palm's breadth: the thumb's knuckle to the pinky's, along
            // s, which the metacarpals spread as they lengthen. `[N25]`
            var thumb_root = index[prefix + "Thumb2"];
            var pinky_root = index[prefix + "Pinky1"];
            rings[slot].girth_d = new[]{ new cage_span{
                a = new[]{ thumb_root }, b = new[]{ pinky_root }, axis = s, rest = Vector3.Dot(rest[thumb_root] - rest[pinky_root], s),
            } };

            // The six control points that carve the palm outline into finger branches. The thumb
            // and pinky ends come from the hand's own width; the four valleys sit halfway between
            // neighbouring finger roots, pushed away from the wrist past the knuckle line.
            var (wide_lo, wide_hi) = inflate(skin.Min(p => Vector3.Dot(p, s)), skin.Max(p => Vector3.Dot(p, s)));
            var thumb = index[prefix + "Thumb2"];
            var pinky = index[prefix + "Pinky1"];

            // The palm's points keep their rest offsets: the back of the hand is one plate, not a
            // section of any finger.
            var cp = new int[6];
            cp[0] = add($"{tag} thumb out", new[]{ thumb }, new[]{ 1f }, s * (wide_hi - Vector3.Dot(rest[thumb], s) + tune.thumb_out / scale), new cage_span[0]);
            for(var f = 0; f < 4; f++){
                // The thumb branches off at its own second joint, the rest at their roots.
                var a = f == 0 ? thumb : index[prefix + fingers[f] + "1"];
                var b = index[prefix + fingers[f + 1] + "1"];
                var span = (rest[a] + rest[b]) * 0.5f - rest[wrist];
                var away = (span - d * Vector3.Dot(span, d)).normalized;
                cp[f + 1] = add($"{tag} {fingers[f].ToLower()}|{fingers[f + 1].ToLower()}", new[]{ a, b }, new[]{ 0.5f, 0.5f }, away * (tune.valley_reach / scale), new cage_span[0]);
            }
            cp[5] = add($"{tag} pinky out", new[]{ pinky }, new[]{ 1f }, s * (wide_lo - Vector3.Dot(rest[pinky], s) - tune.pinky_out / scale), new cage_span[0]);

            // Rings up one finger, past the branch ring it shares with its neighbours: one on every
            // joint out from the second, then one more on a virtual end bone, since the rig stops at
            // the last phalanx. Each ring straddles its own bone direction rather than the s axis,
            // so a splayed finger is still enclosed. The thumb is one ring short: its branch ring
            // already sits at the knuckle. Each ring's width follows the phalanx it stands on -- the
            // bone from the previous joint to its own, the last phalanx for the end ring -- so a
            // longer finger is a thicker one, segment by segment. `[N24]`
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
                    var phalanx = bone(bones[e.j].name);
                    return (hi: add(name, anchor, weight, perp * (r_hi - at + (extra.hi + tune.finger_out) / scale), phalanx),
                            lo: add(name, anchor, weight, perp * (r_lo - at - (extra.lo + tune.finger_out) / scale), phalanx));
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

        // The depth is restored section by section once the cage is placed (restore). The recipe
        // deformed the silhouette -- the seam by the clavicle, the torso's sides by the hips and the
        // armpits, a limb's rings by its root bone -- and left every depth reach what the rest
        // measured, so each section multiplies its reaches by its own width ratio. Each stands alone:
        // the trunk's shared box holds at rest and parts where an edit lands. A reach scales from
        // the joint it hangs on: the trunk's from the Hips, the box's own seat, whatever ring they
        // are on; a limb ring's from its own depth anchors, which is the Foot for all three stations
        // of a foot `[N30]`, so the heel corners, the toe ring's sole and the tips' lower ends drop
        // as one level plane; the head's two rings from the Head joint, so the head is as deep as
        // the stature makes it wide. The wrist ring and the hand are not restored: their three
        // axes are already declared `[N25]`. The rest width is read off the rest cage placed
        // without any restore. `[N29]`
        var rest_placed = control_points(new cage_constants{ rings = rings, posts = posts.ToArray(), sections = new cage_section[0], gates = new cage_gate[0] }, rest);
        cage_section section(string name, int[] hi, int[] lo, int[] front, int[] back, int front_seat, int back_seat, Vector3 d){
            var s = new cage_section{ name = name, hi = hi, lo = lo, front = front, back = back, front_seat = front_seat, back_seat = back_seat, d = d };
            s.rest_width = width(s, rest_placed);
            return s;
        }
        cage_section ring_section(int slot, int front_seat, int back_seat){
            int[] corners(params int[] c){
                return c.Select(x => slot * 4 + x).ToArray();
            }
            return section(rings[slot].name, corners(hi_front, hi_back), corners(lo_front, lo_back), corners(hi_front, lo_front), corners(hi_back, lo_back), front_seat, back_seat, rings[slot].d);
        }
        // A ring off its own joint: a limb's, or the head's two, whose width is the stature's `[N23]`.
        cage_section joint_section(int slot){
            return ring_section(slot, rings[slot].d_hi_anchor.Single(), rings[slot].d_lo_anchor.Single());
        }
        // Two sections that are post pairs: the tilted hip ring, an outer hip post beside the crotch,
        // of which only the hip post scales -- the crotch takes the mean of both sides below -- and
        // the toe cap, the two tip posts, both of which scale off the toe ring's two joints.
        cage_section hip_section(edge e){
            var p = at[(hip, e)];
            var (front, back) = pair(p);
            var crotch = pair(at[(hip, edge.mid)]);
            return section(posts[p].name, new[]{ front, back }, new[]{ crotch.hi, crotch.lo }, new[]{ front }, new[]{ back }, hips, hips, posts[p].d);
        }
        cage_section tip_section(int station){
            var p = at[(station, edge.hi)];
            var (hi_top, hi_sole) = pair(p);
            var (lo_top, lo_sole) = pair(at[(station, edge.lo)]);
            return section(posts[p].name, new[]{ hi_top, hi_sole }, new[]{ lo_top, lo_sole }, new[]{ hi_top, lo_top }, new[]{ hi_sole, lo_sole },
                posts[p].d_hi_anchor.Single(), posts[p].d_lo_anchor.Single(), posts[p].d);
        }
        var sections = new[]{ spine, spine1, spine2, arm_hi, arm_lo }.Select(slot => ring_section(slot, hips, hips))
            .Concat(new[]{ edge.hi, edge.lo }.Select(hip_section))
            .Concat(new[]{ elbow_hi, elbow_lo, knee_hi, knee_lo, ankle_hi, ankle_lo, toe_hi, toe_lo }.Select(joint_section))
            .Concat(new[]{ tip_hi, tip_lo }.Select(tip_section))
            .Concat(new[]{ crown, head }.Select(joint_section))
            .ToArray();

        // And every post closing a rung takes the depth of the corners it closes, so it follows
        // their restored sections: the V's bottom and the sternum the two arm rings' top and bottom
        // edges, each midline post on a ring -- the crown's, the head's, the three spine rings' --
        // its own edges, the crotch the two hip posts. A post between two sections belongs to both,
        // hence the mean; at rest every one of them already stands there. `[N29]`
        void mean(int p, int[] front, int[] back){
            posts[p].hi_mean = front;
            posts[p].lo_mean = back;
        }
        mean(at[(neck, edge.mid)], new[]{ arm_hi * 4 + hi_front, arm_lo * 4 + hi_front }, new[]{ arm_hi * 4 + hi_back, arm_lo * 4 + hi_back });
        mean(at[(sternum, edge.mid)], new[]{ arm_hi * 4 + lo_front, arm_lo * 4 + lo_front }, new[]{ arm_hi * 4 + lo_back, arm_lo * 4 + lo_back });
        foreach(var slot in new[]{ crown, head, spine, spine1, spine2 }){
            mean(at[(slot, edge.mid)], new[]{ slot * 4 + hi_front, slot * 4 + lo_front }, new[]{ slot * 4 + hi_back, slot * 4 + lo_back });
        }
        mean(at[(hip, edge.mid)], new[]{ pair(at[(hip, edge.hi)]).hi, pair(at[(hip, edge.lo)]).hi }, new[]{ pair(at[(hip, edge.hi)]).lo, pair(at[(hip, edge.lo)]).lo });

        // The head sits on the shoulders. Shorten the neck and the parting plane sinks until it
        // passes the arm rings' top edges:
        // the seam crosses the jaw and the neck panels fold through the head. Fitting the two ever
        // more closely cannot settle that, since the mesh decides how much room there is and some
        // mesh will always take it away, so the head is lifted back out of the shoulders instead.
        // The ring, the crown over it and the midline posts they carry move as one, keeping the
        // shape of the box; at rest the head already clears the shoulders, so nothing moves. `[N20]`
        // What is read off the head is its lowest corner, and the slack is how far below the
        // shoulders that corner may still go. With none the head stops the moment it touches and
        // the neck can barely shorten; measured, the panels only start folding once the ring sinks
        // about two fifths of its own height past them, and the tune sits inside that.
        var head_ring = Enumerable.Range(head * 4, 4).ToArray();
        var head_box = new[]{ crown, head }.SelectMany(r => Enumerable.Range(r * 4, 4))
            .Concat(new[]{ (crown, edge.mid), (head, edge.mid) }
                .SelectMany(e => new[]{ post_hi, post_lo }.Select(end => rings.Length * 4 + at[e] * 2 + end)))
            .ToArray();
        var shoulder_tops = new[]{ arm_hi, arm_lo }
            .SelectMany(r => new[]{ hi_front, hi_back }.Select(c => r * 4 + c)).ToArray();

        // And the shoulders stay beside the head. Shorten a clavicle and the arm ring rides in with
        // its joint until its top edge, the one drawn in over the trapezius, stands inside the
        // head's silhouette: the wall down from the head's side leans in and the neck panel folds
        // onto the head panel. What is read is that edge; what moves is the whole ring, the way the
        // head is lifted as a box, so the ring keeps its raglan tilt and merely stops coming in --
        // pushing the one edge out would tilt it the other way. The elbow ring stays where its bone
        // puts it. The head is read for its widest corner on that side, so
        // its tilt does not enter; the two gates move along side and the head gate along up, so
        // neither reads what the other moves and their order is immaterial. At rest the edge clears
        // the head by several centimetres and nothing moves. `[N20]`
        cage_gate arm_beside_head(string name, int arm, Vector3 axis){
            return new cage_gate{
                name = name, moved = Enumerable.Range(arm * 4, 4).ToArray(),
                probe = new[]{ hi_front, hi_back }.Select(c => arm * 4 + c).ToArray(),
                floor = head_ring, slack = tune.arm_gate_slack / scale, axis = axis,
            };
        }

        // And each knee stays on its own side of the crotch. The hip bone is purely lateral, so its
        // length is the pelvis's half width: shorten it and the whole leg walks in, ring and all,
        // until the knee ring's inner edge crosses the midline and the two legs' walls pass through
        // each other. What is read is that edge, what moves is the whole ring -- push the inner edge
        // alone and the outer one keeps coming in, thinning the thigh. The floor is the crotch post,
        // which no gate moves, so the two sides never read what the other did. The slack is negative
        // here: stopped flush both rings stand on the midline and the panels between them still
        // graze, so what is asked for is a gap. The ankle needs no gate -- its ring never reaches the
        // midline, and what was pierced is the shin panel's knee end, which this carries. `[N20]`
        cage_gate knee_beside_crotch(string name, int knee, int[] inner, Vector3 axis){
            return new cage_gate{
                name = name, moved = Enumerable.Range(knee * 4, 4).ToArray(),
                probe = inner.Select(c => knee * 4 + c).ToArray(),
                floor = new[]{ post_hi, post_lo }.Select(end => rings.Length * 4 + at[(hip, edge.mid)] * 2 + end).ToArray(),
                slack = tune.knee_gate_slack / scale, axis = axis,
            };
        }

        // And the torso stays off the hips. The spine ring hangs on the Spine joint, which the pelvis
        // bone carries down, while the hips hang on UpLeg and Hips, which that bone does not move:
        // shorten the pelvis far enough and the ring sinks past the hips beside it, the pelvis panel
        // folds up through the torso, and that is what the length sweep found first. The ring's four
        // corners and the midline post under them stop together, so the bar stays level -- holding
        // the two side edges alone left the midline going down after them, and the V that opened was
        // what the thigh panel crossed once its hip post had walked inward too. The hips themselves
        // are untouched. The slack is negative again: flush on the hips the pelvis panel between them
        // flattens and grazes the torso, so what is asked for is a gap. At rest the ring stands
        // several centimetres above and nothing moves. `[N16]` `[N20]`
        var spine_bar = Enumerable.Range(spine * 4, 4)
            .Concat(new[]{ post_hi, post_lo }.Select(end => rings.Length * 4 + at[(spine, edge.mid)] * 2 + end))
            .ToArray();
        var hip_posts = new[]{ (hip, edge.hi), (hip, edge.lo) }
            .SelectMany(e => new[]{ post_hi, post_lo }.Select(end => rings.Length * 4 + at[e] * 2 + end)).ToArray();

        var k = new cage_constants{
            clearance = clearance / scale,
            gates = new[]{
                new cage_gate{ name = "head above arms", moved = head_box, probe = head_ring, floor = shoulder_tops, slack = tune.head_gate_slack / scale, axis = up },
                arm_beside_head("L arm beside head", arm_hi, side),
                arm_beside_head("R arm beside head", arm_lo, -side),
                new cage_gate{ name = "spine above hips", moved = spine_bar, probe = spine_bar, floor = hip_posts, slack = tune.spine_gate_slack / scale, axis = up },
                knee_beside_crotch("L knee beside crotch", knee_hi, new[]{ lo_back, lo_front }, side),
                knee_beside_crotch("R knee beside crotch", knee_lo, new[]{ hi_front, hi_back }, -side),
            },
            joint_name = bones.Select(t => t.name).ToArray(),
            joint_parent = parent,
            joint_dir = dir,
            joint_rest_len = rest_len,
            rings = rings,
            posts = posts.ToArray(),
            sections = sections,
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

    // Which of the given points the shell does not hold with room to spare: outside it, or inside
    // but nearer a face than the clearance. The margin is the rule rather than the side, because a
    // vertex on a face is what actually breaks the coordinates and nothing at editing time can
    // watch for that one case -- a mesh crossing a cage face with no vertex landing on it is luck,
    // not safety `[N18]`. The body at a set of bone lengths is the rest mesh mapped through the
    // cage those lengths build, so both arguments come out of the same lengths and the check is a
    // pure function of them: the inspector button runs it on the current lengths, the sweep in
    // tools/cage_sweep headless over thousands of them.
    public static List<int> exposed(Vector3[] pts, Vector3[] cage_verts, cage_constants k){
        return Enumerable.Range(0, pts.Length).Where(i => !held(pts[i], cage_verts, k)).ToList();
    }

    // The cage is a closed shell, so an odd crossing count along any ray means the point is in; the
    // ray direction is arbitrary, skewed off the cardinal axes the panels are aligned to. The same
    // pass takes the distance to the nearest face, which is the margin the point is held by.
    static bool held(Vector3 p, Vector3[] v, cage_constants k){
        var dir = new Vector3(0.5773f, 0.3313f, 0.7449f).normalized;
        var hits = 0;
        var gap = float.MaxValue;
        for(var t = 0; t < k.tris.Length; t += 3){
            var a = v[k.tris[t]];
            var b = v[k.tris[t + 1]];
            var c = v[k.tris[t + 2]];
            if(pierces(p, dir, float.PositiveInfinity, a, b, c)){
                hits++;
            }
            gap = Mathf.Min(gap, span(p, a, b, c));
        }
        return hits % 2 == 1 && gap >= k.clearance;
    }

    // Distance from a point to a triangle: the nearest of its face, its three edges and its three
    // corners, picked by the region the point projects into.
    static float span(Vector3 p, Vector3 a, Vector3 b, Vector3 c){
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if(d1 <= 0f && d2 <= 0f){
            return ap.magnitude;
        }

        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if(d3 >= 0f && d4 <= d3){
            return bp.magnitude;
        }

        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if(d6 >= 0f && d5 <= d6){
            return cp.magnitude;
        }

        var along_ab = d1 * d4 - d3 * d2;
        if(along_ab <= 0f && d1 >= 0f && d3 <= 0f){
            return (ap - ab * (d1 / (d1 - d3))).magnitude;
        }

        var along_ac = d5 * d2 - d1 * d6;
        if(along_ac <= 0f && d2 >= 0f && d6 <= 0f){
            return (ap - ac * (d2 / (d2 - d6))).magnitude;
        }

        var along_bc = d3 * d6 - d5 * d4;
        if(along_bc <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f){
            return (bp - (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)))).magnitude;
        }

        var area = 1f / (along_bc + along_ac + along_ab);
        return (ap - ab * (along_ac * area) - ac * (along_ab * area)).magnitude;
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
    // name; a branch ring -- hip beside crotch -- is two posts the tables put
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
