using System;
using System.Linq;
using UnityEngine;

// The body's skeleton as cubes and lines: a cube on every bone the mesh is skinned to, a line from
// each to its parent, rebuilt every frame from the live transforms. A plain mesh, so it shows in
// the scene view, the player and the web build alike; the material draws it over everything
// rather than reading depth. Enabling the component shows it, disabling hides it.
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class skeleton_view : MonoBehaviour{
    public SkinnedMeshRenderer body;
    public float joint_size = 0.02f;   // cube edge, scene units

    // A unit cube about the origin: corner i has bit 0 for x, 1 for y, 2 for z; then the two
    // triangles of each face over those corners. The material culls nothing, so winding is free.
    static readonly Vector3[] corners = Enumerable.Range(0, 8)
        .Select(i => new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f)).ToArray();
    static readonly int[] faces = {
        0, 2, 6, 0, 6, 4,   // -x
        1, 3, 7, 1, 7, 5,   // +x
        0, 1, 5, 0, 5, 4,   // -y
        2, 3, 7, 2, 7, 6,   // +y
        0, 1, 3, 0, 3, 2,   // -z
        4, 5, 7, 4, 7, 6,   // +z
    };

    Mesh mesh;

    void OnEnable(){
        mesh = new Mesh{ name = "skeleton", hideFlags = HideFlags.DontSave };
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    void OnDisable(){
        DestroyImmediate(mesh);
    }

    void LateUpdate(){
        var bones = body.bones;
        var to_local = transform.worldToLocalMatrix;
        var at = bones.Select(b => to_local.MultiplyPoint3x4(b.position)).ToArray();

        // Joint centers first, for the lines; then eight corners per joint, for the cubes.
        mesh.Clear();
        mesh.vertices = at.Concat(at.SelectMany(p => corners.Select(c => p + c * joint_size))).ToArray();
        mesh.subMeshCount = 2;
        mesh.SetIndices(Enumerable.Range(0, at.Length).SelectMany(i => faces.Select(f => at.Length + i * 8 + f)).ToArray(), MeshTopology.Triangles, 0);
        mesh.SetIndices(bones.Select((b, i) => (i, parent: Array.IndexOf(bones, b.parent))).Where(e => e.parent >= 0)
            .SelectMany(e => new[]{ e.i, e.parent }).ToArray(), MeshTopology.Lines, 1);
        mesh.RecalculateBounds();
    }
}
