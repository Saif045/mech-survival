using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MechSurvival.World;

/// <summary>Concrete expedition data. Stable PRNG and ordered traversal are independent of Godot/global RNG.</summary>
public sealed class ExpeditionLayout
{
    public sealed record Room(int X, int Z, int Template, int[] Connections);
    public const int CellSize = 42;
    public int Seed { get; }
    public IReadOnlyList<Room> Rooms { get; }
    public int Start => 0;
    public int Extraction { get; }
    public IReadOnlyList<int> Salvage { get; }
    public IReadOnlyList<int> Parasites { get; }
    public string Signature { get; }
    private ExpeditionLayout(int seed, Room[] rooms, int extraction, int[] salvage, int[] parasites)
    {
        Seed = seed; Rooms = Array.AsReadOnly(rooms); Extraction = extraction;
        Salvage = Array.AsReadOnly(salvage); Parasites = Array.AsReadOnly(parasites);
        string data = string.Join(";", rooms.Select(r => $"{r.X},{r.Z},{r.Template}:{string.Join(',', r.Connections)}")) +
            $"|{extraction}|{string.Join(',', salvage)}|{string.Join(',', parasites)}";
        Signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)))[..16];
    }
    public static ExpeditionLayout Generate(int seed, int count = 18)
    {
        if (count is < 16 or > 20) throw new ArgumentOutOfRangeException(nameof(count));
        uint state = unchecked((uint)seed) ^ 0xA341316Cu;
        int Next(int n) { state = unchecked(state * 1664525u + 1013904223u); return (int)((state >> 8) % (uint)n); }
        var cells = new List<(int X, int Z)> { (0, 0), (1, 0), (1, 1), (0, 1) };
        var directions = new[] { (X: 0, Z: -1), (X: 1, Z: 0), (X: 0, Z: 1), (X: -1, Z: 0) };
        while (cells.Count < count - 3)
        {
            var frontier = cells.SelectMany(c => directions.Select(d => (X: c.X + d.X, Z: c.Z + d.Z)))
                .Where(c => !cells.Contains(c)).Distinct().OrderBy(c => c.X).ThenBy(c => c.Z).ToArray();
            cells.Add(frontier[Next(frontier.Length)]);
        }
        // Reserve a three-cell service spur: a guaranteed dead end and a long extraction approach.
        var tip = cells.OrderByDescending(c => c.X).ThenBy(c => c.Z).First();
        for (int step = 1; step <= 3; step++) cells.Add((tip.X + step, tip.Z));
        var rooms = cells.Select(c => new Room(c.X, c.Z, Next(6), cells.Select((p, i) => (p, i))
            .Where(p => Math.Abs(p.p.X - c.X) + Math.Abs(p.p.Z - c.Z) == 1).Select(p => p.i).ToArray())).ToArray();
        // All cardinal adjacencies connect: frontier growth plus the initial square guarantees an alternate route.
        var temporary = new ExpeditionLayout(seed, rooms, 0, Array.Empty<int>(), Array.Empty<int>());
        var distances = Enumerable.Range(0, count).Select(i => temporary.Path(0, i).Length - 1).ToArray();
        int far = distances.Max();
        var candidates = Enumerable.Range(1, count - 1).Where(i => distances[i] >= far - 1)
            .OrderBy(i => rooms[i].Connections.Length == 1 ? 0 : 1).ThenByDescending(i => distances[i]).ThenBy(i => i).ToArray();
        int extraction = candidates[0];
        var eligible = Enumerable.Range(1, count - 1).Where(i => i != extraction).ToList();
        for (int i = eligible.Count - 1; i > 0; i--) { int j = Next(i + 1); (eligible[i], eligible[j]) = (eligible[j], eligible[i]); }
        return new(seed, rooms, extraction, eligible.Take(5).ToArray(), eligible.Skip(5).Take(3).ToArray());
    }
    public int[] Path(int from, int to)
    {
        var previous = Enumerable.Repeat(-1, Rooms.Count).ToArray(); var queue = new Queue<int>();
        previous[from] = from; queue.Enqueue(from);
        while (queue.Count > 0)
        {
            int r = queue.Dequeue(); if (r == to) break; foreach (int next in Rooms[r].Connections)
                if (previous[next] < 0) { previous[next] = r; queue.Enqueue(next); }
        }
        if (previous[to] < 0) return Array.Empty<int>();
        var path = new List<int>(); for (int r = to; ; r = previous[r]) { path.Add(r); if (r == from) break; }
        path.Reverse(); return path.ToArray();
    }
}
