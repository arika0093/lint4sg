## Release 1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
LSG001 | SourceGenerator | Error | Avoid ISourceGenerator
LSG002 | SourceGenerator | Warning | Prefer ForAttributeWithMetadataName
LSG003 | SourceGenerator | Error | Avoid high-cost SyntaxProvider predicate
LSG004 | SourceGenerator | Error | Forward CancellationToken
LSG005 | SourceGenerator | Error | Missing ThrowIfCancellationRequested in loop
LSG006 | SourceGenerator | Error | Non-deterministic value in RegisterSourceOutput
LSG007 | SourceGenerator | Error | Non-deterministic collection in RegisterSourceOutput
LSG008 | SourceGenerator | Warning | Non-deterministic SyntaxProvider return value
LSG009 | SourceGenerator | Error | Avoid NormalizeWhitespace
LSG010 | SourceGenerator | Error | Excessive whitespace in AppendLine
LSG011 | SourceGenerator | Error | Use raw string literal instead of consecutive AppendLine
LSG012 | SourceGenerator | Warning | External dependency in source generator
LSG013 | SourceGenerator | Warning | Avoid Reflection API in source generator
LSG014 | SourceGenerator | Warning | Microsoft.CodeAnalysis.CSharp version may be too new
LSG101 | Performance | Info | Consider 'in' modifier for struct parameter
LSG102 | Performance | Info | Consider interpolated string instead of string.Format
LSG103 | Performance | Info | Use StringBuilder for string concatenation in loops
