using Microsoft.CodeAnalysis;

namespace XrmPluginCore.SourceGenerator;

/// <summary>
/// Diagnostic descriptors for the source generator.
/// Categories:
/// - XPC1xxx: Informational messages (generation status)
/// - XPC2xxx: Plugin class structure issues (constructor, inheritance)
/// - XPC3xxx: Code style and best practices (nameof, modern API)
/// - XPC4xxx: Handler method issues (not found, signature mismatch)
/// - XPC5xxx: Internal errors (generation failures, symbol resolution)
/// </summary>
public static class DiagnosticDescriptors
{
	private const string Category = "XrmPluginCore.SourceGenerator";
	private const string HelpLinkBaseUri = "https://github.com/delegateas/XrmPluginCore/blob/main/XrmPluginCore.SourceGenerator/rules";

	public static readonly DiagnosticDescriptor GenerationSuccess = new(
		id: "XPC1001",
		title: "Generated type-safe wrapper classes",
		messageFormat: "Generated {0} wrapper class(es) for {1}",
		category: Category,
		DiagnosticSeverity.Info,
		isEnabledByDefault: true);

	public static readonly DiagnosticDescriptor NoParameterlessConstructor = new(
		id: "XPC2001",
		title: "No parameterless constructor found",
		messageFormat: "Plugin class '{0}' has no parameterless constructor. Image wrappers will not be generated for this plugin.",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		helpLinkUri: $"{HelpLinkBaseUri}/XPC2001.md");

	public static readonly DiagnosticDescriptor DuplicateStepRegistration = new(
		id: "XPC2002",
		title: "Duplicate plugin step registration",
		messageFormat: "Plugin '{0}' registers {1}/{2}/{3} more than once. Each entity/operation/stage combination may be registered at most once per plugin.",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: "A plugin may register a given entity/operation/stage combination only once; the platform keys steps by that combination. A duplicate is rejected at registration time and would also make the source-generated image/wrapper type names ambiguous.",
		helpLinkUri: $"{HelpLinkBaseUri}/XPC2002.md");

	public static readonly DiagnosticDescriptor PreferNameofOverStringLiteral = new(
		id: "XPC3001",
		title: "Prefer nameof over string literal for handler method",
		messageFormat: "Use 'nameof({0}.{1})' instead of string literal \"{1}\" for compile-time safety",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Using nameof() provides compile-time verification that the method exists and enables refactoring support.",
		helpLinkUri: $"{HelpLinkBaseUri}/XPC3001.md");

	public static readonly DiagnosticDescriptor LegacyImageRegistration = new(
		id: "XPC3002",
		title: "Consider using modern image registration API",
		messageFormat: "Consider using WithPreImage/WithPostImage with nameof() instead of AddImage for type-safe image wrappers",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Info,
		isEnabledByDefault: true,
		helpLinkUri: $"{HelpLinkBaseUri}/XPC3002.md");

	public static readonly DiagnosticDescriptor ImageWithoutMethodReference = new(
		id: "XPC3003",
		title: "Image registration without method reference",
		messageFormat: "WithPreImage/WithPostImage requires method reference syntax (e.g., 'service => service.HandleUpdate'). Using method invocation (e.g., 's => s.HandleUpdate()') will not generate type-safe wrappers.",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		helpLinkUri: $"{HelpLinkBaseUri}/XPC3003.md");

	public static readonly DiagnosticDescriptor LocalPluginContextAsService = new(
		id: "XPC3004",
		title: "Do not use LocalPluginContext as TService in RegisterStep",
		messageFormat: "RegisterStep<{0}, LocalPluginContext> will cause a runtime exception because LocalPluginContext is not registered in the DI container. Use RegisterPluginStep<{0}> instead, or migrate to a DI-registered service type.",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: "LocalPluginContext cannot be resolved via GetRequiredService<T>() in the scoped DI container. Use RegisterPluginStep which correctly wraps the action, or migrate to a DI-registered service.",
		helpLinkUri: $"{HelpLinkBaseUri}/XPC3004.md");

	public static readonly DiagnosticDescriptor HandlerMethodNotFound = new(
		id: "XPC4001",
		title: "Handler method not found",
		messageFormat: "Method '{0}' not found on service type '{1}'",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		helpLinkUri: $"{HelpLinkBaseUri}/XPC4001.md");

	public static readonly DiagnosticDescriptor HandlerSignatureMismatch = new(
		id: "XPC4002",
		title: "Handler signature does not match registered images",
		messageFormat: "Handler method '{0}' does not have expected signature. Expected parameters in order: {1}. PreImage must be the first parameter, followed by PostImage if both are used.",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		helpLinkUri: $"{HelpLinkBaseUri}/XPC4002.md");

	public static readonly DiagnosticDescriptor HandlerSignatureMismatchError = new(
		id: "XPC4003",
		title: "Handler signature does not match registered images",
		messageFormat: "Handler method '{0}' does not have expected signature. Expected parameters in order: {1}. PreImage must be the first parameter, followed by PostImage if both are used.",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		helpLinkUri: $"{HelpLinkBaseUri}/XPC4003.md");

	public static readonly DiagnosticDescriptor CustomApiNameNotConstant = new(
		id: "XPC3006",
		title: "Custom API name must be a compile-time constant",
		messageFormat: "Custom API name must be a compile-time constant (use nameof(...) or a string literal) so the source generator can emit the request/response types and ActionWrapper",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "The typed RegisterAPI<TService>(name, handlerMethodName) overload requires a constant name so the generated classes can be named after the API. A non-constant name produces no generated ActionWrapper and fails at runtime.",
		helpLinkUri: $"{HelpLinkBaseUri}/XPC3006.md");

	public static readonly DiagnosticDescriptor PreferTypedCustomApiHandler = new(
		id: "XPC3007",
		title: "Prefer the type-safe Custom API handler overload",
		messageFormat: "This Custom API declares request/response parameters; use the typed 'RegisterAPI<{0}>(name, nameof(...))' overload to generate strongly-typed request/response DTOs instead of the action-based overload",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Info,
		isEnabledByDefault: true,
		description: "When a Custom API declares AddRequestParameter/AddResponseProperty, the typed RegisterAPI<TService>(name, handlerMethodName) overload generates strongly-typed {ApiName}Request/{ApiName}Response classes and marshals InputParameters/OutputParameters for you. The action-based overload leaves you to read and write those dictionaries by hand.",
		helpLinkUri: $"{HelpLinkBaseUri}/XPC3007.md");

	public static readonly DiagnosticDescriptor CustomApiHandlerMethodNotFound = new(
		id: "XPC4004",
		title: "Custom API handler method not found",
		messageFormat: "Custom API handler method '{0}' not found on service type '{1}'",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		helpLinkUri: $"{HelpLinkBaseUri}/XPC4004.md");

	public static readonly DiagnosticDescriptor CustomApiHandlerSignatureMismatch = new(
		id: "XPC4005",
		title: "Custom API handler signature does not match registered parameters",
		messageFormat: "Custom API handler method '{0}' does not have the expected signature. Expected: {1}.",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		helpLinkUri: $"{HelpLinkBaseUri}/XPC4005.md");

	public static readonly DiagnosticDescriptor CustomApiHandlerSignatureMismatchError = new(
		id: "XPC4006",
		title: "Custom API handler signature does not match registered parameters",
		messageFormat: "Custom API handler method '{0}' does not have the expected signature. Expected: {1}.",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		helpLinkUri: $"{HelpLinkBaseUri}/XPC4006.md");

	public static readonly DiagnosticDescriptor SymbolResolutionFailed = new(
		id: "XPC5001",
		title: "Failed to resolve symbol",
		messageFormat: "Could not resolve RegisterStep method symbol in {0}. Image wrappers will not be generated for this registration.",
		category: Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	public static readonly DiagnosticDescriptor FullEntityImage = new(
		id: "XPC3005",
		title: "Full entity image registration",
		messageFormat: "'{0}' registered without specifying attributes will capture all entity attributes. Consider specifying only the attributes your handler needs for better performance.",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Registering an image without specifying attributes causes Dynamics 365 to serialize all entity attributes into the image, which may impact performance. Specify only the attributes your handler needs.",
		helpLinkUri: $"{HelpLinkBaseUri}/XPC3005.md");

	public static readonly DiagnosticDescriptor GenerationError = new(
		id: "XPC5002",
		title: "Failed to generate wrapper classes",
		messageFormat: "Exception during generation: {0}",
		category: Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

}
