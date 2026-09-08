using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Bone-length sweep: across the range the tester's sliders allow -- rest x [0.5, 1.5] -- does the
// cage still wrap the body, and does it stay clear of itself?
//
// No skinning is involved anywhere in this pipeline: the skeleton drives the cage and the cage
// drives the mesh, so the body at a set of lengths is the rest mesh mapped through the cage those
// lengths build, and a case is a pure function of its lengths. That is what lets thousands of them
// run outside Unity. cage.cs and cage_deform.cs are compiled straight out of the Unity project
// (see the csproj) so the sweep and the inspector's check buttons run the very same code;
// mapping_tester's "export sweep data" writes the rest side into data/.
//
// The full product of the editable bones is 2^53 corners, so the cases are layered: every bone
// alone across the range, every pair at the four corners of it, and a Monte Carlo sample of whole
// bodies. Cases no real body reaches are deliberately kept -- the point is to find where the recipe
// breaks, not to model a population.
//
// The topology assertions in cage.bake are compiled out here (they are UNITY_ASSERTIONS, and the
// editor holds them); the sweep reads constants the editor already baked and asserted.
static class sweep{
    // The slider range, as mapping_tester draws it.
    const float lo = 0.5f, hi = 1.5f;

    // Steps a single bone is walked through. 1.0 is the baseline and runs once, on its own.
    static readonly float[] steps = { 0.5f, 0.625f, 0.75f, 0.875f, 1.125f, 1.25f, 1.375f, 1.5f };

    // The corners a pair of bones is taken to.
    static readonly float[] corners = { lo, hi };

    // The body sliders, as mapping_tester groups them: three that partition the bones and two that
    // cut the limbs by side. Walking these is the tier that asks about proportions rather than about
    // one bone -- the whole-body tier moves every bone independently, so a taller body, the case the
    // thickness driver is for, never comes up in it. Sides move less: a body is near symmetric.
    static readonly string[] shape = { "torso", "arms", "legs", "left", "right" };
    static readonly float[] shape_steps = { 0.7f, 0.85f, 1f, 1.2f, 1.4f };
    static readonly float[] side_steps = { 0.9f, 1f, 1.1f };

    // The rest side, as "export sweep data" wrote it.
    class rest_data{
        public cage_constants k;
        public Vector3[] pts;       // rest mesh, rig space
        public int[] flesh;         // each vertex's dominant joint, for naming an escape
        public string[] joint;      // the joints the sliders edit
        public string[] bone;       // their anatomical names, in the same order
        public string[] group;      // each cage vertex's name group, for naming a collision
    }

    // One case: what to multiply each editable bone's rest length by, and a name that reads back.
    // A case that walks one bone alone names it, which is what the per-bone table is grouped by.
    class length_case{
        public string tier;
        public string name;
        public float[] ratio;
        public int single;
    }

    // What the two checks found. Escapes are read against the rest baseline, since a cage this
    // coarse escapes at rest already; collisions are absolute -- a clean cage has none.
    class verdict{
        public int outside;
        public int collide;
        public string[] groups;     // cage groups the colliding triangles span
        public string[] parts;      // body parts the escaped vertices belong to, worst first
    }

    static int Main(string[] args){
        var here = Path.GetDirectoryName(Path.GetFullPath(typeof(sweep).Assembly.Location));
        var root = Path.GetFullPath(Path.Combine(here, "../../../"));
        var data = arg(args, "--data") ?? Path.Combine(root, "data");
        var into = arg(args, "--out") ?? Path.Combine(root, "out");
        var tiers = arg(args, "--tiers") ?? "1,2,3,4";
        var count = int.Parse(arg(args, "--random") ?? "20000");
        var seed = int.Parse(arg(args, "--seed") ?? "1");
        var skip = (arg(args, "--skip") ?? "").Split(',').Where(t => t.Length > 0).ToArray();
        var probe_by = float.Parse(arg(args, "--probe") ?? "0");

        if(!File.Exists(Path.Combine(data, "constants.json"))){
            Console.Error.WriteLine($"no sweep data in {data} -- press \"export sweep data\" on the mapping tester first");
            return 1;
        }

        var d = load(data);
        Console.WriteLine($"cage: {d.k.rings.Length * 4 + d.k.posts.Length * 2} control points, {d.k.tris.Length / 3} triangles, "
            + $"{d.pts.Length} mesh vertices, {d.bone.Length} editable bones");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var rest_cage = cage.points(new Dictionary<string, float>(), d.k);
        var bound = cage_deform.bind(cage_coords.mvc, d.pts, rest_cage, d.k.tris);
        Console.WriteLine($"bound the rest mesh to the rest cage through {cage_coords.mvc} in {clock.Elapsed.TotalSeconds:0.0} s");

        if(probe_by > 0f){
            probe(d, bound, probe_by);
            return 0;
        }

        var baseline = check(rest(d), d, bound);
        Console.WriteLine($"rest baseline: {baseline.outside} / {d.pts.Length} vertices outside, {baseline.collide} triangles in self-collision");

        var cases = build(d, tiers, count, seed, skip);
        Console.WriteLine($"running {cases.Length} cases on {Environment.ProcessorCount} cores");

        clock.Restart();
        var found = new verdict[cases.Length];
        var done = 0;
        Parallel.For(0, cases.Length, i => {
            found[i] = check(cases[i], d, bound);
            var n = Interlocked.Increment(ref done);
            if(n % 250 == 0){
                Console.Write($"\r  {n} / {cases.Length}   ");
            }
        });
        Console.WriteLine($"\r  {cases.Length} / {cases.Length} in {clock.Elapsed.TotalMinutes:0.0} min");

        Directory.CreateDirectory(into);
        write_csv(Path.Combine(into, "results.csv"), cases, found, baseline);
        var report = write_report(Path.Combine(into, "report.md"), d, cases, found, baseline, seed, skip);
        Console.WriteLine();
        Console.WriteLine(report);
        Console.WriteLine($"written to {into}");
        return 0;
    }

    static string arg(string[] args, string name){
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    static rest_data load(string dir){
        var k = JsonSerializer.Deserialize<cage_constants>(File.ReadAllText(Path.Combine(dir, "constants.json")),
            new JsonSerializerOptions{ IncludeFields = true });

        var named = File.ReadAllLines(Path.Combine(dir, "bones.txt"))
            .Where(l => l.Length > 0).Select(l => l.Split('\t')).ToArray();

        // Sequential reads, so a loop rather than a query.
        using var f = new BinaryReader(File.OpenRead(Path.Combine(dir, "rest.bin")));
        var n = f.ReadInt32();
        var pts = new Vector3[n];
        for(var i = 0; i < n; i++){
            pts[i] = new Vector3(f.ReadSingle(), f.ReadSingle(), f.ReadSingle());
        }
        var flesh = new int[n];
        for(var i = 0; i < n; i++){
            flesh[i] = f.ReadInt32();
        }

        var group = new string[k.rings.Length * 4 + k.posts.Length * 2];
        foreach(var g in cage.named(k)){
            foreach(var v in g.verts){
                group[v] = g.name;
            }
        }

        return new rest_data{
            k = k, pts = pts, flesh = flesh, group = group,
            joint = named.Select(e => e[0]).ToArray(),
            bone = named.Select(e => e[1]).ToArray(),
        };
    }

    static length_case rest(rest_data d){
        return new length_case{ tier = "0 rest", name = "rest", single = -1, ratio = Enumerable.Repeat(1f, d.bone.Length).ToArray() };
    }

    // Bones named in skip stay at their rest length, so a sweep can ask about one region at a time:
    // with every finger free the whole-body tier fails on the hands and says nothing about the body.
    static length_case[] build(rest_data d, string tiers, int count, int seed, string[] skip){
        var n = d.bone.Length;
        var want = tiers.Split(',').Select(t => t.Trim()).ToHashSet();
        var free = Enumerable.Range(0, n).Where(b => !skip.Any(t => d.bone[b].Contains(t))).ToArray();

        float[] ratios(params (int bone, float r)[] edits){
            var v = Enumerable.Repeat(1f, n).ToArray();
            foreach(var e in edits){
                v[e.bone] = e.r;
            }
            return v;
        }

        var single = !want.Contains("1") ? Enumerable.Empty<length_case>() :
            from b in free
            from r in steps
            select new length_case{ tier = "1 single", name = $"{d.bone[b]}={r:0.###}", single = b, ratio = ratios((b, r)) };

        var pair = !want.Contains("2") ? Enumerable.Empty<length_case>() :
            from i in Enumerable.Range(0, free.Length)
            from j in Enumerable.Range(i + 1, free.Length - i - 1)
            let a = free[i]
            let b = free[j]
            from ra in corners
            from rb in corners
            select new length_case{ tier = "2 pair", name = $"{d.bone[a]}={ra:0.###} + {d.bone[b]}={rb:0.###}", single = -1, ratio = ratios((a, ra), (b, rb)) };

        // The five body groups, read off the skeleton the way mapping_tester reads them: the arms
        // are the subtrees under the clavicles, the legs those under the hips, the torso what is
        // left. A bone takes the product of every group covering it, so an arm carries both arms
        // and its side.
        bool under(int b, string root){
            for(var j = Array.IndexOf(d.k.joint_name, d.joint[b]); j >= 0; j = d.k.joint_parent[j]){
                if(d.k.joint_name[j] == root){
                    return true;
                }
            }
            return false;
        }
        var covers = new Func<int, bool>[]{
            b => !under(b, "LeftArm") && !under(b, "RightArm") && !under(b, "LeftUpLeg") && !under(b, "RightUpLeg"),
            b => under(b, "LeftArm") || under(b, "RightArm"),
            b => under(b, "LeftUpLeg") || under(b, "RightUpLeg"),
            b => under(b, "LeftArm") || under(b, "LeftUpLeg"),
            b => under(b, "RightArm") || under(b, "RightUpLeg"),
        };
        length_case proportion_case(float[] by){
            return new length_case{
                tier = "4 proportion", single = -1,
                name = string.Join(" ", shape.Select((g, i) => $"{g}={by[i]:0.##}")),
                ratio = Enumerable.Range(0, n).Select(b => !free.Contains(b) ? 1f
                    : Enumerable.Range(0, covers.Length).Where(i => covers[i](b)).Aggregate(1f, (m, i) => m * by[i])).ToArray(),
            };
        }

        var proportion = !want.Contains("4") ? Enumerable.Empty<length_case>() :
            from t in shape_steps
            from a in shape_steps
            from l in shape_steps
            from le in side_steps
            from ri in side_steps
            select proportion_case(new[]{ t, a, l, le, ri });

        // The whole-body tier is a Monte Carlo stand-in for the product no sweep can enumerate.
        // Seeded and generated in order, so "random#i" names a case that reproduces exactly.
        var rng = new System.Random(seed);
        var whole = !want.Contains("3") ? Enumerable.Empty<length_case>() :
            Enumerable.Range(0, count).Select(i => new length_case{
                tier = "3 whole", name = $"random#{i}", single = -1,
                ratio = ratios(free.Select(b => (b, lo + (float)rng.NextDouble() * (hi - lo))).ToArray()),
            });

        return single.Concat(pair).Concat(proportion).Concat(whole.ToArray()).ToArray();
    }

    // The two checks, on the cage the case's lengths build and the body that cage maps.
    static verdict check(length_case c, rest_data d, cage_bind bound){
        var lengths = Enumerable.Range(0, d.bone.Length)
            .ToDictionary(b => d.joint[b], b => rest_length(d, d.joint[b]) * c.ratio[b]);

        var live = cage.points(lengths, d.k);
        var moved = cage_deform.map(bound, live);
        var escaped = cage.exposed(moved, live, d.k);
        var hit = cage.self_overlaps(lengths, d.k);

        return new verdict{
            outside = escaped.Count,
            collide = hit.Count,
            parts = escaped.GroupBy(i => d.k.joint_name[d.flesh[i]])
                .OrderByDescending(g => g.Count()).Take(4).Select(g => $"{g.Key} {g.Count()}").ToArray(),
            groups = hit.Select(t => string.Join("+", Enumerable.Range(0, 3)
                    .Select(e => d.group[d.k.tris[t * 3 + e]]).Distinct().OrderBy(s => s)))
                .Distinct().OrderBy(s => s).ToArray(),
        };
    }

    static float rest_length(rest_data d, string joint){
        return d.k.joint_rest_len[Array.IndexOf(d.k.joint_name, joint)];
    }

    // How much of a ring's own widening the mesh inside it actually takes. The thickness driver
    // will declare a section as a ratio on the rest one, so what has to be known first is what that
    // declaration is worth once the coordinates have carried it: widen one ring by k, map the rest
    // mesh through the cage that makes, and measure the flesh across the same window the bake
    // measured it in. Bones stay at rest throughout, so nothing but the one ring has moved.
    //
    // A single ring is a local bulge, not an affine widening -- the rings either side of it stay
    // put and pull back -- so the transfer is expected below k. The last row widens every ring at
    // once, which is what a driver on the whole body would do, and is the upper end of the range.
    static void probe(rest_data d, cage_bind bound, float by){
        // The bake's measurement window, mirrored: flesh within this much of the ring's plane,
        // scaled to the anchor bone. See cage.md 1 (measurement window).
        const float window = 0.25f;

        var lengths = d.joint.ToDictionary(j => j, j => rest_length(d, j));
        var jc = cage.joint_centers(lengths, d.k);

        // The flesh a ring speaks for: its anchors' subtree, as gather_flesh assigns it, cut down to
        // the window so a spine ring takes the waist rather than everything above it.
        bool descends(int joint, int of){
            for(var j = joint; j >= 0; j = d.k.joint_parent[j]){
                if(j == of){
                    return true;
                }
            }
            return false;
        }

        // A section is the ring and the midline post standing on it: the post's ends are baked at
        // the ring's own depth (cage.md 3b), so scaling the ring alone leaves the midline behind and
        // the panel folds inward there -- seen from above the section becomes a bowtie, which is not
        // a wider body. So the two move together, which is what "widen a section" has to mean.
        // Not every post on a ring is named for it: neck mid and sternum mid stand on the arm rings,
        // and the hand is posts throughout, so those stay put here and their rows read low.
        void scale(cage_ring r, float k){
            r.s_hi *= k; r.s_lo *= k;
            r.hi_front *= k; r.lo_front *= k; r.hi_back *= k; r.lo_back *= k;
            foreach(var m in d.k.posts.Where(q => q.name == $"{r.name} mid")){
                m.d_hi *= k; m.d_lo *= k;
            }
        }

        Vector3[] mapped(cage_ring[] widened){
            foreach(var t in widened){
                scale(t, by);
            }
            var live = cage.points(lengths, d.k);
            foreach(var t in widened){
                scale(t, 1f / by);
            }
            return cage_deform.map(bound, live);
        }

        (float across, float deep) spread(Vector3[] p, cage_ring r, int[] at){
            return (at.Max(i => Vector3.Dot(p[i], r.s)) - at.Min(i => Vector3.Dot(p[i], r.s)),
                at.Max(i => Vector3.Dot(p[i], r.d)) - at.Min(i => Vector3.Dot(p[i], r.d)));
        }

        // Every ring widened at once is what a driver on the whole body does, and the upper end of
        // what one ring alone can be worth; it is the same cage for every row, so map it once.
        var all = mapped(d.k.rings);

        Console.WriteLine();
        Console.WriteLine($"widen a ring's section by x{by:0.##}, bones at rest, and measure the flesh across the bake's own window.");
        Console.WriteLine("transfer = (mesh - 1) / (cage - 1): 1.00 is the declaration arriving whole.");
        Console.WriteLine();
        Console.WriteLine("| ring | flesh | rest across / deep (cm) | that ring, across / deep | every ring, across / deep |");
        Console.WriteLine("|---|---|---|---|---|");

        foreach(var r in d.k.rings){
            var anchors = r.anchor_hi.Concat(r.anchor_lo).Distinct().ToArray();
            var plane = anchors.Max(j => Vector3.Dot(jc[j], r.n));
            var reach = window * anchors.Max(j => d.k.joint_rest_len[j]);
            var at = Enumerable.Range(0, d.pts.Length)
                .Where(i => anchors.Any(j => descends(d.flesh[i], j))
                    && Math.Abs(Vector3.Dot(d.pts[i], r.n) - plane) <= reach).ToArray();

            if(at.Length == 0){
                Console.WriteLine($"| {r.name} | 0 | -- no flesh in the window -- | | |");
                continue;
            }

            var was = spread(d.pts, r, at);
            var one = spread(mapped(new[]{ r }), r, at);
            var every = spread(all, r, at);
            string rate((float across, float deep) now){
                return $"**{(now.across / was.across - 1f) / (by - 1f):0.00}** / **{(now.deep / was.deep - 1f) / (by - 1f):0.00}**";
            }
            Console.WriteLine($"| {r.name} | {at.Length} | {was.across * 10000:0.0} / {was.deep * 10000:0.0} | "
                + $"{rate(one)} | {rate(every)} |");
        }
    }

    // A case is clean when nothing pierces the shell. Vertices that leave the deformed cage are
    // not a failure: the coordinates were fixed against the rest cage and a point landing outside
    // the new shell is not thereby mis-mapped, so escapes are reported as a statistic instead. What
    // does have teeth is the rest cage holding the mesh, which the baseline below checks, and the
    // shell not folding, which is this. See cage.md `[N18]`.
    static bool clean(verdict v){
        return v.collide == 0;
    }

    static void write_csv(string path, length_case[] cases, verdict[] found, verdict baseline){
        string cell(string s){
            return $"\"{s.Replace("\"", "\"\"")}\"";
        }
        var rows = cases.Zip(found, (c, v) => string.Join(",",
            cell(c.tier), cell(c.name), v.outside, v.outside - baseline.outside, v.collide,
            cell(string.Join(" | ", v.groups)), cell(string.Join(" | ", v.parts))));

        File.WriteAllLines(path, new[]{ "tier,case,outside,delta,collide_tris,collide_groups,escaped_parts" }.Concat(rows));
    }

    static string write_report(string path, rest_data d, length_case[] cases, verdict[] found, verdict baseline, int seed, string[] skip){
        var all = cases.Zip(found, (c, v) => (c, v)).ToArray();
        var bad = all.Where(e => !clean(e.v)).ToArray();
        var r = new StringBuilder();

        r.AppendLine($"# cage sweep -- {DateTime.Now:yyyy-MM-dd HH:mm}");
        r.AppendLine();
        r.AppendLine($"{d.pts.Length} mesh vertices through {d.k.tris.Length / 3} cage triangles, "
            + $"{d.bone.Length} editable bones over rest x [{lo:0.0}, {hi:0.0}], random seed {seed}"
            + (skip.Length > 0 ? $", bones named {string.Join("/", skip)} held at rest" : "") + ".");
        r.AppendLine();
        var rest_note = baseline.outside == 0 && baseline.collide == 0 ? "clean"
            : "**the recipe is broken at rest, so everything below is measured against a broken baseline**";
        r.AppendLine($"- rest baseline: **{baseline.outside}** vertices outside, **{baseline.collide}** triangles in self-collision -- {rest_note}");
        r.AppendLine($"- cases: **{cases.Length}**, failing: **{bad.Length}** (self-collision; escapes are a statistic, `[N18]`)");

        var esc = all.Select(e => e.v.outside - baseline.outside).Where(x => x > 0).OrderBy(x => x).ToArray();
        r.AppendLine(esc.Length == 0 ? "- escapes: none"
            : $"- escapes: **{esc.Length}** cases leak, median **{esc[esc.Length / 2]}**, 90th **{esc[(int)(esc.Length * 0.9)]}**, "
                + $"worst **{esc.Last()}** of {d.pts.Length} vertices");
        r.AppendLine();

        foreach(var tier in all.GroupBy(e => e.c.tier).OrderBy(g => g.Key)){
            var n = tier.Count(e => !clean(e.v));
            r.AppendLine($"- {tier.Key}: {n} / {tier.Count()} failing");
        }

        r.AppendLine();
        r.AppendLine("## self-collision, by the cage groups the pierced triangles span");
        r.AppendLine();
        r.AppendLine("| cage groups | cases | worst case |");
        r.AppendLine("|---|---|---|");
        var by_group = all.SelectMany(e => e.v.groups.Select(g => (g, e)))
            .GroupBy(e => e.g).OrderByDescending(g => g.Count()).Take(25);
        foreach(var g in by_group){
            var worst = g.OrderByDescending(e => e.e.v.collide).First().e;
            r.AppendLine($"| {g.Key} | {g.Count()} | {worst.c.name} ({worst.v.collide} tris) |");
        }
        if(!by_group.Any()){
            r.AppendLine("| _none_ | 0 | |");
        }

        r.AppendLine();
        r.AppendLine("## escapes (a statistic, not a failure), by the body part the vertices belong to");
        r.AppendLine();
        r.AppendLine("| joint | worst extra escapes | case |");
        r.AppendLine("|---|---|---|");
        var by_part = all.Where(e => e.v.outside > baseline.outside)
            .SelectMany(e => e.v.parts.Take(1).Select(p => (joint: p.Split(' ')[0], e)))
            .GroupBy(e => e.joint)
            .Select(g => (joint: g.Key, worst: g.OrderByDescending(e => e.e.v.outside).First().e))
            .OrderByDescending(e => e.worst.v.outside).Take(25);
        foreach(var e in by_part){
            r.AppendLine($"| {e.joint} | +{e.worst.v.outside - baseline.outside} | {e.worst.c.name} |");
        }
        if(!by_part.Any()){
            r.AppendLine("| _none_ | 0 | |");
        }

        r.AppendLine();
        r.AppendLine("## per bone: how far it goes alone before something breaks");
        r.AppendLine();
        r.AppendLine("| bone | clean range | worst escape | first failure |");
        r.AppendLine("|---|---|---|---|");
        foreach(var b in Enumerable.Range(0, d.bone.Length)){
            var walk = all.Where(e => e.c.single == b).Select(e => (r: e.c.ratio[b], e)).ToArray();
            if(walk.Length > 0){
                // Walk out from rest in both directions; the range ends at the first failing step.
                var down = walk.Where(w => w.r < 1f).OrderByDescending(w => w.r).ToArray();
                var up = walk.Where(w => w.r > 1f).OrderBy(w => w.r).ToArray();
                var shrink = down.TakeWhile(w => clean(w.e.v)).ToArray();
                var stretch = up.TakeWhile(w => clean(w.e.v)).ToArray();
                var broke = down.Skip(shrink.Length).Take(1).Concat(up.Skip(stretch.Length).Take(1))
                    .Select(w => $"{w.r:0.###}: {why(w.e.v, baseline)}");

                var leak = walk.Max(w => w.e.v.outside - baseline.outside);
                r.AppendLine($"| {d.bone[b]} | {(shrink.Length > 0 ? shrink.Last().r : 1f):0.###} - "
                    + $"{(stretch.Length > 0 ? stretch.Last().r : 1f):0.###} | {(leak > 0 ? "+" + leak : "-")} | {string.Join("; ", broke)} |");
            }
        }

        File.WriteAllText(path, r.ToString());
        return r.ToString();
    }

    static string why(verdict v, verdict baseline){
        var parts = new List<string>();
        if(v.collide > 0){
            parts.Add($"{v.collide} tris collide ({string.Join(", ", v.groups.Take(3))})");
        }
        if(v.outside > baseline.outside){
            parts.Add($"+{v.outside - baseline.outside} escaped ({string.Join(", ", v.parts.Take(2))})");
        }
        return string.Join(" - ", parts);
    }
}
