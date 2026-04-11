using SabakaLang.Compiler;

namespace SabakaLang.Runtime.UnitTests;

public class GarbageCollectorTests
{
    private static GarbageCollector MakeGc(int threshold = 100,
        Func<IEnumerable<SabakaObject>>? roots = null)
        => new(threshold, roots ?? (() => []));
 
    private static SabakaObject MakeObj(string name = "T")
        => new(name);

    [Fact]
    public void Alloc_ReturnsFreshObject()
    {
        var gc = MakeGc();
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
    public void LiveCount_EqualsAllocatedWhenAllReachable()
    {
        var roots = new List<SabakaObject>();
        var gc = new GarbageCollector(1000, () => roots);
    
        for (int i = 0; i < 10; i++)
        {
            roots.Add(gc.Alloc("X"));
        }

        gc.Collect();

        Assert.Equal(10, gc.LiveCount);
    }
    
    [Fact]
    public void Collect_RemovesUnreachableObjects()
    {
        var roots = new List<SabakaObject>();
        var gc = new GarbageCollector(1000, () => roots);

        var live = gc.Alloc("Live");
        roots.Add(live);

        gc.Alloc("Dead1");
        gc.Alloc("Dead2");
        gc.Alloc("Dead3");

        gc.Collect();

        Assert.Equal(1, gc.LiveCount);
    }

    [Fact]
    public void Collect_KeepsReachableObjects()
    {
        var roots = new List<SabakaObject>();
        var gc = new GarbageCollector(1000, () => roots);

        var a = gc.Alloc("A");
        var b = gc.Alloc("B");

        roots.Add(a);
        roots.Add(b);

        gc.Alloc("Unreachable");

        gc.Collect();
        
        Assert.Equal(2, gc.LiveCount);
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
    public void Collect_MarksReachableThroughField()
    {
        var child  = MakeObj("Child");
        var parent = MakeObj("Parent");
        parent.Fields["child"] = Value.FromObject(child);
 
        var gc = new GarbageCollector(1000, () => [parent]);
        
        var allocedParent = gc.Alloc("Parent");
        var allocedChild  = gc.Alloc("Child");
        allocedParent.Fields["child"] = Value.FromObject(allocedChild);
 
        var gc2 = new GarbageCollector(1000, () => [allocedParent]);
        gc2.Collect();
    }

    [Fact]
    public void Collect_MarksReachableThroughArray()
    {
        var gc = new GarbageCollector(1000, () => []);
        var obj = gc.Alloc("Item");
        
        var root = gc.Alloc("Root");
        root.Fields["items"] = Value.FromArray([Value.FromObject(obj)]);
        
        var gc2 = new GarbageCollector(1000, () => [root]);
        var r2 = gc2.Alloc("Root");
        var o2 = gc.Alloc("Item");
        r2.Fields["items"] = Value.FromArray([Value.FromObject(o2)]);
        gc2.Collect();
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
    public void MultipleCollects_AccumulateTotalCollected()
    {
        var gc = MakeGc(roots: () => []);
        gc.Alloc("A"); gc.Alloc("B");
        gc.Collect();
 
        gc.Alloc("C");
        gc.Collect();
 
        Assert.Equal(3, gc.TotalCollected);
    }
}