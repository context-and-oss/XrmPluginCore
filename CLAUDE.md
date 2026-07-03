# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

XrmPluginCore is a NuGet library that provides base functionality for developing plugins and custom APIs in Microsoft Dynamics 365/Dataverse. It streamlines plugin development through dependency injection, context wrappers, and automatic registration utilities.

The project consists of:
- **XrmPluginCore**: Main implementation library
- **XrmPluginCore.Abstractions**: Interfaces and enums used for plugin/custom API registration
- **XrmPluginCore.SourceGenerator**: Compile-time source generator for type-safe filtered attributes
- **XrmPluginCore.Tests**: Unit and integration tests

## Build & Test Commands

```bash
# Restore dependencies
dotnet restore

# Build the solution (Release configuration)
dotnet build --configuration Release --no-restore

# Run all tests
dotnet test --configuration Release --no-build --verbosity normal

# Run tests for a specific framework
dotnet test --configuration Release --framework net8.0

# Pack NuGet packages locally
./scripts/Pack-Local.ps1
# OR
dotnet pack --configuration Release --no-build --output ./nupkg
```

## Architecture

### Core Plugin Execution Flow

1. **Plugin Base Class** (`XrmPluginCore/Plugin.cs`): All plugins inherit from this class
   - Implements `IPlugin.Execute(IServiceProvider)` from Dynamics SDK
   - Builds a local scoped service provider for each execution using DI
   - Matches incoming context against registered plugin steps/custom APIs
   - Invokes the appropriate registered action

2. **Registration Pattern**: Plugins register their steps in the constructor using fluent builders:
   - `RegisterStep<TEntity, TService>(EventOperation, ExecutionStage, Action<TService>)` - Standard DI-based approach with optional type-safe wrappers
   - `RegisterPluginStep<T>(EventOperation, ExecutionStage, Action<LocalPluginContext>)` - Legacy approach (deprecated)
   - `RegisterAPI<TService>(string name, Action<TService>)` - For Custom APIs

   When `AddImage()`, `WithPreImage()` or `WithPostImage()` are used, the source generator automatically creates wrapper classes that are discovered at runtime by naming convention.

3. **Service Provider Pattern**:
   - `ExtendedServiceProvider` wraps the Dynamics SDK's IServiceProvider
   - `ServiceProviderExtensions.BuildServiceProvider()` creates a scoped DI container per execution
   - Built-in services injected: IPluginExecutionContext, IOrganizationServiceFactory, ITracingService (as ExtendedTracingService), ILogger
   - Type-safe registrations automatically register generated wrapper classes (PreImage, PostImage) directly in DI
   - Custom services registered via `OnBeforeBuildServiceProvider()` override

4. **Configuration Builders**:
   - `PluginStepConfigBuilder<T>`: Fluent API for configuring plugin step metadata (filtered attributes, images, deployment, etc.)
   - `CustomApiConfigBuilder`: Fluent API for configuring Custom API metadata (binding type, allowed custom processing steps, parameters, etc.)
   - These builders produce `IPluginStepConfig` and `ICustomApiConfig` consumed by XrmSync for automatic registration

### Project Structure

**XrmPluginCore/** (Main library)
- `Plugin.cs` - Base class for all plugins/custom APIs
- `LocalPluginContext.cs` - Legacy context wrapper (provides OrganizationService, TracingService, etc.)
- `ExtendedTracingService.cs` - Enhanced ITracingService with helper methods
- `ExtendedServiceProvider.cs` - Wrapper for IServiceProvider with DI support
- `Plugins/` - Plugin step registration infrastructure (PluginStepConfigBuilder, ImageSpecification, etc.)
- `CustomApis/` - Custom API registration infrastructure (CustomApiConfigBuilder, RequestParameter, ResponseProperty)
- `Extensions/` - Extension methods for context, service provider, etc.

**XrmPluginCore.Abstractions/** (Shared contracts)
- `Enums/` - EventOperation, ExecutionStage, ExecutionMode, ImageType, CustomApiParameterType, etc.
- `Interfaces/Plugin/` - IPluginStepConfig, IImageSpecification (used by registration tools)
- `Interfaces/CustomApi/` - ICustomApiConfig, IRequestParameter, IResponseProperty
- `IPluginDefinition.cs` - Interface for retrieving plugin step configurations
- `ICustomApiDefinition.cs` - Interface for retrieving custom API configuration

**XrmPluginCore.SourceGenerator/** (Compile-time code generation)
- `Generators/PluginImageGenerator.cs` - Incremental source generator that scans for Plugin classes
- `Parsers/RegistrationParser.cs` - Extracts metadata from RegisterStep invocations
- `CodeGeneration/WrapperClassGenerator.cs` - Generates type-safe wrapper classes
- `Helpers/SyntaxHelper.cs` - Roslyn syntax tree analysis utilities
- `Models/PluginStepMetadata.cs` - Data models for storing registration metadata

### Type-Safe Images

The source generator provides compile-time type safety for plugin images (PreImage/PostImage) with **compile-time enforcement** that prevents developers from accidentally ignoring registered images.

#### API Design

Use `WithPreImage`/`WithPostImage` (convenience methods for `AddImage`) to register images. The `nameof()` pattern enables the source generator to validate that your handler method signature matches the registered images:

```csharp
// Basic plugin (no images) - use lambda invocation syntax
RegisterStep<Account, AccountService>(
    EventOperation.Update,
    ExecutionStage.PostOperation,
    s => s.DoSomething())
    .AddFilteredAttributes(x => x.Name);

// PreImage only - handler method MUST accept PreImage parameter
// Use nameof() for compile-time safety when images are registered
RegisterStep<Account, AccountService>(
    EventOperation.Update,
    ExecutionStage.PostOperation,
    nameof(AccountService.HandleUpdate))
    .AddFilteredAttributes(x => x.Name, x => x.AccountNumber)
    .WithPreImage(x => x.Name, x => x.Revenue);

// PostImage only - handler method MUST accept PostImage parameter
RegisterStep<Account, AccountService>(
    EventOperation.Update,
    ExecutionStage.PostOperation,
    nameof(AccountService.HandleUpdate))
    .AddFilteredAttributes(x => x.Name)
    .WithPostImage(x => x.Name, x => x.AccountNumber);

// Both images - handler method MUST accept both parameters
RegisterStep<Account, AccountService>(
    EventOperation.Update,
    ExecutionStage.PostOperation,
    nameof(AccountService.HandleUpdate))
    .AddFilteredAttributes(x => x.Name, x => x.AccountNumber)
    .WithPreImage(x => x.Name, x => x.Revenue)
    .WithPostImage(x => x.Name, x => x.AccountNumber);
```

**Key benefit**: The source generator emits diagnostics if your handler method signature does not match the registered images. This prevents developers from accidentally ignoring registered images.

#### How It Works

1. **Compile-Time Analysis**: The source generator scans all classes that inherit from `Plugin` and finds `RegisterStep` calls that use `WithPreImage()`, `WithPostImage()`, or `AddImage()`.

2. **Metadata Extraction**: For each registration, it extracts:
   - Plugin class name
   - Entity type (`TEntity`)
   - Event operation and execution stage
   - Filtered attributes from `AddFilteredAttributes()` calls
   - Pre/Post image attributes from `WithPreImage()`/`WithPostImage()`/`AddImage()` calls
   - Method reference from the action delegate

3. **Code Generation**: Generates wrapper classes in the **plugin's own namespace**, named after the registration:
   - Namespace: `{PluginNamespace}` (same namespace as the plugin class)
   - Classes: `{PluginClass}{Entity}{Operation}{Stage}PreImage` / `...PostImage` / `...ActionWrapper` (the registration-derived prefix is sanitized into a valid identifier). Uniqueness within the namespace is guaranteed by XPC2002 (one registration per entity/operation/stage per plugin) plus distinct plugin class names — so no per-registration namespace or `using` alias is needed.

4. **Signature Validation**: The source generator validates that the handler method signature matches the registered images and emits compile-time diagnostics if there is a mismatch.

5. **Runtime Execution**: When the plugin executes:
   - Images are constructed from the execution context
   - The handler method is invoked with strongly-typed image wrappers as parameters

#### Example Usage

The generated image types live in the plugin's own namespace, so when the handler is in that same
namespace no `using` is needed. (When the service lives elsewhere, reference the types by their
fully-qualified name — the code fix emits that form for you.)

```csharp
public class AccountPlugin : Plugin
{
    public AccountPlugin()
    {
        // Type-safe API with compile-time enforcement via nameof()
        RegisterStep<Account, AccountService>(
            EventOperation.Update,
            ExecutionStage.PostOperation,
            nameof(AccountService.HandleUpdate))
            .AddFilteredAttributes(x => x.Name, x => x.AccountNumber)
            .WithPreImage(x => x.Name, x => x.Revenue)
            .WithPostImage(x => x.Name, x => x.AccountNumber);
    }

    protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
    {
        return services.AddScoped<AccountService>();
    }
}

public class AccountService
{
    // Handler signature MUST match registered images (enforced by source generator diagnostics).
    // The image types are named after the registration: {PluginClass}{Entity}{Operation}{Stage}{Kind}.
    public void HandleUpdate(
        AccountPluginAccountUpdatePostOperationPreImage preImage,
        AccountPluginAccountUpdatePostOperationPostImage postImage)
    {
        var previousName = preImage.Name;     // Type-safe, IntelliSense works
        var previousRevenue = preImage.Revenue;
        var newName = postImage.Name;
    }
}
```

#### Generated Code Example

The source generator creates wrapper classes in the plugin's own namespace, named after the
registration. Each wrapper holds the strongly-typed entity and implements the shared image interfaces
(see [Image Interfaces](#image-interfaces) below):

```csharp
// Generated in the plugin's namespace, named {PluginClass}{Entity}{Operation}{Stage}{Kind}
namespace YourNamespace
{
    public sealed class AccountPluginAccountUpdatePostOperationPreImage : IPluginPreImage<YourNamespace.Account>
    {
        public AccountPluginAccountUpdatePostOperationPreImage(Entity entity)
        {
            Entity = entity.ToEntity<YourNamespace.Account>();
        }

        public YourNamespace.Account Entity { get; }

        Microsoft.Xrm.Sdk.Entity IPluginImage.Entity => this.Entity;

        public System.Guid Id => Entity.Id;
        public string LogicalName => Entity.LogicalName;

        public string Name => Entity.Name;
        public decimal? Revenue => Entity.Revenue;
    }

    public sealed class AccountPluginAccountUpdatePostOperationPostImage : IPluginPostImage<YourNamespace.Account>
    {
        public AccountPluginAccountUpdatePostOperationPostImage(Entity entity)
        {
            Entity = entity.ToEntity<YourNamespace.Account>();
        }

        public YourNamespace.Account Entity { get; }

        Microsoft.Xrm.Sdk.Entity IPluginImage.Entity => this.Entity;

        public System.Guid Id => Entity.Id;
        public string LogicalName => Entity.LogicalName;

        public string Name => Entity.Name;
        public string AccountNumber => Entity.AccountNumber;
    }
}
```

#### Image Interfaces

Every generated image implements a small interface hierarchy declared in `XrmPluginCore`:

| Interface | `Entity` property type | Purpose |
| --- | --- | --- |
| `IPluginImage` | `Microsoft.Xrm.Sdk.Entity` | Non-generic base; lowest common denominator for fully generic helpers |
| `IPluginImage<TEntity>` | `TEntity` (early-bound) | Type-safe access to the entity |
| `IPluginPreImage` / `IPluginPreImage<TEntity>` | (inherited) | Identifies a pre-image |
| `IPluginPostImage` / `IPluginPostImage<TEntity>` | (inherited) | Identifies a post-image |

The non-generic `IPluginImage` also exposes the members that are always available on any entity image, regardless of which attributes were registered: `Id` (`Guid`, the primary key) and `LogicalName` (`string`).

Each registration generates its **own** concrete `...PreImage`/`...PostImage` type. Prefer those
concrete types — they expose the strongly-typed, per-attribute properties that are the whole point of
the feature. The interfaces are an **escape hatch** for the narrow case where you only need the raw
`Entity` (or genuinely shared, attribute-agnostic logic across registrations); declaring a parameter
as an interface throws away the generated typed properties. A handler method may declare its
parameters using any of the matching interfaces instead of the concrete generated type — the source
generator accepts them and validates the kind (pre vs post) and, for the generic variants, the entity
type:

```csharp
public class AccountService
{
    // Concrete generated types (preferred — full typed property access)
    public void HandleUpdate(
        AccountPluginAccountUpdatePostOperationPreImage pre,
        AccountPluginAccountUpdatePostOperationPostImage post) { }
}

public static class AuditHelper
{
    // Works for the Pre or Post image of ANY registration on Account
    public static void Log(IPluginImage<Account> image) { /* image.Entity is an Account */ }

    // Works for any image of any entity
    public static void LogRaw(IPluginImage image) { /* image.Entity is an Entity */ }
}
```

#### Image Registration Methods

The following methods are available for registering images:

- `WithPreImage(params Expression<Func<TEntity, object>>[] attributes)` - Convenience method to register a PreImage with selected attributes
- `WithPostImage(params Expression<Func<TEntity, object>>[] attributes)` - Convenience method to register a PostImage with selected attributes
- `AddImage(ImageType imageType, params Expression<Func<TEntity, object>>[] attributes)` - General method to register any image type

All three methods are valid and supported. `WithPreImage` and `WithPostImage` are convenience wrappers around `AddImage`.

#### Benefits

- **Compile-time enforcement**: Source generator diagnostics ensure handler signature matches registered images
- **Type safety**: Wrong image types cause compile errors
- **IntelliSense support**: Auto-completion for available image attributes
- **No runtime overhead**: Simple property accessors, no reflection at access time
- **Null safety**: Missing attributes return null instead of throwing exceptions
- **No namespace juggling**: types are emitted into the plugin's own namespace with registration-derived names, so there are no `using` aliases to manage
- **Shared interfaces** (escape hatch): `IPluginImage`/`IPluginPreImage`/`IPluginPostImage` (and generic variants) let handler methods share attribute-agnostic logic across the per-registration concrete image types when you only need the raw `Entity`

### Type-Safe Custom API Request/Response

The source generator provides the same compile-time safety for Custom APIs that it provides for plugin images. The typed overload `RegisterAPI<TService>(string name, string handlerMethodName)` opts in: from the `AddRequestParameter`/`AddResponseProperty` declarations, the generator emits a `Request` and `Response` class **named after the API and placed in the plugin's own namespace**, plus an internal `ActionWrapper` discovered at runtime by naming convention.

#### API Design

```csharp
public class SomeCustomApi : Plugin
{
    public SomeCustomApi()
    {
        RegisterAPI<CallbackService>(nameof(SomeCustomApi), nameof(CallbackService.SomeCustomApiMethod))
            .AddRequestParameter("EntityLogicalName", CustomApiParameterType.String)
            .AddRequestParameter("EntityId", CustomApiParameterType.Guid)
            .AddResponseProperty("StatusCode", CustomApiParameterType.Integer)
            .AddResponseProperty("ErrorMessage", CustomApiParameterType.String);
    }

    protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
        => services.AddScoped<CallbackService>();
}

public class CallbackService
{
    // Signature is enforced by the source generator (XPC4004/XPC4005/XPC4006)
    public SomeCustomApiResponse SomeCustomApiMethod(SomeCustomApiRequest request)
    {
        var id = request.EntityId;           // strongly-typed, from InputParameters["EntityId"]
        return new SomeCustomApiResponse(200, string.Empty);
    }
}
```

#### How It Works

1. **Property names**: each request/response property is named after the *constant value* of the unique-name argument (so `AddRequestParameter("EntityId", ...)` and `AddRequestParameter(CallbackService.EntityId, ...)` both yield an `EntityId` property). The InputParameters/OutputParameters dictionary keys use that same unique name.
2. **Types**: `CustomApiParameterType` is mapped to a CLR type (e.g. `String` → `string`, `Guid` → `System.Guid`, `Integer` → `int`, `Money` → `Microsoft.Xrm.Sdk.Money`). Optional value-type request parameters become nullable (`int?`).
3. **Response shape**: the generated `Response` has settable properties **and** an all-args constructor, so it can be built with `new XResponse(200, "")` or an object initializer.
4. **Signature adaptation**: when no request parameters are declared the handler takes no argument; when no response properties are declared it returns `void`.
5. **Runtime execution**: the generated `ActionWrapper` reads `IPluginExecutionContext.InputParameters` into the request, invokes the handler, and writes the returned response's properties into `OutputParameters`.

#### Diagnostics

| Rule | Severity | Meaning |
| --- | --- | --- |
| XPC3006 | Warning | Custom API name must be a compile-time constant (`nameof`/`const`/literal) for generation |
| XPC4004 | Error | Custom API handler method not found on the service type (code fix creates it) |
| XPC4005 | Warning | Handler signature doesn't match the declared parameters, generated types don't exist yet |
| XPC4006 | Error | Handler signature doesn't match, generated types exist (code fix corrects it) |
| XPC3007 | Info | A Custom API declares request/response parameters but uses the action-based overload; prefer the typed `RegisterAPI<TService>(name, nameof(TService.Method))` overload that generates DTOs (code fix converts the lambda to `nameof`) |
| XPC3001 | Warning | Prefer `nameof(TService.Method)` over a string literal for the handler argument |

### Dependency Injection

XrmPluginCore supports three patterns for registering custom services:

#### Pattern 1: Direct Override (Simple, Single Plugin)

Override `OnBeforeBuildServiceProvider()` directly in your plugin class:

```csharp
public class MyPlugin : Plugin
{
    protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
    {
        return services.AddScoped<IMyService, MyService>();
    }
}
```

**Use when**: You have a single plugin class with unique services.

#### Pattern 2: Base Class (Inheritance-based Sharing)

Create a base plugin class that registers shared services, then inherit from it:

```csharp
public class BasePlugin : Plugin
{
    protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
    {
        return services
            .AddScoped<ISharedService, SharedService>()
            .AddScoped<ILogger, Logger>();
    }
}

public class AccountPlugin : BasePlugin { }
public class ContactPlugin : BasePlugin { }
```

**Use when**: Multiple plugins need the same services and share a common inheritance hierarchy.

#### Pattern 3: Extension Method (Composition-based Sharing)

Create static extension methods to encapsulate service registration logic:

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSharedServices(this IServiceCollection services)
    {
        return services
            .AddScoped<ISharedService, SharedService>()
            .AddScoped<ILogger, Logger>();
    }
}

public class AccountPlugin : Plugin
{
    protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
    {
        return services.AddSharedServices();
    }
}
```

**Use when**: You want to share service registration logic across plugins that may not share inheritance, or when you need to compose multiple service registration modules.

**Note**: Services are scoped to the plugin execution and disposed automatically.

### Multi-Targeting

The library targets both .NET Framework 4.6.2 and .NET 8 to support:
- Traditional on-premise Dynamics 365 deployments (net462)
- Modern Dataverse environments (net8)

Different SDK packages are used per framework:
- net462: `Microsoft.CrmSdk.CoreAssemblies` 9.0.2.59
- net8: `Microsoft.PowerPlatform.Dataverse.Client` 1.2.3

## Key Patterns

### Event Operation Registration

Previously used `EventOperation` enum, but now accepts strings to support custom messages:
```csharp
// Standard operation using enum
RegisterStep<Account, IMyService>(EventOperation.Update, ExecutionStage.PostOperation, s => s.DoSomething())

// Custom message using string
RegisterStep<MyEntity>("custom_CustomMessage", ExecutionStage.PostOperation, s => s.DoSomething())
```

### Plugin Step Images

Images are configured through the builder using `WithPreImage`, `WithPostImage`, or `AddImage`. Use `nameof()` for compile-time safety when registering images:
```csharp
// Using convenience methods (recommended)
RegisterStep<Account, IAccountService>(
    EventOperation.Update,
    ExecutionStage.PostOperation,
    nameof(IAccountService.HandleUpdate))
    .AddFilteredAttributes(x => x.Name, x => x.AccountNumber)
    .WithPreImage(x => x.Name, x => x.Revenue)
    .WithPostImage(x => x.Name, x => x.AccountNumber);

// Using AddImage directly
RegisterStep<Account, IAccountService>(
    EventOperation.Update,
    ExecutionStage.PostOperation,
    nameof(IAccountService.HandleUpdate))
    .AddFilteredAttributes(x => x.Name, x => x.AccountNumber)
    .AddImage(ImageType.PreImage, x => x.Name, x => x.Revenue)
    .AddImage(ImageType.PostImage, x => x.Name, x => x.AccountNumber);
```

### Custom APIs

Custom APIs use a single registration per class:
```csharp
public class MyCustomApi : BasePlugin
{
    public MyCustomApi()
    {
        RegisterAPI<IMyService>("custom_MyApiName", service => service.Execute())
            .WithBindingType(BindingType.Entity)
            .WithBoundEntityLogicalName("account")
            .AddRequestParameter("InputParam", CustomApiParameterType.String, isOptional: false)
            .AddResponseProperty("OutputValue", CustomApiParameterType.Integer);
    }
}
```

## Deployment

Plugins are deployed to Dynamics 365 as ILMerged assemblies containing all dependencies. Use ILRepack to merge:
- XrmPluginCore.dll
- XrmPluginCore.Abstractions.dll
- Microsoft.Extensions.DependencyInjection.dll
- Microsoft.Extensions.DependencyInjection.Abstractions.dll
- Microsoft.Bcl.AsyncInterfaces.dll

Registration is automated using XrmSync (https://github.com/delegateas/XrmSync), which reads the IPluginDefinition and ICustomApiDefinition interfaces.

## Versioning

Version numbers are managed through CHANGELOG.md files:
- `XrmPluginCore/CHANGELOG.md` for the main library and source generator (both use the same version)
- `XrmPluginCore.Abstractions/CHANGELOG.md` for abstractions

`Directory.Build.targets` derives each project's `<Version>` from the latest `### v...` entry in the relevant CHANGELOG.md at build time — no script or CI step required. Core and the source generator share `XrmPluginCore/CHANGELOG.md`; Abstractions overrides `ChangelogPath` in its .csproj to use its own changelog. An explicit `-p:Version=...` still overrides the derived value.

The pending (not-yet-released) entry is marked `### vX.Y.Z - Unreleased` instead of a date. Only the version number is parsed (the ` - Unreleased`/date suffix is ignored), so this doesn't affect the derived `<Version>`. Add new changes to that existing `- Unreleased` entry rather than creating a new heading; when the version is released, replace `Unreleased` with the release date.

Analyzer rules are tracked separately in `XrmPluginCore.SourceGenerator/AnalyzerReleases.Unshipped.md`; new rules go there and are moved to `AnalyzerReleases.Shipped.md` (under a `## Release X.Y.Z` heading) when that version is released.

## Source Generator Development

When developing or debugging the source generator, use the following workflow to test changes against a local project:

### Development Loop

1. **Update CHANGELOG version** - Add a preview version to:
   - `XrmPluginCore/CHANGELOG.md`

   Example: `### v1.2.3-preview.1 - 28 November 2025`

2. **Pack the local NuGet package**:
   ```bash
   ./scripts/Pack-Local.ps1
   ```
   This builds Release configuration and creates packages in `XrmPluginCore/bin/Release/`.

3. **Configure local NuGet source** in the test project's solution (e.g., XrmSync):

   Create or update `NuGet.Config` at the solution root:
   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
     <packageSources>
       <!-- Replace with the absolute path to your local XrmPluginCore repository -->
       <add key="Local-XrmPluginCore" value="C:\path\to\XrmPluginCore\XrmPluginCore\bin\Release" />
       <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
     </packageSources>
   </configuration>
   ```

4. **Update project reference** to the preview version in the test project's .csproj:
   ```xml
   <PackageReference Include="XrmPluginCore" Version="1.2.3-preview.1" />
   ```

5. **Restore and build**:
   ```bash
   dotnet restore --force && dotnet build
   ```

### Inspecting Generated Output

To see what the source generator produces, add these properties to the test project's .csproj:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Generated files appear in `obj/Generated/XrmPluginCore.SourceGenerator/`.

### Important Notes

- **Increment preview version** for each iteration (e.g., preview.1 → preview.2) to ensure NuGet picks up the new package
- **Clear NuGet cache** if issues persist: `dotnet nuget locals all --clear`
- **Visual Studio cache**: If VS shows stale analyzer output, close VS and delete `%LOCALAPPDATA%\Microsoft\VisualStudio\{version}\ComponentModelCache`

## Analyzer Rules
- Headers for violation examples for analyzer rules should be prefixed with ❌
- Headers for violation fixes for analyzer rules should be prefixed with ✅
