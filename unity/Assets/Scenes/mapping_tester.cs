using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class mapping_tester : MonoBehaviour{
    public SkinnedMeshRenderer source;
    public SkinnedMeshRenderer target;

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

        //

        // target.rootBone.transform.localRotation = source.rootBone.localRotation;
        // target.rootBone.transform.localScale = source.rootBone.localScale;
        // target.transform.localRotation = source.transform.localRotation;
        // target.transform.localScale = source.transform.localScale;
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
        }
    }
#endif
}
