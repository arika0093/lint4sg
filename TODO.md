# TODO

## Planned Rules

### LSG017: Pipeline callbacks must be static

- Target pipeline callbacks such as `Select`, `SelectMany`, `Where`, `Combine`, `CreateSyntaxProvider`, `ForAttributeWithMetadataName`, and `RegisterSourceOutput`.
- Report when a lambda or anonymous method can be `static` but is not.
- Primary goal: prevent accidental captures in source-generator hot paths.

### LSG018: Prefer SelectMany over materialized collections in the pipeline

- Report pipeline stages that materialize collections such as arrays, `List<T>`, or `ImmutableArray<T>` only to flatten or iterate them in the next stage.
- Prefer keeping item granularity with `SelectMany` instead of carrying materialized collections through the pipeline.
- This rule is about avoiding unnecessary materialization in the pipeline.
- This is distinct from the tuple-readability concern below.

### LSG019: Delay Collect

- Report cases where `.Collect()` happens earlier than necessary, before the pipeline has been sufficiently filtered or projected.
- Prefer `Where` / `Select` / `SelectMany` first, and call `.Collect()` only near the point where whole-set aggregation is actually needed.
- Open concern: detection may be unreliable when the relevant pipeline stages are split across multiple provider locals. The analyzer design should account for chained and non-chained provider construction before deciding whether this rule is feasible as a precise diagnostic.

### LSG020: Nested tuple proliferation in pipeline composition

- Source generators often overuse tuples during `Combine` chains, which quickly harms readability.
- Error on heavily nested tuple shapes such as `((x.Left.Left, x.Left.Right), x.Right)` and similar patterns that require repeated `.Left` / `.Right` navigation.
- Prefer merging intermediate values into a named type or otherwise flattening the model at each stage.
- This is separate from `LSG018`; the intent here is readability and maintainability, not collection materialization.