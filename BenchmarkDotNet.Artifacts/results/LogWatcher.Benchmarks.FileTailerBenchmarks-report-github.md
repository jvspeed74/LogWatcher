```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26100.7840)
12th Gen Intel Core i7-12700K, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2


```
| Method                   | Mean     | Error    | StdDev   | Allocated |
|------------------------- |---------:|---------:|---------:|----------:|
| ReadAppended_1MB_NewData | 74.25 μs | 1.414 μs | 1.389 μs |     240 B |
| ReadAppended_NoNewData   | 16.28 μs | 0.319 μs | 0.341 μs |     240 B |
