
## Component 4: Top-K computation (exact counts, computed at report time)

### Purpose

Compute **top K message keys** from merged message-count dictionary (exact counts). This is only done at report time (every 2 seconds), so correctness and determinism matter more than micro-optimization.

### Spec details to implement

* Inputs: merged `Dictionary<string, int>` of message key → count
* K: configurable (default 10)
* Ordering:

    * primary: count descending
    * tie-break: key ascending (ordinal)

### Public contract

Create a static helper:

* `static IReadOnlyList<(string Key, int Count)> ComputeTopK(Dictionary<string,int> counts, int k)`

### Implementation steps

1. **Validate inputs**

    * If `k <= 0` => return empty list
    * If counts empty => return empty list
2. **Create a list of entries**

    * Iterate dictionary and push entries into a `List<(string Key, int Count)>`.
3. **Sort**

    * Use `List<T>.Sort` with comparer:

        * compare Count descending
        * if equal, compare Key using `StringComparer.Ordinal`
4. **Take first K**

    * If list.Count > k => return `list.GetRange(0, k)`
    * Else return the full list
5. (Optional) **Avoid allocating huge lists**

    * Not necessary for weekend scope; sort is fine. If dictionary becomes enormous, replace with a min-heap of size K later.

### Unit tests

Create `TopKTests`:

1. `Empty_ReturnsEmpty`
2. `KGreaterThanCount_ReturnsAllSorted`
3. `TieBreak_IsOrdinalAscending`
4. `OrdersByCountDescending`

