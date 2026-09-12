using System;
using System.IO;

// The demo's rest side as one file: the baked constants as JsonUtility text, then the bind, so a
// build maps the body without solving anything. Written by mapping_tester's "export demo bake" and
// by tools/cage_sweep --bake (from the same exported constants); read back by
// mapping_tester.import(TextAsset). The constants travel as text because JsonUtility is Unity's and
// the sweep has only CoreModule, so each side parses them with what it has.
public static class cage_bake{
    const int version = 1;

    public static void write(Stream to, string constants_json, cage_bind b){
        using(var f = new BinaryWriter(to)){
            f.Write(version);
            f.Write(constants_json);
            f.Write((int)b.coords);
            f.Write(b.w.Length / b.stride);
            f.Write(b.stride);
            var bytes = new byte[b.w.Length * sizeof(float)];
            Buffer.BlockCopy(b.w, 0, bytes, 0, bytes.Length);
            f.Write(bytes);
        }
    }

    public static (string constants_json, cage_bind bound) read(byte[] bytes){
        using(var f = new BinaryReader(new MemoryStream(bytes))){
            var v = f.ReadInt32();
            if(v != version){
                throw new InvalidDataException($"cage bake version {v}, expected {version} -- export it again");
            }
            var json = f.ReadString();
            var coords = (cage_coords)f.ReadInt32();
            var points = f.ReadInt32();
            var stride = f.ReadInt32();
            var w = new float[points * stride];
            Buffer.BlockCopy(f.ReadBytes(w.Length * sizeof(float)), 0, w, 0, w.Length * sizeof(float));
            return (json, new cage_bind{ coords = coords, stride = stride, w = w });
        }
    }
}
