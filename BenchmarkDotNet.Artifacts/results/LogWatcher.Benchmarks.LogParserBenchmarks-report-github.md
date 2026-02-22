```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26100.7840)
12th Gen Intel Core i7-12700K, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2


```
| Method                            | Mean          | Error        | StdDev       | Gen0   | Allocated |
|---------------------------------- |--------------:|-------------:|-------------:|-------:|----------:|
| TryParse_ValidLine_WithLatency    |     559.89 ns |     3.616 ns |     3.205 ns | 0.0057 |      80 B |
| TryParse_ValidLine_WithoutLatency |     518.18 ns |     1.980 ns |     1.852 ns | 0.0057 |      80 B |
| TryParse_MalformedTimestamp       |      56.99 ns |     0.131 ns |     0.116 ns | 0.0042 |      56 B |
| TryParse_1000ValidLines           | 538,688.27 ns | 4,011.031 ns | 3,555.674 ns | 5.8594 |   80000 B |
