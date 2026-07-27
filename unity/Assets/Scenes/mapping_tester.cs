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

    // Debug snapshots drawn as gizmos in the live cage space: escaped mesh vertices and the tube
    // segments that self-intersect. Populated by the inspector check buttons.
    [HideInInspector] public Vector3[] outside_points;
    [HideInInspector] public int[] collide_segments;

    // A bone spans a joint's parent to the joint itself, so the far-end joint names it.
    // Leaf joints (Head, ToeBase, finger tips) end no bone; finger phalanges are left out.
    static readonly (string name, string joint)[] anatomy = {
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
        ("left hand", "LeftHandMiddle1"),

        ("right shoulder base", "RightShoulder"),
        ("right clavicle", "RightArm"),
        ("right upper arm", "RightForeArm"),
        ("right forearm", "RightHand"),
        ("right hand", "RightHandMiddle1"),

        ("left hip", "LeftUpLeg"),
        ("left thigh", "LeftLeg"),
        ("left calf", "LeftFoot"),
        ("left foot", "LeftToeBase"),

        ("right hip", "RightUpLeg"),
        ("right thigh", "RightLeg"),
        ("right calf", "RightFoot"),
        ("right foot", "RightToeBase"),
    };

    public class bone{
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

        return anatomy.Select(e => new bone{ name = e.name, joint = e.joint, source = rest[e.joint], target = current[e.joint] });
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
        constants = cage.bake(source);
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

    // Pure regeneration: forward-kinematic joints from the current bone lengths, then lay the
    // baked cross-sections on top. Runs on every length edit.
    public void update_cage(){
        if(constants != null){
            var lengths = measure().ToDictionary(b => b.joint, b => b.native_length);
            cage_view.sharedMesh = cage.build(lengths, constants);
        }
    }

    void OnDrawGizmosSelected(){
        if(cage_view != null && cage_view.sharedMesh != null){
            // The cage child is identity-local under the rig root, so everything below shares
            // the live (deformed) cage space.
            Gizmos.matrix = cage_view.transform.localToWorldMatrix;

            Gizmos.color = new Color(0.2f, 0.9f, 1f);
            Gizmos.DrawWireMesh(cage_view.sharedMesh);

            // Segments in self-collision, highlighted on the live cage.
            if(collide_segments != null){
                Gizmos.color = Color.red;
                var verts = cage_view.sharedMesh.vertices;
                foreach(var a in collide_segments){
                    draw_segment_box(verts, a);
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

    // A tube segment is the box spanning ring a to ring a+1: 4 corners each, matched by index.
    static void draw_segment_box(Vector3[] verts, int a){
        var lo = a * 4;
        var hi = (a + 1) * 4;
        for(var k = 0; k < 4; k++){
            var kn = (k + 1) % 4;
            Gizmos.DrawLine(verts[lo + k], verts[lo + kn]);
            Gizmos.DrawLine(verts[hi + k], verts[hi + kn]);
            Gizmos.DrawLine(verts[lo + k], verts[hi + k]);
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(mapping_tester))]
    public class inspector: Editor{
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
                    mapping.update_cage();
                }

                foreach(var b in mapping.measure()){
                    EditorGUI.BeginChangeCheck();
                    var length = EditorGUILayout.Slider(b.name, b.length, b.rest * 0.5f, b.rest * 1.5f);

                    if(EditorGUI.EndChangeCheck()){
                        Undo.RecordObject(b.target, "edit bone length");
                        b.length = length;
                        mapping.update_cage();
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("cage", EditorStyles.boldLabel);

                if(GUILayout.Button("rebuild cage")){
                    mapping.constants = cage.bake(mapping.source);
                    mapping.update_cage();
                }

                if(mapping.constants != null && GUILayout.Button("check containment")){
                    var lengths = mapping.measure().ToDictionary(b => b.joint, b => b.native_length);
                    mapping.outside_points = cage.find_outside(mapping.source, mapping.target, lengths, mapping.constants).ToArray();
                    Debug.Log($"cage: {mapping.outside_points.Length} / {mapping.source.sharedMesh.vertexCount} mesh vertices outside");
                }

                if(mapping.constants != null && GUILayout.Button("check self-collision")){
                    var lengths = mapping.measure().ToDictionary(b => b.joint, b => b.native_length);
                    mapping.collide_segments = cage.self_overlaps(lengths, mapping.constants).ToArray();
                    Debug.Log($"cage: {mapping.collide_segments.Length} segments in self-collision");
                }
            }
        }
    }
#endif
}
