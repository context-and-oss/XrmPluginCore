### v1.4.2 - Unreleased
* Add: Info XPC3007: when a Custom API declares `AddRequestParameter`/`AddResponseProperty` but registers with the action-based `RegisterAPI<TService>(name, Action<TService>)` overload, it now suggests the type-safe `RegisterAPI<TService>(name, nameof(TService.Handler))` overload that generates strongly-typed request/response DTOs (with a code fix that converts `s => s.Handle()` to `nameof(TService.Handle)`).

### v1.4.1 - 01 July 2026
* Add: `WithExecutePrivilege<T>(Privilege)` on the Custom API builder for type-safe execute-privilege configuration, e.g. `.WithExecutePrivilege<Account>(Privilege.Read)` resolves to `prvReadAccount`. Privilege names use the entity schema name, taken from the early-bound type name (`typeof(T).Name`). A `WithExecutePrivilege(string entitySchemaName, Privilege)` overload is available for late-bound scenarios, and the existing `WithExecutePrivilegeName(string)` remains for non-standard privilege names.
* Add: Type-safe Custom API request/response wrappers. `RegisterAPI<TService>(name, handlerMethodName)` now generates `{ApiName}Request`/`{ApiName}Response` classes (named after the API, in the plugin's namespace) from the `AddRequestParameter`/`AddResponseProperty` declarations. The handler accepts the request and returns the response; a generated `ActionWrapper` marshals `InputParameters` into the request and the returned response into `OutputParameters`. When no request parameters are declared the handler takes no argument, and when no response properties are declared it returns `void`.
* Add: Error XPC4004: Custom API handler method not found (with code fix to create the method).
* Add: Warning XPC4005 / Error XPC4006: Custom API handler signature does not match the declared request parameters and response properties (with code fix to correct the signature).
* Add: XPC3001 (Prefer `nameof` over string literal) now also covers the Custom API handler argument.
* Add: Warning XPC3006: the typed `RegisterAPI<TService>(name, handlerMethodName)` overload requires a compile-time constant name (so the generated classes can be named after the API); a non-constant name is reported instead of silently skipping generation.
* Add: The typed `RegisterAPI<TService>(name, handlerMethodName)` overload now throws `ArgumentException` when the name or handler method name is null or whitespace, so misconfigurations fail fast at registration instead of being silently treated as "no registration" at execution time.
* Fix: Generated image properties now mirror the `[Obsolete]` attribute of the underlying entity property, so deprecation warnings (CS0612/CS0618) surface in the calling code instead of inside the auto-generated image class.
* Fix: Generated code (images and Custom API request/response) now only emits nullable reference-type annotations (`string?`) and a `#nullable enable` directive when the consuming project has nullable reference types enabled. On projects without NRT (including .NET Framework / C# 7.3 defaults) the generated code is emitted without those annotations, keeping it compilable and warning-free. Nullable value types (`int?`) are always emitted.

### v1.3.0 - 22 June 2026
* Add: `IPluginImage`, `IPluginImage<TEntity>`, `IPluginPreImage`/`IPluginPreImage<TEntity>` and `IPluginPostImage`/`IPluginPostImage<TEntity>` interfaces for generated images. Handler methods can now accept these interface types so functionality can be shared across the per-registration concrete image types. The generic variants expose a type-safe `Entity` property.
* Add: Generated images (and `IPluginImage`) now always expose the record's `Id` (primary key) and `LogicalName`, since they are available on every entity image.
* Fix: Detect handler methods inherited from base interfaces
* Fix: Always generate aliased usings
* Breaking: Removed `IEntityImageWrapper<T>`; generated images now implement `IPluginPreImage<TEntity>`/`IPluginPostImage<TEntity>` instead. Replace any usage of `IEntityImageWrapper<T>` with `IPluginImage<TEntity>`.

### v1.2.8 - 30 April 2026
* Fix: Set ServiceProvider property on LocalPluginContext
* Fix: XPC3004: Detect and report usage of LocalPluginContext when implicitly passed

### v1.2.7 - 22 April 2026
* Add: Ability to generate Pre and Post images with all attributes
* Add: Error XPC3004: Do not use LocalPluginContext as TService in RegisterStep
* Add: Warning XPC3005: Full entity image registration without specifying attributes

### v1.2.6 - 27 February 2026
* Add: Add using directives for generated image namespaces
* Fix: Handle ambiguous PreImage/PostImage usings with aliases

### v1.2.5 - 22 January 2026
* Fix: Avoid naming collisions on generated types when multiple plugins in the same assembly use Type-Safe Images
* Fix: Generate PreImage/PostImage types, even when handler signature does not match
* Fix: Refactored the image registration analyzer to always report XPC3002 for any AddImage usage, regardless of handler syntax (nameof, method reference, or lambda).
XPC3003 is now only reported for lambda invocation with the modern API.

### v1.2.4 - 3 December 2025
* ADD: Support for setting ExecutePrivilegeName
* Fix: Image wrappers now forward to the underlying strongly-typed entity type instead of the base Entity type

### v1.2.3 - 28 November 2025
* Fix: Generate PreImage/PostImage types even when handler signature doesn't match (fixes chicken-and-egg problem where types couldn't be used until they existed)
* Breaking: Reorganized diagnostic IDs by category (XPC1xxx=Info, XPC2xxx=Plugin structure, XPC3xxx=Style, XPC4xxx=Handler methods, XPC5xxx=Internal errors)

### v1.2.2 - 27 November 2025
* Fix: XPC4003 has been reduced to Warning to allow initial build to succeed
* Add: New rule XPC4006 (Error) to enforce handler signature correctness once generated types exist

### v1.2.1 - 27 November 2025
* Fix: Analyzer for XPC4005 to correctly identify lambda expressions in AddImage calls

### v1.2.0 - 27 November 2025
* Add: Type-Safe Images feature with compile-time enforcement via source generator
* Add: Source analyzer rules with hotfixes and documentation to help use the Type-Safe Images feature correctly

### v1.1.1 - 14 November 2025
* Add: IManagedIdentityService to service provider (#1)

### v1.1.0 - 8 October 2025
* Breaking: Change Plugin Step Configs to not use the EventOperation enum but instead use a string to allow for custom messages. RegisterStep supports both enum and string for ease of use.
* Fix: Remove the MessageEntity type since it isn't needed and muddies the waters

### v1.0.1 - 2 October 2025
* Refactor: Merge CustomAPI into Plugin base class for simplicity

### v1.0.0 - 30 September 2025
* Add: Support for XrmBedrock style plugins by wrapping the LocalPluginContext in IServiceProvider
* Add: IPluginExecutionContext extension methods to simplify getting the target and image data
* Add: Ability to overload the IServiceProvider before it is used to create the LocalPluginContext and local IServiceProvider
* Refactor: Update from .NET6 to .NET8 build target
* Refactor: Modifications to CustomAPI definitions to align better with data
* Refactor: Remove Delegate branding after company rebrand to Context&

### v0.0.7 - 5 May 2025
* Add icon to package
* Sign the assembly

### v0.0.6 - 1 May 2025
* Fix: Mark base classes (CustomApi and Plugin) as abstract

### v0.0.5 - 29 April 2025
* Add AsyncAutoDelete field to PluginConfig

### v0.0.4 - 29 April 2025
* Fix: String formatting had invalid indexes

### v0.0.3 - 29 April 2025
* Clean-up and move concrete classes to XrmPluginCore from XrmPluginCore.Abstractions

### v0.0.2 - 25 April 2025
* Fixes to project file so version and dependencies are picked up correctly

### v0.0.1 - 14 March 2025
* Initial release of XrmPluginCore.
