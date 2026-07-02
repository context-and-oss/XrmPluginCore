## Release 1.4.1

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
XPC3006 | XrmPluginCore.SourceGenerator | Warning  | XPC3006 Custom API name must be a compile-time constant
XPC4004 | XrmPluginCore.SourceGenerator | Error    | XPC4004 Custom API handler method not found
XPC4005 | XrmPluginCore.SourceGenerator | Warning  | XPC4005 Custom API handler signature mismatch (generated types don't exist)
XPC4006 | XrmPluginCore.SourceGenerator | Error    | XPC4006 Custom API handler signature mismatch (generated types exist)

## Release 1.2.7

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
XPC3004 | XrmPluginCore.SourceGenerator | Error    | XPC3004 Do not use LocalPluginContext as TService in RegisterStep
XPC3005 | XrmPluginCore.SourceGenerator | Warning  | XPC3005 Full entity image registration without specifying attributes

## Release 1.2.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
XPC1000 | XrmPluginCore.SourceGenerator | Info     | XPC1000 Generated type-safe wrapper classes
XPC3001 | XrmPluginCore.SourceGenerator | Warning  | XPC3001 Prefer nameof over string literal for handler method
XPC4000 | XrmPluginCore.SourceGenerator | Warning  | XPC4000 Failed to resolve symbol
XPC4001 | XrmPluginCore.SourceGenerator | Warning  | XPC4001 No parameterless constructor found
XPC4002 | XrmPluginCore.SourceGenerator | Error    | XPC4002 Handler method not found
XPC4003 | XrmPluginCore.SourceGenerator | Error    | XPC4003 Handler signature does not match registered images
XPC4004 | XrmPluginCore.SourceGenerator | Warning  | XPC4004 Image registration without method reference
XPC5000 | XrmPluginCore.SourceGenerator | Error    | XPC5000 Failed to generate wrapper classes

## Release 1.2.1

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
XPC4005 | XrmPluginCore.SourceGenerator | Info     | XPC4005 Consider using modern image registration API

## Release 1.2.2

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
XPC4006 | XrmPluginCore.SourceGenerator | Error    | XPC4006 Handler signature mismatch (generated types exist)

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------
XPC4003 | XrmPluginCore.SourceGenerator | Warning | XrmPluginCore.SourceGenerator | Error | Handler signature does not match registered images (generated types don't exist)

## Release 1.2.3

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
XPC1001 | XrmPluginCore.SourceGenerator | Info | Generated type-safe wrapper classes (replaces XPC1000)
XPC2001 | XrmPluginCore.SourceGenerator | Warning | No parameterless constructor found (renumbered from XPC4001)
XPC3002 | XrmPluginCore.SourceGenerator | Info | Consider using modern image registration API (renumbered from XPC4005)
XPC3003 | XrmPluginCore.SourceGenerator | Warning | Image registration without method reference (renumbered from XPC4004)
XPC4001 | XrmPluginCore.SourceGenerator | Error | Handler method not found (renumbered from XPC4002)
XPC4002 | XrmPluginCore.SourceGenerator | Warning | Handler signature mismatch - types don't exist (renumbered from XPC4003)
XPC4003 | XrmPluginCore.SourceGenerator | Error | Handler signature mismatch - types exist (renumbered from XPC4006)
XPC5001 | XrmPluginCore.SourceGenerator | Warning | Failed to resolve symbol (renumbered from XPC4000)
XPC5002 | XrmPluginCore.SourceGenerator | Error | Failed to generate wrapper classes (renumbered from XPC5000)

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
XPC1000 | XrmPluginCore.SourceGenerator | Info | Replaced by XPC1001
XPC4000 | XrmPluginCore.SourceGenerator | Warning | Replaced by XPC5001
XPC4004 | XrmPluginCore.SourceGenerator | Warning | Replaced by XPC3003
XPC4005 | XrmPluginCore.SourceGenerator | Info | Replaced by XPC3002
XPC4006 | XrmPluginCore.SourceGenerator | Error | Replaced by XPC4003
XPC5000 | XrmPluginCore.SourceGenerator | Error | Replaced by XPC5002
