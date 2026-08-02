using System;
using System.Linq;
using UnityEngine;

// Cage-based vertex mapping: express every mesh vertex in coordinates of the rest cage, then
// rebuild it from the deformed cage. The coordinate vector is the vertex's invariant address
// inside the cage, so a vertex keeps its semantic place ("the navel stays the navel") however the
// cage is edited. Both cages share the baked topology, so their vertex arrays index the same
// control points. Background and the planned upgrades: docs/cage-deformation-plan.md.

public enum cage_coords{
    mvc,
}

// A point set's coordinates in the rest cage. Solving them is the whole cost of the method --
// closed form for mean value coordinates, a boundary integral for Green and Somigliana -- while
// the rest cage that fixes them never moves, so it is solved once and every deformed cage
// afterwards is just a rebuild from these numbers.
public class cage_bind{
    public cage_coords coords;  // which coordinates the weights are, so a method switch rebinds
    public int stride;          // cage control points per bound point
    public float[] w;           // point-major: w[p * stride + i]
}

public static class cage_deform{
    // Bind every point against the rest cage. Green and Somigliana carry per-face terms too and
    // will widen cage_bind, but they bind through this same call.
    public static cage_bind bind(cage_coords coords, Vector3[] pts, Vector3[] rest, int[] tris){
        switch(coords){
            case cage_coords.mvc:
                var w = new float[pts.Length * rest.Length];
                for(var p = 0; p < pts.Length; p++){
                    mvc(pts[p], rest, tris).CopyTo(w, p * rest.Length);
                }
                return new cage_bind{ coords = coords, stride = rest.Length, w = w };
            default:
                throw new ArgumentOutOfRangeException(nameof(coords), coords, "no such cage coordinates");
        }
    }

    // Rebuild every bound point from the deformed cage. Mean value coordinates are a plain weighted
    // sum of the control points; Green and Somigliana add their face normal terms here.
    public static Vector3[] map(cage_bind b, Vector3[] live){
        switch(b.coords){
            case cage_coords.mvc:
                Debug.Assert(b.stride == live.Length, "cage: the bind and the cage disagree on the control point count");
                var moved = new Vector3[b.w.Length / b.stride];
                for(var p = 0; p < moved.Length; p++){
                    var q = Vector3.zero;
                    for(var i = 0; i < b.stride; i++){
                        q += live[i] * b.w[p * b.stride + i];
                    }
                    moved[p] = q;
                }
                return moved;
            default:
                throw new ArgumentOutOfRangeException(nameof(b.coords), b.coords, "no such cage coordinates");
        }
    }

    const float eps = 1e-7f;

    // Mean value coordinates for a closed triangle cage (Ju/Schaefer/Warren 2005). Weights form a
    // partition of unity and reproduce the point linearly, so an affine change of the cage carries
    // the point along exactly. They go negative where the cage is concave -- here, around the
    // armpits and the crotch of the plus-shaped silhouette -- which is this method's known limit.
    // Numeric kernel: written as loops, not LINQ.
    static float[] mvc(Vector3 p, Vector3[] cage, int[] tris){
        var w = new float[cage.Length];
        var d = new float[cage.Length];
        var u = new Vector3[cage.Length];

        for(var i = 0; i < cage.Length; i++){
            var r = cage[i] - p;
            d[i] = r.magnitude;
            if(d[i] < eps){
                // p sits on a control point, so it maps to that control point alone.
                w[i] = 1f;
                return w;
            }
            u[i] = r / d[i];
        }

        var theta = new float[3];
        var c = new float[3];
        var s = new float[3];

        for(var t = 0; t < tris.Length; t += 3){
            // Spherical triangle the face projects to on the unit sphere around p.
            for(var i = 0; i < 3; i++){
                var l = (u[tris[t + (i + 1) % 3]] - u[tris[t + (i + 2) % 3]]).magnitude;
                theta[i] = 2f * Mathf.Asin(Mathf.Clamp(l * 0.5f, -1f, 1f));
            }
            var h = (theta[0] + theta[1] + theta[2]) * 0.5f;

            if(Mathf.PI - h < eps){
                // p lies on this face: the face's own barycentric coordinates are exact, and no
                // other face contributes.
                var flat = new float[cage.Length];
                for(var i = 0; i < 3; i++){
                    flat[tris[t + i]] = Mathf.Sin(theta[i]) * d[tris[t + (i + 1) % 3]] * d[tris[t + (i + 2) % 3]];
                }
                return normalized(flat);
            }

            var det = Vector3.Dot(u[tris[t]], Vector3.Cross(u[tris[t + 1]], u[tris[t + 2]]));
            var sign = det < 0f ? -1f : 1f;
            var spans = true;
            for(var i = 0; i < 3; i++){
                c[i] = 2f * Mathf.Sin(h) * Mathf.Sin(h - theta[i])
                    / (Mathf.Sin(theta[(i + 1) % 3]) * Mathf.Sin(theta[(i + 2) % 3])) - 1f;
                s[i] = sign * Mathf.Sqrt(Mathf.Max(0f, 1f - c[i] * c[i]));
                spans = spans && Mathf.Abs(s[i]) > eps;
            }

            // A face p is coplanar with but outside of contributes nothing.
            if(spans){
                for(var i = 0; i < 3; i++){
                    var i1 = (i + 1) % 3;
                    var i2 = (i + 2) % 3;
                    w[tris[t + i]] += (theta[i] - c[i1] * theta[i2] - c[i2] * theta[i1])
                        / (d[tris[t + i]] * Mathf.Sin(theta[i1]) * s[i2]);
                }
            }
        }
        return normalized(w);
    }

    // Coordinates must sum to one for the rebuild to be affine invariant. The global sign of the
    // raw weights follows the cage winding and cancels here.
    static float[] normalized(float[] w){
        var sum = w.Sum();
        for(var i = 0; i < w.Length; i++){
            w[i] /= sum;
        }
        return w;
    }
}
