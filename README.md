SmallBlockMemoryStream
======================
This assembly exposes a single class, `SmallBlockMemoryStream`, that is intended to be a drop-in replacement for the BCL [`MemoryStream`](http://msdn.microsoft.com/en-us/library/system.io.memorystream.aspx) class. The need for this class came to light at [Dow Jones](http://dowjones.com) while performance tuning high-capacity, high-availablity market data services for [MarketWatch](http://marketwatch.com), [The Wall Street Journal](http://online.wsj.com) and [Barron's](http://online.barrons.com). These services often return very large response messages, and preparing those messages was producing memory allocations on the [Large Object Heap](http://msdn.microsoft.com/en-us/magazine/cc534993.aspx) (LOH). When the LOH was eventually compacted, our services would pause for several seconds, and that would lead to mayhem in the data center (several seconds is an eternity to a high-capacity system).

We built the first version of this class to allow us to return large messages from WebApi actions without invoking the LOH. Along with configuration settings for the legacy WCF services, we were able eliminate the LOH from the picture entirely.

Usage
---
Install from [Nuget](https://www.nuget.org/packages/SmallBlockMemoryStream/) with the console command `Install-Package SmallBlockMemoryStream`.

For the most part, use the class just like a `MemoryStream`. It only has two constructors:
```cs
using Aethon.IO;

public class MyService()
{
  public Stream GetData()
  {
    var result = new SmallBlockMemoryStream(); // start with an empty, zero-capacity stream
    // ...write, read, position, etc...
    return result;
  }
  
  public Stream GetData(long howMuch)
  {
    var result = new SmallBlockMemoryStream(howMuch); // preallocate room for the stream
    // ...write, read, position, etc...
    return result;
  }
}
```

When To Use
---
The `SmallBlockMemoryStream` does not inherit from `MemoryStream` because:
  - there was little functionality that could be shared,
  - it would have increased the size of the implementation to carry it along as a base class, and
  - there are a few methods on `MemoryStream` that do not make sense in this context (`GetBuffer` and `ToArray`)
  
And that brings up a good point about these two classes: `SmallBlockMemoryStream` is not intended as a general replacement for `MemoryStream`. It is intended as a replacement when certain performance features are desired. In many cases, `MemoryStream` will be more performant than `SmallBlockMemoryStream`:

If|Then
-------------------|-------------------------
You know the final length of the stream and it will be <85K| `new MemoryStream(knownCapacity)`
You will need the contents of the stream as an array | `new MemoryStream()`
You know the final length of the stream and it will be >85K| `new SmallBlockMemoryStream(knownCapacity)`
The stream might end up >85K and you want to avoid the LOH | `new SmallBlockMemoryStream()`
The stream sizes will vary widely | Profile each class in your actual code

Comparison
---
`SmallBlockMemoryStream` is designed and unit tested to be an exact, drop-in analogue to `MemoryStream` except where the LOH is affected. There are a few unavoidable differences to be aware of:

Feature|`SmallBlockMemoryStream`|`MemoryStream`
---|---|---
Read-only|Never|By constructor
Writable|Always|By constructor
Expandable|Always|By constructor
Memory Allocation|Allocates additional buffers as needed|Reallocates a single buffer as needed
Memory Copies|Never copies|Copies on each reallocation
`GetBuffer`|No|Yes
`ToArray`|No|Yes
Retains stream buffer(s) after `Close`|No|Yes


