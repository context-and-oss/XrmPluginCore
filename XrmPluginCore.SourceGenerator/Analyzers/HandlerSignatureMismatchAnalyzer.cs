using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;
using XrmPluginCore.SourceGenerator.Helpers;

namespace XrmPluginCore.SourceGenerator.Analyzers;

/// <summary>
/// Analyzer that reports when a handler method signature does not match the registered images.
/// Reports XPC4002 (Warning) when generated types don't exist yet, XPC4003 (Error) when they do.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class HandlerSignatureMismatchAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(
			DiagnosticDescriptors.HandlerSignatureMismatch,
			DiagnosticDescriptors.HandlerSignatureMismatchError);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
	}

	private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;

		// Check if this is a RegisterStep call
		if (!RegisterStepHelper.IsRegisterStepCall(invocation, out var genericName))
		{
			return;
		}

		// Check if there are at least 2 type arguments (TEntity, TService)
		if (genericName.TypeArgumentList.Arguments.Count < 2)
		{
			return;
		}

		// Check if there's a 3rd argument (the handler method)
		var arguments = invocation.ArgumentList.Arguments;
		if (arguments.Count < 3)
		{
			return;
		}

		var handlerArgument = arguments[2].Expression;

		// Get the method name from nameof or string literal
		var methodName = RegisterStepHelper.GetMethodName(handlerArgument);
		if (methodName == null)
		{
			return;
		}

		// Get the service type symbol
		var serviceTypeSyntax = genericName.TypeArgumentList.Arguments[1];
		var serviceTypeInfo = context.SemanticModel.GetTypeInfo(serviceTypeSyntax);
		var serviceType = serviceTypeInfo.Type;

		if (serviceType == null)
		{
			return;
		}

		// Check if the method exists on the service type
		var methods = TypeHelper.GetAllMethodsIncludingInherited(serviceType, methodName);
		if (!methods.Any())
		{
			return; // Method doesn't exist - XPC4001 handles this
		}

		// Check for registered images
		var (hasPreImage, hasPostImage) = RegisterStepHelper.CheckForImages(invocation);

		// If no images registered, no signature check needed
		if (!hasPreImage && !hasPostImage)
		{
			return;
		}

		// Resolve the registered entity type (TEntity) for validating typed image interfaces
		var entityTypeSyntax = genericName.TypeArgumentList.Arguments[0];
		var entityType = context.SemanticModel.GetTypeInfo(entityTypeSyntax).Type;

		// Compute the fully-qualified names of the generated image wrapper types. They live in the
		// plugin's own namespace with registration-derived names.
		var preImageTypeName = hasPreImage
			? RegisterStepHelper.GetExpectedImageTypeName(invocation, genericName, context.SemanticModel, Constants.PreImageTypeName)
			: null;
		var postImageTypeName = hasPostImage
			? RegisterStepHelper.GetExpectedImageTypeName(invocation, genericName, context.SemanticModel, Constants.PostImageTypeName)
			: null;

		// If the expected type name can't be resolved, generation can't produce a stable name either, so
		// skip rather than report a spurious mismatch.
		if ((hasPreImage && preImageTypeName == null) || (hasPostImage && postImageTypeName == null))
		{
			return;
		}

		// Check if any overload matches the expected signature
		var hasMatchingOverload = methods.Any(method => SignatureMatches(method, hasPreImage, hasPostImage, entityType, preImageTypeName, postImageTypeName));
		if (hasMatchingOverload)
		{
			return;
		}

		// Build expected signature description
		var expectedSignature = SyntaxFactoryHelper.BuildSignatureDescription(hasPreImage, hasPostImage);

		// Determine if generated types exist to choose appropriate diagnostic severity
		var generatedTypesExist = DoGeneratedTypesExist(context, preImageTypeName, postImageTypeName);

		// Choose diagnostic: XPC4003 (Error) if types exist, XPC4002 (Warning) if they don't
		var descriptor = generatedTypesExist
			? DiagnosticDescriptors.HandlerSignatureMismatchError
			: DiagnosticDescriptors.HandlerSignatureMismatch;

		// Create diagnostic properties for the code fix
		var properties = ImmutableDictionary.CreateBuilder<string, string>();
		properties.Add(Constants.PropertyServiceType, serviceType.Name);
		properties.Add(Constants.PropertyMethodName, methodName);
		properties.Add(Constants.PropertyHasPreImage, hasPreImage.ToString());
		properties.Add(Constants.PropertyHasPostImage, hasPostImage.ToString());
		if (preImageTypeName != null)
		{
			properties.Add(Constants.PropertyPreImageType, preImageTypeName);
		}
		if (postImageTypeName != null)
		{
			properties.Add(Constants.PropertyPostImageType, postImageTypeName);
		}

		var diagnostic = Diagnostic.Create(
			descriptor,
			handlerArgument.GetLocation(),
			properties.ToImmutable(),
			methodName,
			expectedSignature);

		context.ReportDiagnostic(diagnostic);
	}

	private static bool DoGeneratedTypesExist(
		SyntaxNodeAnalysisContext context,
		string preImageTypeName,
		string postImageTypeName)
	{
		var compilation = context.SemanticModel.Compilation;

		if (preImageTypeName != null && compilation.GetTypeByMetadataName(preImageTypeName) == null)
		{
			return false;
		}

		if (postImageTypeName != null && compilation.GetTypeByMetadataName(postImageTypeName) == null)
		{
			return false;
		}

		return true;
	}

	private static bool SignatureMatches(IMethodSymbol method, bool hasPreImage, bool hasPostImage, ITypeSymbol entityType, string preImageTypeName, string postImageTypeName)
	{
		var parameters = method.Parameters;
		var expectedParamCount = (hasPreImage ? 1 : 0) + (hasPostImage ? 1 : 0);

		if (parameters.Length != expectedParamCount)
		{
			return false;
		}

		var paramIndex = 0;

		if (hasPreImage)
		{
			if (paramIndex >= parameters.Length)
			{
				return false;
			}

			if (!IsImageParameter(parameters[paramIndex], preImageTypeName, Constants.PreImageInterfaceName, entityType))
			{
				return false;
			}

			paramIndex++;
		}

		if (hasPostImage)
		{
			if (paramIndex >= parameters.Length)
			{
				return false;
			}

			if (!IsImageParameter(parameters[paramIndex], postImageTypeName, Constants.PostImageInterfaceName, entityType))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Determines whether a handler parameter is a valid image parameter. A parameter is valid when it is:
	/// <list type="bullet">
	/// <item>the concrete generated wrapper type (PreImage / PostImage), or</item>
	/// <item>one of the shared XrmPluginCore image interfaces matching the image kind
	/// (IPluginImage / IPluginPreImage / IPluginPostImage, generic or non-generic).</item>
	/// </list>
	/// For the generic interfaces, the type argument must match the registered entity type, otherwise the
	/// generated ActionWrapper would fail to compile when passing the concrete image.
	/// </summary>
	private static bool IsImageParameter(IParameterSymbol parameter, string expectedFullTypeName, string expectedInterfaceName, ITypeSymbol entityType)
	{
		var type = parameter.Type;

		// Concrete generated wrapper: match by fully-qualified name so a same-named type in a different
		// namespace is not mistaken for the generated wrapper.
		if (FullTypeName(type) == expectedFullTypeName)
		{
			return true;
		}

		// Shared image interfaces - must be declared in XrmPluginCore.
		if (type.ContainingNamespace?.ToString() != Constants.PluginNamespace)
		{
			return false;
		}

		// The non-generic base interface matches the kind, but not a more specific opposite kind.
		if (type.Name != Constants.PluginImageInterfaceName && type.Name != expectedInterfaceName)
		{
			return false;
		}

		// For generic interfaces, the entity type argument must match the registered entity.
		if (type is INamedTypeSymbol named && named.IsGenericType)
		{
			var argument = named.TypeArguments.Length == 1 ? named.TypeArguments[0] : null;
			return argument != null
				&& entityType != null
				&& SymbolEqualityComparer.Default.Equals(argument, entityType);
		}

		return true;
	}

	/// <summary>
	/// The namespace-qualified name of a type (no <c>global::</c> prefix), matching the format produced by
	/// <see cref="RegisterStepHelper.GetExpectedImageTypeName"/>.
	/// </summary>
	private static string FullTypeName(ITypeSymbol type)
	{
		var containingNamespace = type.ContainingNamespace;
		return containingNamespace == null || containingNamespace.IsGlobalNamespace
			? type.Name
			: $"{containingNamespace.ToDisplayString()}.{type.Name}";
	}
}
