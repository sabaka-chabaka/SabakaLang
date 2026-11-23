using SabakaLang.Runtime;

namespace SabakaLang.VM;

/// <summary>
/// Tracks script objects and reports how many are still reachable. Memory itself is managed by .NET; this collector
/// keeps the live-object bookkeeping (and statistics) exact. Marking is iterative, so long object chains are safe.
/// </summary>
public sealed class GarbageCollector
{
    private readonly HashSet<SabakaObject> _heap = new(ReferenceEqualityComparer.Instance);
    private int _threshold = 10_000;

    public int LiveCount => _heap.Count;
    public long TotalAllocated { get; private set; }
    public long TotalCollected { get; private set; }
    public int CollectionRuns { get; private set; }

    /// <summary>Supplies the root set (stack, globals) when a collection runs.</summary>
    public Func<IEnumerable<Value>>? Roots { get; set; }

    public SabakaObject Alloc(SabakaObject obj)
    {
        _heap.Add(obj);
        TotalAllocated++;
        if (_heap.Count >= _threshold) Collect();
        return obj;
    }

    public void Collect()
    {
        CollectionRuns++;
        var marked = new HashSet<SabakaObject>(ReferenceEqualityComparer.Instance);
        var arrays = new HashSet<List<Value>>(ReferenceEqualityComparer.Instance);
        var work = new Stack<Value>();
        foreach (var r in Roots?.Invoke() ?? []) work.Push(r);

        while (work.Count > 0)
        {
            var v = work.Pop();
            switch (v.Type)
            {
                case SabakaType.Object:
                    if (marked.Add(v.Object!))
                        foreach (var f in v.Object!.Fields.Values) work.Push(f);
                    break;
                case SabakaType.Array:
                    if (arrays.Add(v.Array!))
                        foreach (var x in v.Array!) work.Push(x);
                    break;
            }
        }

        var before = _heap.Count;
        _heap.IntersectWith(marked);
        TotalCollected += before - _heap.Count;
        _threshold = Math.Max(10_000, _heap.Count * 2);
    }
}
