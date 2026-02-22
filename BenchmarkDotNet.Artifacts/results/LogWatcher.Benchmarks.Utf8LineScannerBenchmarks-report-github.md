```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26100.7840)
12th Gen Intel Core i7-12700K, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2


```
| Method                        | Mean     | Error    | StdDev   | Allocated |
|------------------------------ |---------:|---------:|---------:|----------:|
| Scan_1000CompleteLinesNoCarry | 16.54 μs | 0.177 μs | 0.166 μs |         - |
| Scan_1000LinesWithSplitCarry  | 16.29 μs | 0.150 μs | 0.133 μs |     280 B |
