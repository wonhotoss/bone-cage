using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// The five body sliders, as ratios on the rest body. Groups on purpose overlap: torso, arms and
// legs partition every editable bone, and left and right cut the arms and legs a second way -- so a
// left forearm carries both arms and left, and the sliders stack by multiplying. The shoulder bases
// stay out of the sides, which keeps the torso whole under any asymmetry. See cage.md 7.
[Serializable]
public class cage_shape{
    public enum group{ torso, arms, legs, left, right }

    public float torso = 1f, arms = 1f, legs = 1f, left = 1f, right = 1f;

    public float this[group g]{
        get => g switch{ group.torso => torso, group.arms => arms, group.legs => legs, group.left => left, _ => right };
        set{
            switch(g){
                case group.torso: torso = value; break;
                case group.arms: arms = value; break;
                case group.legs: legs = value; break;
                case group.left: left = value; break;
                default: right = value; break;
            }
        }
    }
}

public class mapping_tester : MonoBehaviour{
    public SkinnedMeshRenderer source;
    public SkinnedMeshRenderer target;

    // Baked at import from the rest mesh; the cage generator reads only this plus bone lengths.
    [HideInInspector] public cage_constants constants;
    public MeshFilter cage_view;

    // The target's skeleton drawn as cubes and lines; enabling it shows it. A scene object of its
    // own rather than a child of the rig root, which import replaces.
    public skeleton_view skeleton;

    // Scene view: draw the whole triangulation instead of only the edges cage.md declares. What the
    // tuning sliders move is exactly a ring's edges and a post's ends, so the recipe's own frame is
    // the view to tune against; the panels' quad diagonals bury it. Turn this on to read the shell
    // as a surface -- containment and self-collision are about the surface.
    public bool all_edges;

#if UNITY_EDITOR
    // The tuning sliders' departure from cage_tune.baked, knob by knob; zero is the baked cage.
    // The inspector's cage section draws it as sliders and rebakes from baked + delta.
    [HideInInspector] public cage_tune tune_delta = new();
#endif

    // Where the five body sliders stand. They scale whole groups of bones at once, so a proportion
    // can be reached without dragging fifty-three sliders -- and the groups overlap on purpose, an
    // arm bone taking both arms and its side. Each is a ratio on the rest body, and moving one
    // applies only the change since it was last here, so per-bone edits under it survive.
    [HideInInspector] public cage_shape shape = new();

    // Which coordinates the deform button maps the mesh through.
    public cage_coords coords;

    // The rest mesh solved against the rest cage. Both sides are fixed by the import, so this is
    // where the whole cost of the method sits and deform reduces to a weighted sum. Not serialized:
    // one weight per mesh vertex per cage corner would outweigh the scene several times over, and
    // the heavier coordinates the plan calls for want an asset of their own anyway -- so a scene
    // reload rebinds.
    cage_bind bound;

    // The rest cage the bind was solved against. A rebake moves the rest cage while the weights
    // stay, so this is what tells a stale bind from a current one -- there is no flag to keep in step.
    Vector3[] bound_rest;

    // Whether mapping through the current cage would need a fresh solve: no bind yet, another
    // coordinate method chosen, or the constants no longer build the rest cage the bind was made on.
    public bool bind_stale => bound == null || bound.coords != coords
        || !cage.points(new Dictionary<string, float>(), constants).SequenceEqual(bound_rest);

    // Debug snapshots drawn as gizmos in the live cage space: escaped mesh vertices and the cage
    // triangles that self-intersect. Populated by the inspector check buttons.
    [HideInInspector] public Vector3[] outside_points;
    [HideInInspector] public int[] collide_tris;

    // A bone spans a joint's parent to the joint itself, so the far-end joint names it.
    // Leaf joints (Head, ToeBase, finger tips) end no bone.
    static readonly (string name, string joint)[] body = {
        ("pelvis", "Spine"),
        ("lumbar", "Spine1"),
        ("lower thorax", "Spine2"),
        ("upper thorax", "Spine3"),
        ("chest", "Neck"),
        ("lower neck", "Neck1"),
        ("upper neck", "Head"),

        ("left shoulder base", "LeftShoulder"),
        ("left clavicle", "LeftArm"),
        ("left upper arm", "LeftForeArm"),
        ("left forearm", "LeftHand"),

        ("right shoulder base", "RightShoulder"),
        ("right clavicle", "RightArm"),
        ("right upper arm", "RightForeArm"),
        ("right forearm", "RightHand"),

        ("left hip", "LeftUpLeg"),
        ("left thigh", "LeftLeg"),
        ("left calf", "LeftFoot"),
        ("left foot", "LeftToeBase"),

        ("right hip", "RightUpLeg"),
        ("right thigh", "RightLeg"),
        ("right calf", "RightFoot"),
        ("right foot", "RightToeBase"),
    };

    static readonly string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

    // Every bone a slider edits. The three phalanx joints of each finger are numbered out from the
    // palm rather than named, since the same ordinal is a different phalanx on the thumb; they carry
    // a group so the inspector can fold a whole hand away.
    static IEnumerable<(string group, string name, string joint)> anatomy => body
        .Select(e => ("", e.name, e.joint))
        .Concat(new[]{ "Left", "Right" }.SelectMany(h => fingers.SelectMany(f => Enumerable.Range(1, 3)
            .Select(i => ($"{h.ToLower()} hand", $"{f.ToLower()} {i}", $"{h}Hand{f}{i}")))));

    public class bone{
        public string group;
        public string name;
        public string joint;
        public Transform source;
        public Transform target;

        // No joint carries its own scaling, so every joint's parent holds the same uniform scale.
        float scale => target.parent.lossyScale.x;

        // import clones the source subtree verbatim, so both local spaces share this scale.
        public float rest => source.localPosition.magnitude * scale;

        public float length{
            get => target.localPosition.magnitude * scale;
            set => target.localPosition = target.localPosition.normalized * (value / scale);
        }

        // Length in the rig's own units, matching the joint positions the cage is built in.
        public float native_length => target.localPosition.magnitude;
    }

    public IEnumerable<bone> measure(){
        var rest = source.rootBone.GetComponentsInChildren<Transform>(true).ToDictionary(b => b.name);
        var current = target.rootBone.GetComponentsInChildren<Transform>(true).ToDictionary(b => b.name);

        return anatomy.Select(e => new bone{ group = e.group, name = e.name, joint = e.joint, source = rest[e.joint], target = current[e.joint] });
    }

    public void reset_lengths(){
        foreach(var b in measure()){
            b.length = b.rest;
        }
        shape = new cage_shape();
    }

    // Which of the five groups a bone belongs to, read off the skeleton rather than listed: the two
    // arms are the subtrees under the clavicles, the two legs those under the hips, and the torso is
    // whatever is left -- spine, neck, head and the shoulder bases the arms hang from. So the groups
    // partition the body, and the sides cut the same bones a second way, the shoulder bases aside.
    IEnumerable<bone> group_of(cage_shape.group g){
        bool under(Transform t, string root){
            for(var a = t; a != null; a = a.parent){
                if(a.name == root){
                    return true;
                }
            }
            return false;
        }
        bool left(bone b){ return under(b.target, "LeftArm") || under(b.target, "LeftUpLeg"); }
        bool right(bone b){ return under(b.target, "RightArm") || under(b.target, "RightUpLeg"); }
        bool arm(bone b){ return under(b.target, "LeftArm") || under(b.target, "RightArm"); }
        bool leg(bone b){ return under(b.target, "LeftUpLeg") || under(b.target, "RightUpLeg"); }

        return measure().Where(b => g switch{
            cage_shape.group.torso => !arm(b) && !leg(b),
            cage_shape.group.arms => arm(b),
            cage_shape.group.legs => leg(b),
            cage_shape.group.left => left(b),
            _ => right(b),
        });
    }

    // Move one slider: every bone the group covers takes the ratio between where it was and where
    // it is now. The sliders multiply where they overlap because each applies to the length it
    // finds, and an edit made under them is carried along rather than overwritten.
    public void rescale(cage_shape.group g, float to){
        var from = shape[g];
        foreach(var b in group_of(g)){
            b.length *= to / from;
        }
        shape[g] = to;
    }

    public void import(){
        clone_source();
#if UNITY_EDITOR
        // The bind is a product of the bake -- same rest geometry, same editor-only step.
        constants = cage.bake(source, cage_tune.tuned(tune_delta));
        bind();
#endif
        ensure_cage_view();
        update_cage();
    }

    // The same import with the rest side read from a bake instead of solved here: how the demo
    // stands the body up at once in a build, where the baker is compiled out and the bind would
    // hold the first frame for seconds. The file is what export_bake wrote.
    public void import(TextAsset bake){
        clone_source();
        var (json, b) = cage_bake.read(bake.bytes);
        Debug.Assert(b.w.Length / b.stride == source.sharedMesh.vertexCount, "cage: the bake was made from another mesh");
        constants = JsonUtility.FromJson<cage_constants>(json);
        bound = b;
        // The bake wrote constants and bind from one cage, so the bind is current by construction.
        bound_rest = cage.points(new Dictionary<string, float>(), constants);
        coords = b.coords;
        ensure_cage_view();
        update_cage();
    }

    // The target as a fresh clone of the source: its mesh, and its skeleton as a subtree of this
    // transform, so the bones can be edited while the source keeps standing at rest.
    void clone_source(){
        if(target.rootBone != null){
            DestroyImmediate(target.rootBone.gameObject);
        }

        var mesh = Instantiate(source.sharedMesh);
        mesh.name = source.sharedMesh.name;

        var root = Instantiate(source.rootBone.gameObject, transform).transform;
        root.name = source.rootBone.name;

        // Instantiate preserves hierarchy order, so both traversals line up index by index.
        var map = source.rootBone.GetComponentsInChildren<Transform>(true)
            .Zip(root.GetComponentsInChildren<Transform>(true), (o, c) => (o, c))
            .ToDictionary(p => p.o, p => p.c);

        target.sharedMesh = mesh;
        target.bones = source.bones.Select(b => map[b]).ToArray();
        target.rootBone = root;
        target.sharedMaterials = source.sharedMaterials;
        target.localBounds = source.localBounds;
    }

    void ensure_cage_view(){
        if(cage_view == null){
            // A child of the rig root so the cage sits in the same space as the skeleton it wraps.
            var view = new GameObject("cage").transform;
            view.SetParent(target.rootBone, false);
            cage_view = view.gameObject.AddComponent<MeshFilter>();
        }
    }

    // Pure regeneration: forward-kinematic joints from the current bone lengths, then the baked
    // rings re-placed on them. Runs on every length edit.
    public void update_cage(){
        if(constants != null){
            var lengths = measure().ToDictionary(b => b.joint, b => b.native_length);
            cage_view.sharedMesh = cage.build(lengths, constants);
        }
    }

    // Bind space -- the space the mesh's own vertex buffer lives in -- to rig root local space,
    // which is where the cage is built. Every bone yields the same matrix by definition of the bind
    // pose, and the source skeleton still stands in it, so bone 0 supplies it.
    Matrix4x4 bind_to_rig => source.rootBone.worldToLocalMatrix
        * source.bones[0].localToWorldMatrix * source.sharedMesh.bindposes[0];

    // Solve the pristine source geometry against the rest cage. Both are constants of the import,
    // so this runs there and once after a cage rebuild, and deform is a weighted sum from then on.
    public void bind(){
        var to_rig = bind_to_rig;
        var rest_pts = source.sharedMesh.vertices.Select(v => to_rig.MultiplyPoint3x4(v)).ToArray();
        var rest_cage = cage.points(new Dictionary<string, float>(), constants);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        bound = cage_deform.bind(coords, rest_pts, rest_cage, constants.tris);
        bound_rest = rest_cage;
        Debug.Log($"cage: bound {rest_pts.Length} vertices to the rest cage through {coords} in {clock.ElapsedMilliseconds} ms");
    }

    // The rest mesh mapped through the given cage, in rig space: what the body *is* at the lengths
    // that built that cage. No skinning is involved anywhere in this pipeline -- the skeleton drives
    // the cage and the cage drives the mesh -- so this is the body the containment check measures too.
    public Vector3[] mapped(Vector3[] live){
        // The dropdown is a plain field with no hook, and a tuning drag rebakes without binding,
        // so a stale bind surfaces here: solved once, on the first map that needs it.
        if(bind_stale){
            bind();
        }
        return cage_deform.map(bound, live);
    }

    // Map the mesh through the cage: rest cage -> current cage, straight into the target's vertex
    // buffer. The bind holds the rest side, so this reads nothing of the target's own output and
    // can be re-run after any length edit or with any coordinates without compounding.
    // The buffer holds the rest shape, so the viewport still shows this skinned by the *old* rest
    // pose -- the length edit applied twice -- until refresh_rest_pose rebinds it.
    public void deform(){
        var lengths = measure().ToDictionary(b => b.joint, b => b.native_length);
        var moved = mapped(cage.points(lengths, constants));

        var to_bind = bind_to_rig.inverse;
        var mesh = target.sharedMesh;
        mesh.vertices = moved.Select(p => to_bind.MultiplyPoint3x4(p)).ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    // Make the deformed skeleton the mesh's rest pose: rebind every bone so that skinning at the
    // current transforms is the identity, which is what puts the vertex buffer on screen as it was
    // written. The bones need no moving -- the sliders already stand them at the deformed skeleton,
    // and only lengths changed, so their rotations are untouched. Afterwards rest pose, current
    // pose and vertex buffer all agree, and animation on top deforms the new body.
    public void refresh_rest_pose(){
        var to_world = target.rootBone.localToWorldMatrix * bind_to_rig;
        var mesh = target.sharedMesh;
        mesh.bindposes = target.bones.Select(b => b.worldToLocalMatrix * to_world).ToArray();
        // The renderer caches its skinning setup, so hand the mesh back to make it re-read them.
        target.sharedMesh = mesh;
    }

    // The whole consequence of a length edit: the cage re-placed on the new skeleton, the mesh
    // mapped through it, and the rest pose rebound so the viewport shows the new body instead of
    // the edit applied twice. Cheap enough to run on every edit now that the bind is precomputed.
    // Neither of the last two is undoable, so undoing a length leaves the body a step behind until
    // the next edit; "import source" is the way back to the original geometry.
    public void update_body(){
        update_cage();
        deform();
        refresh_rest_pose();
    }

#if UNITY_EDITOR
    // Everything tools/cage_sweep needs to run the containment and self-collision checks outside
    // Unity: the baked constants, the rest mesh in rig space, each vertex's dominant joint so an
    // escape can be named after the body part it belongs to, and the joints the sliders edit so a
    // sweep covers exactly the bones this tester supports. The sweep compiles cage.cs and
    // cage_deform.cs themselves and binds its own coordinates, so no weights and nothing derived
    // travels -- press this again after any rebake and the sweep is current.
    public void export_sweep_data(){
        var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../tools/cage_sweep/data"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "constants.json"), JsonUtility.ToJson(constants, true));
        // The finger names repeat between the hands, so the group qualifies them.
        File.WriteAllLines(Path.Combine(dir, "bones.txt"),
            anatomy.Select(e => $"{e.joint}\t{(e.group == "" ? e.name : $"{e.group} {e.name}")}"));

        var to_rig = bind_to_rig;
        var mesh = source.sharedMesh;
        var slot = source.bones.Select(b => Array.IndexOf(constants.joint_name, b.name)).ToArray();

        using(var f = new BinaryWriter(File.Create(Path.Combine(dir, "rest.bin")))){
            f.Write(mesh.vertexCount);
            foreach(var v in mesh.vertices){
                var p = to_rig.MultiplyPoint3x4(v);
                f.Write(p.x);
                f.Write(p.y);
                f.Write(p.z);
            }
            foreach(var w in mesh.boneWeights){
                f.Write(slot[cage.dominant_bone(w)]);
            }
        }
        Debug.Log($"cage: sweep data for {mesh.vertexCount} vertices written to {dir}");
    }

    // The same export without opening the editor, so refreshing a sweep is one command:
    //   Unity.exe -batchmode -quit -projectPath unity -executeMethod mapping_tester.export_headless
    static void export_headless(){
        baked().export_sweep_data();
    }

    // main.unity's tester, rebaked, for the headless exports: nobody is here to press "rebuild
    // cage", and what is worth exporting is the cage the current sources and the current tune delta make.
    static mapping_tester baked(){
        var tester = EditorSceneManager.OpenScene("Assets/Scenes/main.unity")
            .GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<mapping_tester>(true)).Single();
        tester.constants = cage.bake(tester.source, cage_tune.tuned(tester.tune_delta));
        return tester;
    }

    // The rest side for the demo as one file -- the baked constants, then the bind -- so a build
    // maps the body without solving anything. Read back by import(TextAsset). Rebinds first if the
    // bind is stale, so what goes out is the current cage.
    const string bake_path = "Assets/demo/cage_bake.bytes";

    public void export_bake(){
        if(bind_stale){
            bind();
        }
        cage_bake.write(File.Create(Path.Combine(Application.dataPath, "..", bake_path)), JsonUtility.ToJson(constants), bound);
        AssetDatabase.ImportAsset(bake_path);
        Debug.Log($"cage: demo bake written to {bake_path}, {bound.w.Length * sizeof(float) / 1048576f:0.0} MB of weights");
    }

    // The same without opening the editor:
    //   Unity.exe -batchmode -quit -projectPath unity -executeMethod mapping_tester.export_bake_headless
    static void export_bake_headless(){
        baked().export_bake();
    }
#endif

#if UNITY_EDITOR
    void OnDrawGizmosSelected(){
        if(cage_view != null && cage_view.sharedMesh != null){
            // The cage child is identity-local under the rig root, so everything below shares
            // the live (deformed) cage space.
            Gizmos.matrix = cage_view.transform.localToWorldMatrix;

            Gizmos.color = new Color(0.2f, 0.9f, 1f);
            if(all_edges){
                Gizmos.DrawWireMesh(cage_view.sharedMesh);
            }
            else{
                var frame = cage_view.sharedMesh.vertices;
                foreach(var e in cage.frame(constants)){
                    Gizmos.DrawLine(frame[e.a], frame[e.b]);
                }
            }

            // Panels in self-collision, outlined on the live cage.
            if(collide_tris != null){
                Gizmos.color = Color.red;
                var verts = cage_view.sharedMesh.vertices;
                var tris = cage_view.sharedMesh.triangles;
                foreach(var t in collide_tris){
                    for(var e = 0; e < 3; e++){
                        Gizmos.DrawLine(verts[tris[t * 3 + e]], verts[tris[t * 3 + (e + 1) % 3]]);
                    }
                }
            }

            // Mapped mesh vertices that escaped the current cage.
            if(outside_points != null){
                Gizmos.color = Color.red;
                var r = target.localBounds.size.magnitude * 0.004f;
                foreach(var p in outside_points){
                    Gizmos.DrawCube(p, Vector3.one * r);
                }
            }
        }
    }
#endif

#if UNITY_EDITOR
    [CustomEditor(typeof(mapping_tester))]
    public class inspector: Editor{
        // Which slider groups are unfolded. Inspector state, so it lives with the inspector.
        readonly Dictionary<string, bool> open = new();

        // Which cage groups are unfolded in the scene view, by name. Inspector state likewise.
        readonly HashSet<string> unfolded = new();

        // The tuning sliders: a cage_tune knob, its label, and the absolute range it may reach.
        static readonly (string field, string label, float min, float max)[] tune_knobs = {
            ("arm_tilt", "arm ring tilt (deg)", -30f, 45f),
            ("arm_length", "arm ring length", 0.05f, 0.3f),
            ("body_front", "body front reach", -0.05f, 0.1f),
            ("body_back", "body back reach", -0.05f, 0.1f),
            ("head_tilt", "head ring tilt (deg)", 0f, 45f),
            ("head_offset", "head ring offset", -0.02f, 0.06f),
            ("head_front", "head ring front reach", -0.05f, 0.1f),
            ("head_back", "head ring back reach", -0.05f, 0.1f),
            ("head_gate_slack", "head gate slack", 0f, 0.1f),
            ("arm_gate_slack", "arm gate slack", -0.05f, 0.05f),
            ("spine_gate_slack", "spine gate slack", -0.035f, 0.01f),
            ("knee_gate_slack", "knee gate slack", -0.035f, 0.01f),
            ("armpit_gate_slack", "armpit gate slack", -0.05f, 0.01f),
            ("neck_gate_slack", "neck gate slack", -0.08f, 0.01f),
            ("crown_front", "crown ring front reach", -0.05f, 0.1f),
            ("crown_back", "crown ring back reach", -0.05f, 0.1f),
            ("crotch_drop", "crotch drop", 0f, 0.3f),
            ("hip_out", "hip out (ratio)", 0f, 2f),
            ("knee_out", "knee ring outer reach", -0.1f, 0.1f),
            ("knee_back", "knee ring back reach", -0.05f, 0.2f),
            ("ankle_tilt", "ankle ring tilt (deg)", 0f, 80f),
            ("ankle_front", "ankle ring front reach", -0.1f, 0.1f),
            ("ankle_back", "ankle ring back reach", -0.05f, 0.1f),
            ("elbow_hi", "elbow ring hi reach", -0.05f, 0.1f),
            ("wrist_thumb", "wrist ring thumb-side reach", -0.05f, 0.05f),
            ("wrist_pinky", "wrist ring pinky-side reach", -0.05f, 0.05f),
            ("thumb_out", "palm thumb out reach", -0.05f, 0.05f),
            ("pinky_out", "palm pinky out reach", -0.05f, 0.05f),
            ("finger_out", "finger ring side reach", -0.005f, 0.01f),
            ("valley_reach", "palm valley reach", 0f, 0.03f),
        };

        // A tag hides once its group is smaller than this on screen, so the body rings read at
        // full-figure zoom and the finger rings only once the view is on a hand.
        const float tag_min_px = 24f;

        // Tag backgrounds by kind: a name tag is white (yellow once unfolded), a vertex index cyan
        // like the cage wire, a placing joint orange.
        static readonly Color vertex_color = new(0.5f, 0.9f, 1f);
        static readonly Color joint_color = new(1f, 0.65f, 0.3f);

        // The cage's names as clickable tags in the scene view: the 3D appendix to the design
        // document. Clicking a tag unfolds that group alone -- its vertex indices, and a line from
        // every joint that places it -- so the detail never floods the whole cage at once.
        void OnSceneGUI(){
            var mapping = target as mapping_tester;
            if(mapping.constants != null && mapping.cage_view != null && mapping.cage_view.sharedMesh != null){
                var k = mapping.constants;
                var to_world = mapping.cage_view.transform.localToWorldMatrix;
                var verts = mapping.cage_view.sharedMesh.vertices.Select(v => to_world.MultiplyPoint3x4(v)).ToArray();

                // Groups large enough on screen to carry a tag, with where the tag goes.
                var shown = cage.named(k).Select(g => {
                    var gui = g.verts.Select(v => HandleUtility.WorldToGUIPoint(verts[v])).ToArray();
                    var size = new Vector2(gui.Max(p => p.x) - gui.Min(p => p.x), gui.Max(p => p.y) - gui.Min(p => p.y)).magnitude;
                    var center = gui.Aggregate((a, b) => a + b) / gui.Length;
                    return (g.name, g.verts, size, center);
                }).Where(g => g.size >= tag_min_px).ToArray();

                var picked = shown.Where(g => unfolded.Contains(g.name)).ToArray();
                var lengths = mapping.measure().ToDictionary(b => b.joint, b => b.native_length);
                var anchors = cage.anchors(lengths, k).Where(a => picked.Any(g => g.name == a.name)).ToArray();

                Handles.color = Color.yellow;
                foreach(var a in anchors){
                    Handles.DrawDottedLine(to_world.MultiplyPoint3x4(a.from), to_world.MultiplyPoint3x4(a.to), 4f);
                }

                // Every tag is a boxed mini label, so it reads over the mesh; the color tells the kind.
                Handles.BeginGUI();
                Rect box(Vector2 at, GUIContent content){
                    var size = EditorStyles.miniButton.CalcSize(content);
                    return new Rect(at - size * 0.5f, size);
                }
                foreach(var g in shown){
                    var content = new GUIContent(g.name);
                    GUI.backgroundColor = unfolded.Contains(g.name) ? Color.yellow : Color.white;
                    if(GUI.Button(box(g.center, content), content, EditorStyles.miniButton)){
                        if(!unfolded.Remove(g.name)){
                            unfolded.Add(g.name);
                        }
                    }
                }
                GUI.backgroundColor = vertex_color;
                foreach(var v in picked.SelectMany(g => g.verts)){
                    var content = new GUIContent($"v{v}");
                    GUI.Label(box(HandleUtility.WorldToGUIPoint(verts[v]), content), content, EditorStyles.miniButton);
                }
                // One joint may place both edges of a ring; label it once.
                GUI.backgroundColor = joint_color;
                foreach(var j in anchors.Select(a => (a.joint, a.weight, a.from)).Distinct()){
                    var content = new GUIContent(j.weight == 1f ? j.joint : $"{j.joint} ×{j.weight:0.00}");
                    GUI.Label(box(HandleUtility.WorldToGUIPoint(to_world.MultiplyPoint3x4(j.from)), content), content, EditorStyles.miniButton);
                }
                GUI.backgroundColor = Color.white;
                Handles.EndGUI();
            }
        }

        public override void OnInspectorGUI(){
            DrawDefaultInspector();

            var mapping = target as mapping_tester;

            // The view's own enabled flag, surfaced here so the whole control surface is one inspector.
            EditorGUI.BeginChangeCheck();
            var show = EditorGUILayout.Toggle("show skeleton", mapping.skeleton.enabled);
            if(EditorGUI.EndChangeCheck()){
                Undo.RecordObject(mapping.skeleton, "toggle skeleton");
                mapping.skeleton.enabled = show;
            }

            if(GUILayout.Button("import source")){
                mapping.import();
            }

            if(mapping.target.rootBone != null){
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("body", EditorStyles.boldLabel);

                // One slider per group, so a proportion is reached in a drag rather than in fifty
                // three. They overlap and stack (cage_shape), and each carries whatever per-bone
                // edits sit under it, so the two sections can be used in any order.
                foreach(cage_shape.group g in Enum.GetValues(typeof(cage_shape.group))){
                    EditorGUI.BeginChangeCheck();
                    var to = EditorGUILayout.Slider(g.ToString(), mapping.shape[g], 0.5f, 1.5f);
                    if(EditorGUI.EndChangeCheck()){
                        Undo.RecordObjects(mapping.measure().Select(b => (UnityEngine.Object)b.target).Append(mapping).ToArray(), "scale body");
                        mapping.rescale(g, to);
                        mapping.update_body();
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("bone lengths", EditorStyles.boldLabel);

                if(GUILayout.Button("reset bone lengths")){
                    Undo.RecordObjects(mapping.measure().Select(b => (UnityEngine.Object)b.target).Append(mapping).ToArray(), "reset bone lengths");
                    mapping.reset_lengths();
                    mapping.update_body();
                }

                // Fifteen bones per hand would bury the body, so each hand folds into one header.
                foreach(var g in mapping.measure().GroupBy(b => b.group)){
                    if(g.Key != ""){
                        open.TryGetValue(g.Key, out var was);
                        open[g.Key] = EditorGUILayout.Foldout(was, g.Key, true);
                    }

                    if(g.Key == "" || open[g.Key]){
                        foreach(var b in g){
                            // As a ratio on rest, the way the sweep names its cases and the design
                            // document reads: a slider at 1.3 is the bone at 1.3.
                            EditorGUI.BeginChangeCheck();
                            var ratio = EditorGUILayout.Slider(b.name, b.length / b.rest, 0.5f, 1.5f);

                            if(EditorGUI.EndChangeCheck()){
                                Undo.RecordObject(b.target, "edit bone length");
                                b.length = b.rest * ratio;
                                mapping.update_body();
                            }
                        }
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("cage", EditorStyles.boldLabel);

                // One change check over every tuning slider: whichever moved, the whole tune rebakes.
                // Each slider moves a knob's delta off cage_tune.baked -- zero is the baked value -- over
                // the knob's documented absolute range, and the label prints where that lands.
                EditorGUI.BeginChangeCheck();
                var deltas = tune_knobs.Select(k => {
                    var f = typeof(cage_tune).GetField(k.field);
                    var at = (float)f.GetValue(cage_tune.baked);
                    var delta = (float)f.GetValue(mapping.tune_delta);
                    return (f, to: EditorGUILayout.Slider($"{k.label} = {at + delta:0.####}", delta, k.min - at, k.max - at));
                }).ToArray();
                if(EditorGUI.EndChangeCheck()){
                    Undo.RecordObject(mapping, "tune cage");
                    foreach(var (f, to) in deltas){
                        f.SetValue(mapping.tune_delta, to);
                    }
                    // Rebake and re-place only -- milliseconds, so the wire follows the drag. The
                    // bind is seconds and is its own button below; until it is pressed the mesh
                    // stays on the old cage and the warning says so.
                    mapping.constants = cage.bake(mapping.source, cage_tune.tuned(mapping.tune_delta));
                    mapping.update_cage();
                }

                if(GUILayout.Button("reset tune")){
                    Undo.RecordObject(mapping, "reset tune");
                    mapping.tune_delta = new cage_tune();
                    mapping.constants = cage.bake(mapping.source, cage_tune.tuned(mapping.tune_delta));
                    mapping.update_cage();
                }
                if(GUILayout.Button("rebuild cage")){
                    mapping.constants = cage.bake(mapping.source, cage_tune.tuned(mapping.tune_delta));
                    mapping.update_cage();
                }

                if(mapping.constants != null){
                    if(mapping.bind_stale){
                        EditorGUILayout.HelpBox("mesh is not bound to the current cage -- press bind mesh", MessageType.Warning);
                    }
                    if(GUILayout.Button("bind mesh")){
                        mapping.bind();
                        mapping.update_body();
                    }
                }

                if(mapping.constants != null && GUILayout.Button("check containment")){
                    var lengths = mapping.measure().ToDictionary(b => b.joint, b => b.native_length);
                    var live = cage.points(lengths, mapping.constants);
                    var moved = mapping.mapped(live);
                    mapping.outside_points = cage.exposed(moved, live, mapping.constants).Select(i => moved[i]).ToArray();
                    Debug.Log($"cage: {mapping.outside_points.Length} / {moved.Length} mesh vertices outside the shell or too near a face");
                }

                if(mapping.constants != null && GUILayout.Button("export sweep data")){
                    mapping.export_sweep_data();
                }

                if(mapping.constants != null && GUILayout.Button("export demo bake")){
                    mapping.export_bake();
                }

                if(mapping.constants != null && GUILayout.Button("check self-collision")){
                    var lengths = mapping.measure().ToDictionary(b => b.joint, b => b.native_length);
                    mapping.collide_tris = cage.self_overlaps(lengths, mapping.constants).ToArray();
                    Debug.Log($"cage: {mapping.collide_tris.Length} cage triangles in self-collision");
                }

                if(mapping.constants != null){
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("mesh", EditorStyles.boldLabel);

                    // Both edit the mesh buffer of a runtime clone, so neither is undoable; press
                    // "import source" to get the original geometry back.
                    if(GUILayout.Button("deform")){
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        mapping.deform();
                        Debug.Log($"cage: deformed {mapping.target.sharedMesh.vertexCount} vertices through {mapping.coords} in {clock.ElapsedMilliseconds} ms");
                    }

                    if(GUILayout.Button("refresh rest pose")){
                        mapping.refresh_rest_pose();
                        Debug.Log("cage: rest pose rebound to the current skeleton");
                    }
                }
            }
        }
    }
#endif
}
