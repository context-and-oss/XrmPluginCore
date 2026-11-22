using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;
using XrmPluginCore.SourceGenerator.Helpers;

namespace XrmPluginCore.SourceGenerator.Analyzers;

/// <summary>
/// Analyzer that reports an error when a handler method referenced in RegisterStep does not exist.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class HandlerMethodNotFoundAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(DiagnosticDescriptors.HandlerMethodNotFound);

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

		// Check if the method exists on the service type (including inherited methods)
		var methods = TypeHelper.GetAllMethodsIncludingInherited(serviceType, methodName);
		if (methods.Any())
		{
			return; // Method exists, no error
		}

		// Create diagnostic properties for the code fix
		var properties = ImmutableDictionary.CreateBuilder<string, string>();
		properties.Add(Constants.PropertyServiceType, serviceType.Name);
		properties.Add(Constants.PropertyMethodName, methodName);

		// Determine if there are images registered by checking the call chain
		var (hasPreImage, hasPostImage) = RegisterStepHelper.CheckForImages(invocation);
		properties.Add(Constants.PropertyHasPreImage, hasPreImage.ToString());
		properties.Add(Constants.PropertyHasPostImage, hasPostImage.ToString());

		if (hasPreImage)
		{
			var preImageTypeName = RegisterStepHelper.GetExpectedImageTypeName(invocation, genericName, context.SemanticModel, Constants.PreImageTypeName);
			if (preImageTypeName != null)
			{
				properties.Add(Constants.PropertyPreImageType, preImageTypeName);
			}
		}

		if (hasPostImage)
		{
			var postImageTypeName = RegisterStepHelper.GetExpectedImageTypeName(invocation, genericName, context.SemanticModel, Constants.PostImageTypeName);
			if (postImageTypeName != null)
			{
				properties.Add(Constants.PropertyPostImageType, postImageTypeName);
			}
		}

		var diagnostic = Diagnostic.Create(
			DiagnosticDescriptors.HandlerMethodNotFound,
			handlerArgument.GetLocation(),
			properties.ToImmutable(),
			methodName,
			serviceType.Name);

		context.ReportDiagnostic(diagnostic);
	}
}
