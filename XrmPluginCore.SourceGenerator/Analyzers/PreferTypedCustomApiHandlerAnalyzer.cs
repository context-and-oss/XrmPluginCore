using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using XrmPluginCore.SourceGenerator.Helpers;

namespace XrmPluginCore.SourceGenerator.Analyzers;

/// <summary>
/// Suggests migrating an action-based <c>RegisterAPI&lt;TService&gt;(name, Action&lt;TService&gt;)</c> call that
/// declares <c>AddRequestParameter</c>/<c>AddResponseProperty</c> to the type-safe
/// <c>RegisterAPI&lt;TService&gt;(name, nameof(TService.Handler))</c> overload, which generates strongly-typed
/// request/response DTOs (mirroring the type-safe image support for plugin steps).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PreferTypedCustomApiHandlerAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(DiagnosticDescriptors.PreferTypedCustomApiHandler);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
	}

	private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;

		// Only the generic action overload is a candidate: the typed handler-name overload already
		// generates DTOs, and the non-generic overloads have no TService to point nameof(...) at.
		if (!RegisterApiHelper.IsRegisterApiCall(invocation, out var genericName) ||
			!RegisterApiHelper.IsActionOverload(invocation, context.SemanticModel))
		{
			return;
		}

		if (genericName.TypeArgumentList.Arguments.Count < 1)
		{
			return;
		}

		// Only suggest the typed overload when there is actually a request/response shape to generate.
		var (hasRequest, hasResponse) = RegisterApiHelper.CheckForParameters(invocation);
		if (!hasRequest && !hasResponse)
		{
			return;
		}

		var actionArgument = RegisterApiHelper.GetActionArgument(invocation, context.SemanticModel);
		if (actionArgument == null)
		{
			return;
		}

		var serviceType = genericName.TypeArgumentList.Arguments[0].ToString();

		// Pass the handler method name to the code fix when it can be extracted from the lambda body;
		// when it can't (e.g. a statement block), the diagnostic still reports but no automatic fix is offered.
		var properties = ImmutableDictionary.CreateBuilder<string, string>();
		properties.Add(Constants.PropertyServiceType, serviceType);

		var methodName = RegisterApiHelper.GetActionMethodName(actionArgument);
		if (methodName != null)
		{
			properties.Add(Constants.PropertyMethodName, methodName);
		}

		context.ReportDiagnostic(Diagnostic.Create(
			DiagnosticDescriptors.PreferTypedCustomApiHandler,
			actionArgument.GetLocation(),
			properties.ToImmutable(),
			serviceType));
	}
}
