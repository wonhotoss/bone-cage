using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Bone-length driven cage generation.
//
// bake() (editor) reads the rest mesh + skeleton once and distils everything the
// generator needs into cage_constants (per-joint FK data + per-ring cross-sections).
// build() (runtime) is a pure function of the current bone lengths + those constants:
// it re-runs forward kinematics for the joint centers and emits a fixed-topology
// quad-tube mesh. Cross-sections are constant, so a longer bone just stretches its
// tube. All geometry lives in root-bone (Hips) local space; the cage GameObject is a
// child of that root, so the scene's x100 scale is inherited.

[Serializable]
public class cage_constants{
    public float scale;

    // Joints, in parent-before-child order, for forward kinematics.
    public string[] joint_name;
    public int[] joint_parent;      // index into joint_name; -1 for the root
    public Vector3[] joint_dir;     // unit rest direction parent->joint (Hips space, invariant to length edits)
    public float[] joint_rest_len;  // native rest distance parent->joint

    // Rings: each contributes 4 corner vertices. A ring either sits on a joint center
    // or is a terminal extension reaching past a leaf joint (skull, fingertips, toes).
    public int[] ring_joint;        // joint index for the center, or -1 for an extension ring
    public int[] ring_from_joint;   // extension: joint it grows from (-1 otherwise)
    public Vector3[] ring_ext_dir;  // extension: growth direction (Hips space)
    public float[] ring_ext_len;    // extension: growth length (native)
    public Vector3[] ring_u;        // cross-section axes (unit, Hips space)
    public Vector3[] ring_v;
    public float[] ring_hu;         // cross-section half-extents (native, includes margin)
    public float[] ring_hv;

    // Chains laid out contiguously in the ring arrays; consecutive rings form segments.
    public int[] chain_start;
    public int[] chain_count;

    public int[] tris;              // indices into the 4*ring_count corner vertices
}

public static class cage{
    // build reconstructs the joint centers from the supplied lengths and lays the baked
    // rings on top. Lengths are native (joint.localPosition.magnitude), keyed by joint name.
    public static Mesh build(IReadOnlyDictionary<string, float> lengths, cage_constants k){
        var verts = ring_corners(k, joint_centers(lengths, k));

        var mesh = new Mesh{ name = "cage" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
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

    static Vector3[] ring_corners(cage_constants k, Vector3[] jc){
        var r = k.ring_joint.Length;
        var verts = new Vector3[r * 4];
        for(var i = 0; i < r; i++){
            var c = k.ring_joint[i] >= 0
                ? jc[k.ring_joint[i]]
                : jc[k.ring_from_joint[i]] + k.ring_ext_dir[i] * k.ring_ext_len[i];

            var u = k.ring_u[i] * k.ring_hu[i];
            var v = k.ring_v[i] * k.ring_hv[i];
            verts[i * 4 + 0] = c + u + v;
            verts[i * 4 + 1] = c - u + v;
            verts[i * 4 + 2] = c - u - v;
            verts[i * 4 + 3] = c + u - v;
        }
        return verts;
    }

#if UNITY_EDITOR
    // Skeleton the cage is built around. Joints are listed parent-before-child; chains
    // reference a subset and drive the visible tubes. Terminal chains extend past their
    // leaf joint to wrap the flesh (skull / fingers / toes) that reaches beyond it.
    static readonly string[] joints = {
        "Hips",
        "Spine", "Spine1", "Spine2", "Spine3",
        "Neck", "Neck1", "Head",
        "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand", "LeftHandMiddle1",
        "RightShoulder", "RightArm", "RightForeArm", "RightHand", "RightHandMiddle1",
        "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase",
        "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase",
    };

    class chain_def{
        public string[] ring_joints;
        public bool extend_tip;     // grow a terminal ring past the last joint
    }

    static readonly chain_def[] chains = {
        new chain_def{ ring_joints = new[]{ "Hips", "Spine", "Spine1", "Spine2", "Spine3" }, extend_tip = false },
        new chain_def{ ring_joints = new[]{ "Neck", "Neck1", "Head" }, extend_tip = true },
        new chain_def{ ring_joints = new[]{ "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand", "LeftHandMiddle1" }, extend_tip = true },
        new chain_def{ ring_joints = new[]{ "RightShoulder", "RightArm", "RightForeArm", "RightHand", "RightHandMiddle1" }, extend_tip = true },
        new chain_def{ ring_joints = new[]{ "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase" }, extend_tip = true },
        new chain_def{ ring_joints = new[]{ "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase" }, extend_tip = true },
    };

    // Fractional slack added to every measured half-extent so the tube strictly contains the
    // flesh instead of merely touching it. Editable constant.
    const float margin = 0.05f;

    public static cage_constants bake(SkinnedMeshRenderer source){
        var root = source.rootBone;
        var by_name = root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
        var joint_index = joints.Select((n, i) => (n, i)).ToDictionary(e => e.n, e => e.i);

        var rest_pos = joints.Select(n => root.InverseTransformPoint(by_name[n].position)).ToArray();
        var parent = joints.Select(n => nearest_listed(by_name[n].parent, joint_index)).ToArray();
        var rest_len = joints.Select((n, j) => parent[j] < 0 ? 0f : (rest_pos[j] - rest_pos[parent[j]]).magnitude).ToArray();
        var dir = joints.Select((n, j) => parent[j] < 0 ? Vector3.zero : (rest_pos[j] - rest_pos[parent[j]]).normalized).ToArray();

        var flesh = gather_flesh(source, root, joint_index);

        var k = new cage_constants{
            scale = root.lossyScale.x,
            joint_name = joints,
            joint_parent = parent,
            joint_dir = dir,
            joint_rest_len = rest_len,
        };

        var ring_joint = new List<int>();
        var ring_from = new List<int>();
        var ring_ext_dir = new List<Vector3>();
        var ring_ext_len = new List<float>();
        var ring_u = new List<Vector3>();
        var ring_v = new List<Vector3>();
        var ring_hu = new List<float>();
        var ring_hv = new List<float>();
        var chain_start = new List<int>();
        var chain_count = new List<int>();
        var tris = new List<int>();

        foreach(var chain in chains){
            var js = chain.ring_joints.Select(n => joint_index[n]).ToArray();
            var start = ring_joint.Count;
            chain_start.Add(start);

            // Ring axis at each joint bisects the incoming and outgoing segment; the frame is
            // parallel-transported along the chain so corner k stays aligned and the tube never twists.
            var axis = ring_axes(js, rest_pos);
            var frame = transport_frames(axis);

            for(var s = 0; s < js.Length; s++){
                var j = js[s];
                var prev = s > 0 ? js[s - 1] : -1;
                var pts = flesh[j].Concat(prev >= 0 ? flesh[prev] : Enumerable.Empty<Vector3>());
                var (hu, hv) = extents(pts, rest_pos[j], frame[s].u, frame[s].v);

                ring_joint.Add(j);
                ring_from.Add(-1);
                ring_ext_dir.Add(Vector3.zero);
                ring_ext_len.Add(0f);
                ring_u.Add(frame[s].u);
                ring_v.Add(frame[s].v);
                ring_hu.Add(hu);
                ring_hv.Add(hv);
            }

            if(chain.extend_tip){
                var tip = js[js.Length - 1];
                var a = axis[js.Length - 1];
                var f = frame[js.Length - 1];
                var reach = tip_reach(flesh[tip], rest_pos[tip], a);
                var (hu, hv) = extents(flesh[tip], rest_pos[tip] + a * reach, f.u, f.v);

                ring_joint.Add(-1);
                ring_from.Add(tip);
                ring_ext_dir.Add(a);
                ring_ext_len.Add(reach);
                ring_u.Add(f.u);
                ring_v.Add(f.v);
                ring_hu.Add(hu);
                ring_hv.Add(hv);
            }

            var count = ring_joint.Count - start;
            chain_count.Add(count);
            emit_tube(tris, start, count);
        }

        k.ring_joint = ring_joint.ToArray();
        k.ring_from_joint = ring_from.ToArray();
        k.ring_ext_dir = ring_ext_dir.ToArray();
        k.ring_ext_len = ring_ext_len.ToArray();
        k.ring_u = ring_u.ToArray();
        k.ring_v = ring_v.ToArray();
        k.ring_hu = ring_hu.ToArray();
        k.ring_hv = ring_hv.ToArray();
        k.chain_start = chain_start.ToArray();
        k.chain_count = chain_count.ToArray();
        k.tris = tris.ToArray();
        return k;
    }

    // Walk up the transform hierarchy until a listed joint is reached. Bones outside the
    // list (fingers, twist bones) fold into the nearest listed ancestor so their flesh is
    // still wrapped. Returns -1 above the root.
    static int nearest_listed(Transform t, Dictionary<string, int> joint_index){
        for(var c = t; c != null; c = c.parent){
            if(joint_index.TryGetValue(c.name, out var i)){
                return i;
            }
        }
        return -1;
    }

    // Group each skinned vertex (in Hips space, at rest) under the listed joint its dominant
    // bone folds to. This is the flesh each ring must enclose.
    static List<Vector3>[] gather_flesh(SkinnedMeshRenderer source, Transform root, Dictionary<string, int> joint_index){
        var mesh = source.sharedMesh;
        var bones = source.bones;
        var binds = mesh.bindposes;
        var weights = mesh.boneWeights;
        var verts = mesh.vertices;

        var fold = bones.Select(b => nearest_listed(b, joint_index)).ToArray();

        var flesh = Enumerable.Range(0, joints.Length).Select(_ => new List<Vector3>()).ToArray();
        for(var i = 0; i < verts.Length; i++){
            var b = dominant_bone(weights[i]);
            var group = fold[b];
            if(group < 0){
                continue;
            }
            var world = bones[b].localToWorldMatrix.MultiplyPoint3x4(binds[b].MultiplyPoint3x4(verts[i]));
            flesh[group].Add(root.InverseTransformPoint(world));
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

    static (Vector3 u, Vector3 v)[] transport_frames(Vector3[] axis){
        var frame = new (Vector3 u, Vector3 v)[axis.Length];
        var seed = Mathf.Abs(axis[0].y) < 0.9f ? Vector3.up : Vector3.forward;
        var u = Vector3.Cross(axis[0], seed).normalized;
        for(var i = 0; i < axis.Length; i++){
            // Re-project the previous u onto the new normal plane to keep the frame twist-free.
            u = (u - axis[i] * Vector3.Dot(u, axis[i])).normalized;
            frame[i] = (u, Vector3.Cross(axis[i], u).normalized);
        }
        return frame;
    }

    static Vector3[] ring_axes(int[] js, Vector3[] rest_pos){
        var seg = new Vector3[js.Length - 1];
        for(var i = 0; i < seg.Length; i++){
            seg[i] = (rest_pos[js[i + 1]] - rest_pos[js[i]]).normalized;
        }
        var axis = new Vector3[js.Length];
        for(var i = 0; i < js.Length; i++){
            var a = i > 0 ? seg[i - 1] : seg[0];
            var b = i < seg.Length ? seg[i] : seg[seg.Length - 1];
            axis[i] = (a + b).normalized;
        }
        return axis;
    }

    static (float hu, float hv) extents(IEnumerable<Vector3> pts, Vector3 center, Vector3 u, Vector3 v){
        var hu = 0f;
        var hv = 0f;
        foreach(var p in pts){
            var d = p - center;
            hu = Mathf.Max(hu, Mathf.Abs(Vector3.Dot(d, u)));
            hv = Mathf.Max(hv, Mathf.Abs(Vector3.Dot(d, v)));
        }
        return (hu * (1f + margin), hv * (1f + margin));
    }

    // How far the flesh folded onto a leaf joint reaches beyond it along the tube axis.
    static float tip_reach(List<Vector3> pts, Vector3 center, Vector3 axis){
        var reach = 0f;
        foreach(var p in pts){
            reach = Mathf.Max(reach, Vector3.Dot(p - center, axis));
        }
        return reach;
    }

    // Side quads between consecutive rings, plus a flat cap on the first and last ring.
    static void emit_tube(List<int> tris, int start, int count){
        for(var s = 0; s < count - 1; s++){
            var a = (start + s) * 4;
            var b = (start + s + 1) * 4;
            for(var k = 0; k < 4; k++){
                var kn = (k + 1) % 4;
                quad(tris, a + k, a + kn, b + kn, b + k);
            }
        }
        var first = start * 4;
        var last = (start + count - 1) * 4;
        quad(tris, first + 3, first + 2, first + 1, first + 0);
        quad(tris, last + 0, last + 1, last + 2, last + 3);
    }

    static void quad(List<int> tris, int i0, int i1, int i2, int i3){
        tris.Add(i0); tris.Add(i1); tris.Add(i2);
        tris.Add(i0); tris.Add(i2); tris.Add(i3);
    }

    // Containment check against the current (deformed) cage: the rig-space positions of every
    // skinned mesh vertex that falls outside all tube segments. The mesh is skinned to the live
    // bones, so both mesh and cage follow the current lengths. (Until cage-based mesh deformation
    // exists the mesh skins by LBS, which the cage does not match -- so escapes here are expected
    // and will vanish once the mesh is mapped through the cage.) The tube is sampled as a lofted
    // box between consecutive rings, so a point is inside a segment's interpolated cross-section.
    // Geometry (bind-space verts/weights/bindposes) is read from geo -- always readable -- while
    // the live bones come from pose; the two are identical-topology clones sharing bone order.
    public static List<Vector3> find_outside(SkinnedMeshRenderer geo, SkinnedMeshRenderer pose, IReadOnlyDictionary<string, float> lengths, cage_constants k){
        var root = pose.rootBone;
        var mesh = geo.sharedMesh;
        var bones = pose.bones;
        var binds = mesh.bindposes;
        var weights = mesh.boneWeights;
        var verts = mesh.vertices;

        var center = ring_centers(k, joint_centers(lengths, k));

        var outside = new List<Vector3>();
        for(var i = 0; i < verts.Length; i++){
            var p = root.InverseTransformPoint(skinned(verts[i], weights[i], bones, binds));
            if(!inside_any(p, k, center)){
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

    struct obb{
        public Vector3 c, u, v, w;  // center and orthonormal axes
        public Vector3 h;           // half-extents along u, v, w
    }

    // Segments (start ring index) that intersect another segment's box. Consecutive segments in
    // a chain share a ring by design and are skipped; everything else flagged here is a genuine
    // self-collision -- including the junction overlaps where limbs are buried in the torso.
    public static List<int> self_overlaps(IReadOnlyDictionary<string, float> lengths, cage_constants k){
        var center = ring_centers(k, joint_centers(lengths, k));

        var seg_a = new List<int>();
        var seg_chain = new List<int>();
        for(var c = 0; c < k.chain_start.Length; c++){
            for(var s = 0; s < k.chain_count[c] - 1; s++){
                seg_a.Add(k.chain_start[c] + s);
                seg_chain.Add(c);
            }
        }

        var hit = new HashSet<int>();
        for(var i = 0; i < seg_a.Count; i++){
            var a = segment_obb(seg_a[i], center, k);
            for(var j = i + 1; j < seg_a.Count; j++){
                var neighbours = seg_chain[i] == seg_chain[j] && Mathf.Abs(seg_a[i] - seg_a[j]) <= 1;
                if(!neighbours && obb_overlap(a, segment_obb(seg_a[j], center, k))){
                    hit.Add(seg_a[i]);
                    hit.Add(seg_a[j]);
                }
            }
        }
        return hit.ToList();
    }

    static obb segment_obb(int a, Vector3[] center, cage_constants k){
        var b = a + 1;
        var along = center[b] - center[a];
        var len = along.magnitude;
        var w = len > 1e-9f ? along / len : Vector3.forward;
        var u = (k.ring_u[a] - w * Vector3.Dot(k.ring_u[a], w)).normalized;
        return new obb{
            c = (center[a] + center[b]) * 0.5f,
            u = u,
            v = Vector3.Cross(w, u),
            w = w,
            h = new Vector3(Mathf.Max(k.ring_hu[a], k.ring_hu[b]), Mathf.Max(k.ring_hv[a], k.ring_hv[b]), len * 0.5f),
        };
    }

    // Separating-axis test over the 3+3 face normals and 9 edge cross products; overlap when no
    // axis separates the two boxes.
    static bool obb_overlap(obb a, obb b){
        var a_ax = new[]{ a.u, a.v, a.w };
        var b_ax = new[]{ b.u, b.v, b.w };
        var axes = a_ax.Concat(b_ax).Concat(from x in a_ax from y in b_ax select Vector3.Cross(x, y));
        var t = b.c - a.c;

        var separated = axes.Where(ax => ax.sqrMagnitude > 1e-9f).Any(ax => {
            var l = ax.normalized;
            var ra = Mathf.Abs(Vector3.Dot(a.u, l)) * a.h.x + Mathf.Abs(Vector3.Dot(a.v, l)) * a.h.y + Mathf.Abs(Vector3.Dot(a.w, l)) * a.h.z;
            var rb = Mathf.Abs(Vector3.Dot(b.u, l)) * b.h.x + Mathf.Abs(Vector3.Dot(b.v, l)) * b.h.y + Mathf.Abs(Vector3.Dot(b.w, l)) * b.h.z;
            return Mathf.Abs(Vector3.Dot(t, l)) > ra + rb;
        });
        return !separated;
    }

    static Vector3[] ring_centers(cage_constants k, Vector3[] joint_pos){
        return Enumerable.Range(0, k.ring_joint.Length).Select(i => k.ring_joint[i] >= 0
            ? joint_pos[k.ring_joint[i]]
            : joint_pos[k.ring_from_joint[i]] + k.ring_ext_dir[i] * k.ring_ext_len[i]).ToArray();
    }

    static bool inside_any(Vector3 p, cage_constants k, Vector3[] center){
        return Enumerable.Range(0, k.chain_start.Length).Any(c =>
            Enumerable.Range(0, k.chain_count[c] - 1).Any(s =>
                inside_segment(p, k.chain_start[c] + s, k.chain_start[c] + s + 1, k, center)));
    }

    // A lofted box between two rings: inside when the point projects within the segment
    // (raw t in [0,1]) and lies within the interpolated cross-section there.
    static bool inside_segment(Vector3 p, int a, int b, cage_constants k, Vector3[] center){
        var axis = center[b] - center[a];
        var len2 = axis.sqrMagnitude;
        var inside = false;
        if(len2 >= 1e-12f){
            var t = Vector3.Dot(p - center[a], axis) / len2;
            if(t >= 0f && t <= 1f){
                var c = Vector3.Lerp(center[a], center[b], t);
                var u = Vector3.Lerp(k.ring_u[a] * k.ring_hu[a], k.ring_u[b] * k.ring_hu[b], t);
                var v = Vector3.Lerp(k.ring_v[a] * k.ring_hv[a], k.ring_v[b] * k.ring_hv[b], t);
                var d = p - c;
                inside = Mathf.Abs(Vector3.Dot(d, u.normalized)) <= u.magnitude
                    && Mathf.Abs(Vector3.Dot(d, v.normalized)) <= v.magnitude;
            }
        }
        return inside;
    }
#endif
}
