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
        var tiers = arg(args, "--tiers") ?? "1,2,3";
        var count = int.Parse(arg(args, "--random") ?? "20000");
        var seed = int.Parse(arg(args, "--seed") ?? "1");
        var skip = (arg(args, "--skip") ?? "").Split(',').Where(t => t.Length > 0).ToArray();

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

        // The whole-body tier is a Monte Carlo stand-in for the product no sweep can enumerate.
        // Seeded and generated in order, so "random#i" names a case that reproduces exactly.
        var rng = new System.Random(seed);
        var whole = !want.Contains("3") ? Enumerable.Empty<length_case>() :
            Enumerable.Range(0, count).Select(i => new length_case{
                tier = "3 whole", name = $"random#{i}", single = -1,
                ratio = ratios(free.Select(b => (b, lo + (float)rng.NextDouble() * (hi - lo))).ToArray()),
            });

        return single.Concat(pair).Concat(whole.ToArray()).ToArray();
    }

    // The two checks, on the cage the case's lengths build and the body that cage maps.
    static verdict check(length_case c, rest_data d, cage_bind bound){
        var lengths = Enumerable.Range(0, d.bone.Length)
            .ToDictionary(b => d.joint[b], b => rest_length(d, d.joint[b]) * c.ratio[b]);

        var live = cage.points(lengths, d.k);
        var moved = cage_deform.map(bound, live);
        var escaped = cage.outside(moved, live, d.k.tris);
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

    // A case is clean when nothing pierces the shell and the body has not escaped further than it
    // does at rest.
    static bool clean(verdict v, verdict baseline){
        return v.collide == 0 && v.outside <= baseline.outside;
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
        var bad = all.Where(e => !clean(e.v, baseline)).ToArray();
        var r = new StringBuilder();

        r.AppendLine($"# cage sweep -- {DateTime.Now:yyyy-MM-dd HH:mm}");
        r.AppendLine();
        r.AppendLine($"{d.pts.Length} mesh vertices through {d.k.tris.Length / 3} cage triangles, "
            + $"{d.bone.Length} editable bones over rest x [{lo:0.0}, {hi:0.0}], random seed {seed}"
            + (skip.Length > 0 ? $", bones named {string.Join("/", skip)} held at rest" : "") + ".");
        r.AppendLine();
        r.AppendLine($"- rest baseline: **{baseline.outside}** vertices outside, **{baseline.collide}** triangles in self-collision");
        r.AppendLine($"- cases: **{cases.Length}**, failing: **{bad.Length}** "
            + $"(containment {all.Count(e => e.v.outside > baseline.outside)} - self-collision {all.Count(e => e.v.collide > 0)})");
        r.AppendLine();

        foreach(var tier in all.GroupBy(e => e.c.tier).OrderBy(g => g.Key)){
            var n = tier.Count(e => !clean(e.v, baseline));
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
        r.AppendLine("## containment, by the body part the escaped vertices belong to");
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
        r.AppendLine("| bone | clean range | first failure |");
        r.AppendLine("|---|---|---|");
        foreach(var b in Enumerable.Range(0, d.bone.Length)){
            var walk = all.Where(e => e.c.single == b).Select(e => (r: e.c.ratio[b], e)).ToArray();
            if(walk.Length > 0){
                // Walk out from rest in both directions; the range ends at the first failing step.
                var down = walk.Where(w => w.r < 1f).OrderByDescending(w => w.r).ToArray();
                var up = walk.Where(w => w.r > 1f).OrderBy(w => w.r).ToArray();
                var shrink = down.TakeWhile(w => clean(w.e.v, baseline)).ToArray();
                var stretch = up.TakeWhile(w => clean(w.e.v, baseline)).ToArray();
                var broke = down.Skip(shrink.Length).Take(1).Concat(up.Skip(stretch.Length).Take(1))
                    .Select(w => $"{w.r:0.###}: {why(w.e.v, baseline)}");

                r.AppendLine($"| {d.bone[b]} | {(shrink.Length > 0 ? shrink.Last().r : 1f):0.###} - "
                    + $"{(stretch.Length > 0 ? stretch.Last().r : 1f):0.###} | {string.Join("; ", broke)} |");
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
