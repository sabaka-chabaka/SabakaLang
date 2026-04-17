using SabakaLang.Compiler;

namespace SabakaLang.Runtime;

public sealed class GarbageCollector
{
    private int Threshold { get; }

    private readonly Func<IEnumerable<SabakaObject>> _rootProvider;

    private readonly HashSet<SabakaObject> _heap =
        new(ReferenceEqualityComparer.Instance);

    private int _allocCount;

    public int TotalAllocated { get; private set; }
    public int TotalCollected { get; private set; }
    public int CollectionRuns { get; private set; }

    public GarbageCollector(int threshold, Func<IEnumerable<SabakaObject>> rootProvider)
    {
        if (threshold < 1) throw new ArgumentOutOfRangeException(nameof(threshold));
        Threshold     = threshold;
        _rootProvider = rootProvider;
    }

    public SabakaObject Alloc(string className)
    {
        var obj = new SabakaObject(className);
        _heap.Add(obj);

        TotalAllocated++;
        _allocCount++;

        if (_allocCount >= Threshold)
        {
            Collect();
            _allocCount = 0;
        }

        return obj;
    }
    
    public void Collect()
    {
        CollectionRuns++;

        var alive = new HashSet<SabakaObject>(ReferenceEqualityComparer.Instance);
        foreach (var root in _rootProvider())
            Mark(root, alive);

        int before = _heap.Count;
        var dead = _heap
            .Where(o => !alive.Contains(o))
            .ToList();
        foreach (var o in dead)
            _heap.Remove(o);

        TotalCollected += before - _heap.Count;
    }

    public int LiveCount => _heap.Count;

    private static void Mark(SabakaObject obj, HashSet<SabakaObject> visited)
    {
        if (!visited.Add(obj)) return;

        foreach (var kv in obj.Fields)
        {
            if (kv.Value is { Type: SabakaType.Object, Object: { } child })
                Mark(child, visited);

            if (kv.Value is { Type: SabakaType.Array, Array: { } arr })
                MarkArray(arr, visited);
        }
    }

    private static void MarkArray(List<Value> arr, HashSet<SabakaObject> visited)
    {
        foreach (var v in arr)
        {
            if (v is { Type: SabakaType.Object, Object: { } obj })    Mark(obj, visited);
            if (v is { Type: SabakaType.Array, Array: { } nested }) MarkArray(nested, visited);
        }
    }
}