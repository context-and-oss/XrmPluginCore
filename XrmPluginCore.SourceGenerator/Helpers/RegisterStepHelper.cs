using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Linq;
using System.Threading;

namespace XrmPluginCore.SourceGenerator.Helpers;

/// <summary>
/// Shared utilities for analyzing RegisterStep invocations.
/// </summary>
internal static class RegisterStepHelper
{
	/// <summary>
	/// Computes the fully-qualified name of a generated image wrapper type for a RegisterStep invocation,
	/// e.g. <c>{PluginNamespace}.{Sanitize(PluginClass+Entity+Operation+Stage)}{imageTypeSuffix}</c> where
	/// <paramref name="imageTypeSuffix"/> is "PreImage" or "PostImage". Must stay in sync with the
	/// generator (<see cref="Models.PluginStepMetadata"/>) and the runtime discovery in
	/// <c>Plugin.RegisterStep</c>. Returns null when the registration tuple can't be resolved.
	/// </summary>
	public static string GetExpectedImageTypeName(
		InvocationExpressionSyntax invocation,
		GenericNameSyntax genericName,
		SemanticModel semanticModel,
		string imageTypeSuffix)
	{
		var pluginClass = invocation.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
		if (pluginClass == null)
		{
			return null;
		}

		var pluginNamespace = GetNamespace(pluginClass);

		var entityTypeInfo = semanticModel.GetTypeInfo(genericName.TypeArgumentList.Arguments[0]);
		var entityTypeName = entityTypeInfo.Type?.Name ?? "Unknown";

		var (operationExpr, stageExpr) = GetOperationAndStageArguments(invocation, semanticModel);
		if (operationExpr == null || stageExpr == null)
		{
			return null;
		}

		var operation = ExtractEnumValue(operationExpr);
		var stage = ExtractEnumValue(stageExpr);

		var prefix = IdentifierHelper.Sanitize($"{pluginClass.Identifier.Text}{entityTypeName}{operation}{stage}");
		var ns = string.IsNullOrEmpty(pluginNamespace) ? string.Empty : pluginNamespace + ".";
		return $"{ns}{prefix}{imageTypeSuffix}";
	}

	/// <summary>
	/// Resolves the <c>eventOperation</c> and <c>executionStage</c> argument expressions by parameter
	/// name (so named/reordered arguments are honored). Either may be null when the call cannot be
	/// resolved.
	/// </summary>
	public static (ExpressionSyntax operation, ExpressionSyntax stage) GetOperationAndStageArguments(
		InvocationExpressionSyntax invocation,
		SemanticModel semanticModel)
	{
		var bound = ArgumentBinder.Bind(invocation, semanticModel);
		var operation = bound.TryGetValue(Constants.ParameterEventOperation, out var op) ? op : null;
		var stage = bound.TryGetValue(Constants.ParameterExecutionStage, out var st) ? st : null;
		return (operation, stage);
	}

	/// <summary>
	/// Extracts the namespace from a syntax node by walking up the tree.
	/// </summary>
	public static string GetNamespace(SyntaxNode node)
	{
		while (node != null)
		{
			if (node is NamespaceDeclarationSyntax namespaceDecl)
			{
				return namespaceDecl.Name.ToString();
			}

			if (node is FileScopedNamespaceDeclarationSyntax fileScopedNs)
			{
				return fileScopedNs.Name.ToString();
			}

			node = node.Parent;
		}

		return string.Empty;
	}

	/// <summary>
	/// Extracts the enum value name from an expression (e.g., EventOperation.Update -> "Update").
	/// </summary>
	public static string ExtractEnumValue(ExpressionSyntax expression)
	{
		if (expression is MemberAccessExpressionSyntax memberAccess)
		{
			return memberAccess.Name.Identifier.Text;
		}

		if (expression is LiteralExpressionSyntax literal)
		{
			return literal.Token.ValueText;
		}

		return "Unknown";
	}

	/// <summary>
	/// Checks if an invocation is a RegisterStep call and extracts the generic name.
	/// </summary>
	public static bool IsRegisterStepCall(InvocationExpressionSyntax invocation, out GenericNameSyntax genericName)
	{
		genericName = null;

		// Handle: this.RegisterStep<...>(...) or RegisterStep<...>(...)
		if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
		{
			if (memberAccess.Name is GenericNameSyntax generic &&
				generic.Identifier.Text == Constants.RegisterStepMethodName)
			{
				genericName = generic;
				return true;
			}
		}

		// Handle: RegisterStep<...>(...) without 'this.'
		if (invocation.Expression is GenericNameSyntax directGeneric &&
			directGeneric.Identifier.Text == Constants.RegisterStepMethodName)
		{
			genericName = directGeneric;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Checks if an invocation is a <c>RegisterStep</c> or legacy <c>RegisterPluginStep</c> call and
	/// extracts the generic name. Both identify a plugin step whose (entity, operation, stage) tuple
	/// must be unique within a plugin.
	/// </summary>
	public static bool IsStepRegistrationCall(InvocationExpressionSyntax invocation, out GenericNameSyntax genericName)
	{
		genericName = GetGenericName(invocation);
		return genericName != null &&
			(genericName.Identifier.Text == Constants.RegisterStepMethodName ||
			 genericName.Identifier.Text == Constants.RegisterPluginStepMethodName);
	}

	/// <summary>
	/// Resolves the service type symbol (the second generic argument of a RegisterStep call) from a
	/// diagnostic location. Returns null if no enclosing RegisterStep call is found or the type cannot
	/// be resolved.
	/// </summary>
	public static INamedTypeSymbol ResolveServiceType(
		SyntaxNode root,
		SemanticModel semanticModel,
		TextSpan diagnosticSpan,
		CancellationToken cancellationToken)
	{
		var diagnosticNode = root.FindNode(diagnosticSpan);
		var registerStepInvocation = diagnosticNode.AncestorsAndSelf()
			.OfType<InvocationExpressionSyntax>()
			.FirstOrDefault(i => IsRegisterStepCall(i, out _));

		if (registerStepInvocation == null)
		{
			return null;
		}

		var genericName = GetGenericName(registerStepInvocation);
		if (genericName == null || genericName.TypeArgumentList.Arguments.Count < 2)
		{
			return null;
		}

		var serviceTypeSyntax = genericName.TypeArgumentList.Arguments[1];
		var typeInfo = semanticModel.GetTypeInfo(serviceTypeSyntax, cancellationToken);
		return typeInfo.Type as INamedTypeSymbol;
	}

	/// <summary>
	/// Gets the GenericNameSyntax from a RegisterStep call.
	/// </summary>
	public static GenericNameSyntax GetGenericName(InvocationExpressionSyntax invocation)
	{
		if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
			memberAccess.Name is GenericNameSyntax generic)
		{
			return generic;
		}

		if (invocation.Expression is GenericNameSyntax directGeneric)
		{
			return directGeneric;
		}

		return null;
	}

	/// <summary>
	/// Extracts method name from nameof(), string literal, or lambda expressions.
	/// </summary>
	public static string GetMethodName(ExpressionSyntax expression)
	{
		// Handle nameof(): nameof(IService.HandleDelete)
		if (expression is InvocationExpressionSyntax invocation &&
			invocation.Expression is IdentifierNameSyntax identifier &&
			identifier.Identifier.Text == "nameof")
		{
			var argument = invocation.ArgumentList.Arguments.FirstOrDefault();
			if (argument?.Expression is MemberAccessExpressionSyntax memberAccess)
			{
				return memberAccess.Name.Identifier.Text;
			}

			if (argument?.Expression is IdentifierNameSyntax simpleIdentifier)
			{
				return simpleIdentifier.Identifier.Text;
			}
		}

		// Handle string literal: "HandleDelete"
		if (expression is LiteralExpressionSyntax literal &&
			literal.IsKind(SyntaxKind.StringLiteralExpression))
		{
			return literal.Token.ValueText;
		}

		// Handle lambda: service => service.HandleUpdate
		if (expression is SimpleLambdaExpressionSyntax simpleLambda)
		{
			if (simpleLambda.Body is MemberAccessExpressionSyntax memberAccess)
			{
				return memberAccess.Name.Identifier.Text;
			}
		}

		if (expression is ParenthesizedLambdaExpressionSyntax parenLambda)
		{
			if (parenLambda.Body is MemberAccessExpressionSyntax memberAccess)
			{
				return memberAccess.Name.Identifier.Text;
			}
		}

		return null;
	}

	/// <summary>
	/// Checks the call chain for WithPreImage/WithPostImage/AddImage registrations.
	/// </summary>
	public static (bool hasPreImage, bool hasPostImage) CheckForImages(InvocationExpressionSyntax registerStepInvocation)
	{
		var hasPreImage = false;
		var hasPostImage = false;

		// Walk up to find the full fluent call chain
		var current = registerStepInvocation.Parent;

		while (current != null)
		{
			if (current is MemberAccessExpressionSyntax memberAccess)
			{
				var methodName = memberAccess.Name.Identifier.Text;
				if (methodName == Constants.WithPreImageMethodName)
				{
					hasPreImage = true;
				}
				else if (methodName == Constants.WithPostImageMethodName)
				{
					hasPostImage = true;
				}
				else if (methodName == Constants.AddImageMethodName)
				{
					// Need to check the ImageType argument
					if (current.Parent is InvocationExpressionSyntax addImageInvocation)
					{
						var args = addImageInvocation.ArgumentList.Arguments;
						if (args.Count > 0 && args[0].Expression is MemberAccessExpressionSyntax imageTypeAccess)
						{
							var imageTypeName = imageTypeAccess.Name.Identifier.Text;
							if (imageTypeName == Constants.PreImageTypeName)
							{
								hasPreImage = true;
							}
							else if (imageTypeName == Constants.PostImageTypeName)
							{
								hasPostImage = true;
							}
						}
					}
				}
			}

			current = current.Parent;
		}

		return (hasPreImage, hasPostImage);
	}
}
