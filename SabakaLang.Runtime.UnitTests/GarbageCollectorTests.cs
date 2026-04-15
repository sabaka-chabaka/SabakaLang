using SabakaLang.Compiler;

namespace SabakaLang.Runtime.UnitTests;

public class GarbageCollectorTests
{
    private static GarbageCollector MakeGc(
        int threshold = 100,
        Func<IEnumerable<SabakaObject>>? roots = null)
        => new(threshold, roots ?? (() => []));

    [Fact]
    public void Alloc_ReturnsFreshObject()
    {
        var gc  = MakeGc();
        var obj = gc.Alloc("MyClass");
        Assert.Equal("MyClass", obj.ClassName);
        Assert.Empty(obj.Fields);
    }

    [Fact]
    public void Alloc_IncrementsTotalAllocated()
    {
        var gc = MakeGc();
        gc.Alloc("A"); gc.Alloc("B"); gc.Alloc("C");
        Assert.Equal(3, gc.TotalAllocated);
    }

    [Fact]
    public void Alloc_AddedToHeap_BeforeCollect()
    {
        var gc = MakeGc();
        gc.Alloc("X"); gc.Alloc("Y");
        // No collect yet — both must be visible
        Assert.Equal(2, gc.LiveCount);
    }

    [Fact]
    public void LiveCount_EqualsAllocatedWhenAllReachable()
    {
        var roots = new List<SabakaObject>();
        var gc    = new GarbageCollector(1000, () => roots);

        for (int i = 0; i < 10; i++)
            roots.Add(gc.Alloc("X"));

        gc.Collect();

        Assert.Equal(10, gc.LiveCount);
    }

    [Fact]
    public void Collect_RemovesUnreachableObjects()
    {
        var roots = new List<SabakaObject>();
        var gc    = new GarbageCollector(1000, () => roots);

        var live = gc.Alloc("Live");
        roots.Add(live);

        gc.Alloc("Dead1");
        gc.Alloc("Dead2");
        gc.Alloc("Dead3");

        gc.Collect();

        Assert.Equal(1, gc.LiveCount);
    }

    [Fact]
    public void Collect_KeepsAllRootObjects()
    {
        var roots = new List<SabakaObject>();
        var gc    = new GarbageCollector(1000, () => roots);

        var a = gc.Alloc("A");
        var b = gc.Alloc("B");
        roots.Add(a);
        roots.Add(b);

        gc.Alloc("Unreachable");

        gc.Collect();

        Assert.Equal(2, gc.LiveCount);
    }

    [Fact]
    public void Collect_AllUnreachable_HeapBecomesEmpty()
    {
        var gc = MakeGc(roots: () => []);
        gc.Alloc("X"); gc.Alloc("Y"); gc.Alloc("Z");

        gc.Collect();

        Assert.Equal(0, gc.LiveCount);
    }

    [Fact]
    public void Collect_UpdatesTotalCollected()
    {
        var gc = MakeGc(roots: () => []);
        gc.Alloc("X"); gc.Alloc("Y");

        gc.Collect();

        Assert.Equal(2, gc.TotalCollected);
    }

    [Fact]
    public void Collect_IncreasesCollectionRuns()
    {
        var gc = MakeGc();
        gc.Collect();
        gc.Collect();
        Assert.Equal(2, gc.CollectionRuns);
    }

    [Fact]
    public void MultipleCollects_AccumulateTotalCollected()
    {
        var gc = MakeGc(roots: () => []);
        gc.Alloc("A"); gc.Alloc("B");
        gc.Collect();   // sweeps 2

        gc.Alloc("C");
        gc.Collect();   // sweeps 1

        Assert.Equal(3, gc.TotalCollected);
    }

    [Fact]
    public void AutoCollect_TriggersAfterThresholdAllocations()
    {
        var gc = new GarbageCollector(5, () => []);

        for (int i = 0; i < 5; i++) gc.Alloc("T");

        Assert.True(gc.CollectionRuns >= 1);
    }

    [Fact]
    public void AutoCollect_DoesNotTriggerBeforeThreshold()
    {
        var gc = new GarbageCollector(10, () => []);
        for (int i = 0; i < 9; i++) gc.Alloc("T");
        Assert.Equal(0, gc.CollectionRuns);
    }

    [Fact]
    public void AutoCollect_ResetsCounterAndCollectsAgain()
    {
        // threshold = 3; allocate 6 → 2 auto-collects
        var gc = new GarbageCollector(3, () => []);
        for (int i = 0; i < 6; i++) gc.Alloc("T");
        Assert.True(gc.CollectionRuns >= 2);
    }

    [Fact]
    public void Collect_MarksReachableThroughObjectField()
    {
        var roots = new List<SabakaObject>();
        var gc    = new GarbageCollector(1000, () => roots);

        var child  = gc.Alloc("Child");
        var parent = gc.Alloc("Parent");
        parent.Fields["child"] = Value.FromObject(child);

        roots.Add(parent);
        gc.Collect();

        Assert.Equal(2, gc.LiveCount);
    }

    [Fact]
    public void Collect_RemovesObjectFieldedByDeadParent()
    {
        var roots = new List<SabakaObject>();
        var gc    = new GarbageCollector(1000, () => roots);

        var child  = gc.Alloc("Child");
        var parent = gc.Alloc("Parent");
        parent.Fields["child"] = Value.FromObject(child);

        gc.Collect();

        Assert.Equal(0, gc.LiveCount);
    }

    [Fact]
    public void Collect_MarksReachableThroughArray()
    {
        var roots = new List<SabakaObject>();
        var gc    = new GarbageCollector(1000, () => roots);

        var item = gc.Alloc("Item");
        var root = gc.Alloc("Root");
        root.Fields["items"] = Value.FromArray([Value.FromObject(item)]);

        roots.Add(root);
        gc.Collect();

        Assert.Equal(2, gc.LiveCount);
    }

    [Fact]
    public void Collect_MarksReachableThroughNestedArray()
    {
        var roots = new List<SabakaObject>();
        var gc    = new GarbageCollector(1000, () => roots);

        var deep = gc.Alloc("Deep");
        var root = gc.Alloc("Root");

        var inner = new List<Value> { Value.FromObject(deep) };
        root.Fields["data"] = Value.FromArray([Value.FromArray(inner)]);

        roots.Add(root);
        gc.Collect();

        Assert.Equal(2, gc.LiveCount);
    }

    [Fact]
    public void Collect_HandlesCircularReferences_BothReachable()
    {
        var roots = new List<SabakaObject>();
        var gc    = new GarbageCollector(1000, () => roots);

        var a = gc.Alloc("A");
        var b = gc.Alloc("B");
        a.Fields["b"] = Value.FromObject(b);
        b.Fields["a"] = Value.FromObject(a);

        roots.Add(a);   // b reachable via a.b
        gc.Collect();

        Assert.Equal(2, gc.LiveCount);
    }

    [Fact]
    public void Collect_HandlesCircularReferences_BothUnreachable()
    {
        var roots = new List<SabakaObject>();
        var gc    = new GarbageCollector(1000, () => roots);

        var a = gc.Alloc("A");
        var b = gc.Alloc("B");
        a.Fields["b"] = Value.FromObject(b);
        b.Fields["a"] = Value.FromObject(a);

        gc.Collect();

        Assert.Equal(0, gc.LiveCount);
    }

    [Fact]
    public void EmptyHeap_CollectDoesNotThrow()
    {
        var gc = MakeGc();
        var ex = Record.Exception(() => gc.Collect());
        Assert.Null(ex);
    }

    [Fact]
    public void Threshold_MustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GarbageCollector(0, () => []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GarbageCollector(-1, () => []));
    }

    [Fact]
    public void TotalAllocated_IsNotAffectedByCollect()
    {
        var gc = MakeGc(roots: () => []);
        gc.Alloc("A"); gc.Alloc("B"); gc.Alloc("C");
        gc.Collect();
        Assert.Equal(3, gc.TotalAllocated);
    }
}