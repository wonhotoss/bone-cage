using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class mapping_tester : MonoBehaviour{
    public SkinnedMeshRenderer source;
    public SkinnedMeshRenderer target;

    // Baked at import from the rest mesh; the cage generator reads only this plus bone lengths.
    [HideInInspector] public cage_constants constants;
    public MeshFilter cage_view;

    // Which coordinates the deform button maps the mesh through.
    public cage_coords coords;

    // The rest mesh solved against the rest cage. Both sides are fixed by the import, so this is
    // where the whole cost of the method sits and deform reduces to a weighted sum. Not serialized:
    // one weight per mesh vertex per cage corner would outweigh the scene several times over, and
    // the heavier coordinates the plan calls for want an asset of their own anyway -- so a scene
    // reload rebinds.
    cage_bind bound;

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
    }

    public void import(){
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

#if UNITY_EDITOR
        // The bind is a product of the bake -- same rest geometry, same editor-only step.
        constants = cage.bake(source);
        bind();
#endif
        ensure_cage_view();
        update_cage();
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
    // so this runs there and after any cage rebuild, and deform is a weighted sum from then on.
    public void bind(){
        var to_rig = bind_to_rig;
        var rest_pts = source.sharedMesh.vertices.Select(v => to_rig.MultiplyPoint3x4(v)).ToArray();
        var rest_cage = cage.points(new Dictionary<string, float>(), constants);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        bound = cage_deform.bind(coords, rest_pts, rest_cage, constants.tris);
        Debug.Log($"cage: bound {rest_pts.Length} vertices to the rest cage through {coords} in {clock.ElapsedMilliseconds} ms");
    }

    // Map the mesh through the cage: rest cage -> current cage, straight into the target's vertex
    // buffer. The bind holds the rest side, so this reads nothing of the target's own output and
    // can be re-run after any length edit or with any coordinates without compounding.
    // The buffer holds the rest shape, so the viewport still shows this skinned by the *old* rest
    // pose -- the length edit applied twice -- until refresh_rest_pose rebinds it.
    public void deform(){
        // The dropdown is a plain field with no hook, so a method switch surfaces here.
        if(bound == null || bound.coords != coords){
            bind();
        }

        var lengths = measure().ToDictionary(b => b.joint, b => b.native_length);
        var moved = cage_deform.map(bound, cage.points(lengths, constants));

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

    void OnDrawGizmosSelected(){
        if(cage_view != null && cage_view.sharedMesh != null){
            // The cage child is identity-local under the rig root, so everything below shares
            // the live (deformed) cage space.
            Gizmos.matrix = cage_view.transform.localToWorldMatrix;

            Gizmos.color = new Color(0.2f, 0.9f, 1f);
            Gizmos.DrawWireMesh(cage_view.sharedMesh);

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

            // Skinned mesh vertices that escaped the current cage.
            if(outside_points != null){
                Gizmos.color = Color.red;
                var r = target.localBounds.size.magnitude * 0.004f;
                foreach(var p in outside_points){
                    Gizmos.DrawCube(p, Vector3.one * r);
                }
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(mapping_tester))]
    public class inspector: Editor{
        // Which slider groups are unfolded. Inspector state, so it lives with the inspector.
        readonly Dictionary<string, bool> open = new();

        public override void OnInspectorGUI(){
            DrawDefaultInspector();

            var mapping = target as mapping_tester;

            if(GUILayout.Button("import source")){
                mapping.import();
            }

            if(mapping.target.rootBone != null){
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("bone lengths", EditorStyles.boldLabel);

                if(GUILayout.Button("reset bone lengths")){
                    Undo.RecordObjects(mapping.measure().Select(b => b.target).ToArray(), "reset bone lengths");
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
                            EditorGUI.BeginChangeCheck();
                            var length = EditorGUILayout.Slider(b.name, b.length, b.rest * 0.5f, b.rest * 1.5f);

                            if(EditorGUI.EndChangeCheck()){
                                Undo.RecordObject(b.target, "edit bone length");
                                b.length = length;
                                mapping.update_body();
                            }
                        }
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("cage", EditorStyles.boldLabel);

                if(GUILayout.Button("rebuild cage")){
                    mapping.constants = cage.bake(mapping.source);
                    mapping.update_cage();
                    mapping.bind();
                }

                if(mapping.constants != null && GUILayout.Button("check containment")){
                    var lengths = mapping.measure().ToDictionary(b => b.joint, b => b.native_length);
                    mapping.outside_points = cage.find_outside(mapping.source, mapping.target, lengths, mapping.constants).ToArray();
                    Debug.Log($"cage: {mapping.outside_points.Length} / {mapping.source.sharedMesh.vertexCount} mesh vertices outside");
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
