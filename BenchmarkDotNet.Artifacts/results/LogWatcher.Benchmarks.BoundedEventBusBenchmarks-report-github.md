```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26100.7840)
12th Gen Intel Core i7-12700K, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2
  Job-HUDYDY : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2


```
| Method                 | Job        | InvocationCount | UnrollFactor | Mean      | Error     | StdDev     | Allocated |
|----------------------- |----------- |---------------- |------------- |----------:|----------:|-----------:|----------:|
| TryDequeue_WithItem    | DefaultJob | Default         | 16           |  70.13 ns |  0.436 ns |   0.408 ns |         - |
| Publish_BusHasCapacity | Job-HUDYDY | 1               | 1            | 777.32 ns | 65.344 ns | 189.575 ns |         - |
| Publish_BusFull        | Job-HUDYDY | 1               | 1            | 313.00 ns | 32.571 ns |  96.038 ns |         - |
