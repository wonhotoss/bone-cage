using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
#endif

// What the skeleton does on top of the mapped body. Nothing is keyed: each is a few joints turned
// as a function of time about the rig's own axes, so no clip has to be authored for this skeleton
// and the same motion reads the same on any body the sliders make.
public enum demo_motion{ rest, wave, walk, stretch, twist }

// The live demo: mapping_tester's inspector as a runtime panel. The rest side comes from the bake,
// so the body stands up at once; from then on every slider is a length edit that update_body
// carries through the cage into the mesh and its rest pose, and the motion poses the result.
[RequireComponent(typeof(UIDocument))]
public class demo : MonoBehaviour{
    public mapping_tester mapping;
    public TextAsset bake;          // what mapping_tester's "export demo bake" wrote
    public Material wire_material;  // the cage's edges
    public Renderer rest_body;      // the source body beside the mapped one, for comparison

    // True while the pointer is over the panel; the camera reads it so a drag on a slider does
    // not also turn the view.
    public bool pointer_over_ui{ get; private set; }

    demo_motion motion;

    // A length changed this frame; the body follows once, in LateUpdate, however many sliders moved.
    bool dirty;

    // The editable bones and their rest rotations, fixed by the import. Measured once: the motions
    // look joints up in them every frame, and once both skeletons move nothing else holds the rest.
    mapping_tester.bone[] bones;
    Quaternion[] rest;

    // The wire: the cage's declared edges, or every triangle edge, strung over the live control points.
    Mesh wire;
    MeshRenderer wire_view;

    // The rig's cardinal axes in Hips space, derived as the bake derives them (cage.md 1). Where a
    // motion says up, forward or to the side, it means these.
    Vector3 up, side, depth;

    void Start(){
        mapping.import(bake);
        bones = mapping.measure().ToArray();
        rest = bones.Select(b => b.source.localRotation).ToArray();

        var k = mapping.constants;
        var jc = cage.joint_centers(new Dictionary<string, float>(), k);
        Vector3 at(string joint){
            return jc[Array.IndexOf(k.joint_name, joint)];
        }
        up = cage.cardinal(at("Head") - at("Hips"));
        side = cage.cardinal(at("LeftArm") - at("RightArm"));
        depth = Vector3.Cross(up, side);
        depth *= Mathf.Sign(Vector3.Dot(at("LeftToeBase") - at("LeftFoot"), depth));

        // A sibling of the cage under the rig root, so it shares the live cage space.
        wire = new Mesh{ name = "cage wire" };
        var view = new GameObject("wire");
        view.transform.SetParent(mapping.target.rootBone, false);
        view.AddComponent<MeshFilter>().sharedMesh = wire;
        wire_view = view.AddComponent<MeshRenderer>();
        wire_view.sharedMaterial = wire_material;
        wire_view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wire_view.receiveShadows = false;
        refresh_wire();

        bind_ui();
    }

    void bind_ui(){
        var root = GetComponent<UIDocument>().rootVisualElement;
        root.pickingMode = PickingMode.Ignore; // the empty screen stays click-through; only the panel is pickable
        var panel = root.Q("panel");
        panel.RegisterCallback<PointerEnterEvent>(_ => pointer_over_ui = true);
        panel.RegisterCallback<PointerLeaveEvent>(_ => pointer_over_ui = false);

        // Five sliders over groups of bones, then one per bone. A group slider moves the bones under
        // it, so their sliders follow it; reset puts every one back.
        var per_bone = new List<(mapping_tester.bone b, Slider s)>();
        void show_lengths(){
            foreach(var (b, s) in per_bone){
                s.SetValueWithoutNotify(b.length / b.rest);
            }
        }

        var groups = Enum.GetValues(typeof(cage_shape.group)).Cast<cage_shape.group>()
            .Select(g => (g, s: root.Q<Slider>(g.ToString()))).ToArray();
        foreach(var (g, s) in groups){
            s.RegisterValueChangedCallback(e => {
                mapping.rescale(g, e.newValue);
                show_lengths();
                dirty = true;
            });
        }
        root.Q<Button>("reset").clicked += () => {
            mapping.reset_lengths();
            foreach(var (g, s) in groups){
                s.SetValueWithoutNotify(1f);
            }
            show_lengths();
            dirty = true;
        };

        var list = root.Q("bones");
        foreach(var g in bones.GroupBy(b => b.group)){
            // Fifteen bones per hand would bury the body, so each hand folds into one header.
            VisualElement into = list;
            if(g.Key != ""){
                var hand = new Foldout{ text = g.Key, value = false };
                list.Add(hand);
                into = hand;
            }
            foreach(var b in g){
                var s = new Slider(b.name, 0.5f, 1.5f){ showInputField = true };
                s.RegisterValueChangedCallback(e => {
                    b.length = b.rest * e.newValue;
                    dirty = true;
                });
                into.Add(s);
                per_bone.Add((b, s));
            }
        }
        show_lengths();

        root.Q<Toggle>("show_cage").RegisterValueChangedCallback(e => wire_view.enabled = e.newValue);
        var all_edges = root.Q<Toggle>("all_edges");
        all_edges.SetValueWithoutNotify(mapping.all_edges);
        all_edges.RegisterValueChangedCallback(e => {
            mapping.all_edges = e.newValue;
            refresh_wire();
        });

        var motions = root.Q<DropdownField>("motion");
        motions.choices = Enum.GetNames(typeof(demo_motion)).ToList();
        motions.SetValueWithoutNotify(motion.ToString());
        motions.RegisterValueChangedCallback(e => motion = Enum.Parse<demo_motion>(e.newValue));

        root.Q<Toggle>("rest_body").RegisterValueChangedCallback(e => rest_body.enabled = e.newValue);
    }

    // Rest first: update_body reads both skeletons standing at rest -- the bind space off the
    // source, the rest pose off the target -- so the motion comes off before it and goes back on
    // after, on the rest body and the mapped one alike.
    void LateUpdate(){
        for(var i = 0; i < bones.Length; i++){
            bones[i].source.localRotation = rest[i];
            bones[i].target.localRotation = rest[i];
        }
        if(dirty){
            mapping.update_body();
            refresh_wire();
            dirty = false;
        }

        // A joint turned by q, given in the world frame the rest rig stands in, is the same turn
        // seen from its parent's rest frame on top of its rest rotation. Every local rotation is
        // computed while the skeleton still stands at rest and applied afterwards, so no posed
        // parent is ever read as a frame, and a child's turn rides on whatever its parent does.
        var posed = poses(Time.time).Select(p => {
            var i = Array.FindIndex(bones, b => b.joint == p.joint);
            var parent = bones[i].source.parent.rotation;
            return (i, local: Quaternion.Inverse(parent) * p.q * parent * rest[i]);
        }).ToArray();
        foreach(var (i, local) in posed){
            bones[i].source.localRotation = local;
            bones[i].target.localRotation = local;
        }
    }

    // The same choice the tester's gizmo makes: the edges the design document declares, or the
    // whole triangulation when the shell is to be read as a surface.
    void refresh_wire(){
        var k = mapping.constants;
        var edges = mapping.all_edges
            ? Enumerable.Range(0, k.tris.Length / 3)
                .SelectMany(t => Enumerable.Range(0, 3).Select(e => (k.tris[t * 3 + e], k.tris[t * 3 + (e + 1) % 3])))
                .Select(e => (a: Mathf.Min(e.Item1, e.Item2), b: Mathf.Max(e.Item1, e.Item2))).Distinct()
            : cage.frame(k);
        wire.Clear();
        wire.vertices = mapping.cage_view.sharedMesh.vertices;
        wire.SetIndices(edges.SelectMany(e => new[]{ e.a, e.b }).ToArray(), MeshTopology.Lines, 0);
        wire.RecalculateBounds();
    }

    // A turn about one of the rig's axes, and a swing: the turn that carries the direction a limb
    // points at rest toward another direction, by so many degrees.
    Quaternion turn(Vector3 axis, float deg){
        return Quaternion.AngleAxis(deg, mapping.source.rootBone.TransformDirection(axis));
    }

    Quaternion swing(Vector3 from, Vector3 to, float deg){
        return turn(Vector3.Cross(from, to), deg);
    }

    // Each motion as the turns it puts on the joints at time t. Directions are the limb's own at
    // rest -- the rig stands in a T-pose, so an arm points along its side and a leg down -- and a
    // product applies right to left: the arm is lowered first, then swung.
    IEnumerable<(string joint, Quaternion q)> poses(float t){
        switch(motion){
            case demo_motion.rest:
                return Enumerable.Empty<(string, Quaternion)>();
            case demo_motion.wave: {
                var w = Mathf.Sin(t * 2f * Mathf.PI * 1.5f);
                return new[]{
                    ("LeftArm", swing(side, -up, 70f)),
                    ("RightArm", swing(-side, up, 60f)),
                    ("RightForeArm", swing(-side, up, 70f + 20f * w)),
                    ("Head", swing(up, -side, 6f)),
                };
            }
            case demo_motion.walk: {
                var p = t * 2f * Mathf.PI / 1.2f;   // one stride per 1.2 s
                var s = Mathf.Sin(p);
                var c = Mathf.Cos(p);
                return new[]{
                    ("LeftUpLeg", swing(-up, depth, 30f * s)),
                    ("RightUpLeg", swing(-up, depth, -30f * s)),
                    ("LeftLeg", swing(-up, -depth, 45f * Mathf.Max(0f, c))),    // the knee bends mid-swing
                    ("RightLeg", swing(-up, -depth, 45f * Mathf.Max(0f, -c))),
                    ("LeftArm", swing(-up, depth, -25f * s) * swing(side, -up, 75f)),   // hangs, swings against its leg
                    ("RightArm", swing(-up, depth, 25f * s) * swing(-side, -up, 75f)),
                    ("LeftForeArm", swing(side, depth, 20f)),
                    ("RightForeArm", swing(-side, depth, 20f)),
                    ("Spine1", turn(up, 6f * s)),
                };
            }
            case demo_motion.stretch: {
                var raise = (1f - Mathf.Cos(t * 2f * Mathf.PI / 4f)) * 0.5f;   // 0 -> 1 -> 0 over 4 s
                var lean = Mathf.Sin(t * 2f * Mathf.PI / 4f);
                return new[]{
                    ("LeftArm", swing(side, up, 80f * raise)),
                    ("RightArm", swing(-side, up, 80f * raise)),
                    ("Spine", swing(up, side, 8f * lean)),
                    ("Spine1", swing(up, side, 8f * lean)),
                    ("Spine2", swing(up, side, 6f * lean)),
                    ("Head", swing(up, -side, 10f * lean)),
                };
            }
            case demo_motion.twist: {
                var a = Mathf.Sin(t * 2f * Mathf.PI / 3f);
                return new[]{
                    ("LeftArm", swing(side, -up, 70f)),
                    ("RightArm", swing(-side, -up, 70f)),
                    ("Spine", turn(up, 12f * a)),
                    ("Spine1", turn(up, 12f * a)),
                    ("Spine2", turn(up, 12f * a)),
                    ("Spine3", turn(up, 10f * a)),
                    ("Neck", turn(up, 10f * a)),
                    ("Head", turn(up, 10f * a)),
                };
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(motion), motion, "no such motion");
        }
    }

#if UNITY_EDITOR
    // The web build the docs host: from the menu, or without opening the editor:
    //   Unity.exe -batchmode -quit -projectPath unity -executeMethod demo.build_web
    [MenuItem("Demo/Build Web")]
    static void build_web(){
        var report = BuildPipeline.BuildPlayer(new[]{ "Assets/demo/demo.unity" }, "../docs/unity", BuildTarget.WebGL, BuildOptions.None);
        if(report.summary.result != BuildResult.Succeeded){
            throw new Exception($"demo: web build {report.summary.result}, {report.summary.totalErrors} errors");
        }
    }
#endif
}
