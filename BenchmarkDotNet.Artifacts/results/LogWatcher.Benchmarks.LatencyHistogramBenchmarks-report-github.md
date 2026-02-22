```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26100.7840)
12th Gen Intel Core i7-12700K, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2
  Job-HUDYDY : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2


```
| Method                  | Job        | InvocationCount | UnrollFactor | Mean           | Error       | StdDev        | Median         | Allocated |
|------------------------ |----------- |---------------- |------------- |---------------:|------------:|--------------:|---------------:|----------:|
| Add_SingleSample        | DefaultJob | Default         | 16           |      0.0037 ns |   0.0056 ns |     0.0052 ns |      0.0006 ns |         - |
| Add_1000Samples         | DefaultJob | Default         | 16           |  1,087.3760 ns |   5.0709 ns |     4.7433 ns |  1,087.3501 ns |         - |
| Percentile_P99          | DefaultJob | Default         | 16           |  3,122.1294 ns |   8.0179 ns |     7.4999 ns |  3,122.8535 ns |         - |
| MergeFrom_FullHistogram | Job-HUDYDY | 1               | 1            | 10,279.5918 ns | 408.5663 ns | 1,191.8070 ns | 10,100.0000 ns |         - |
