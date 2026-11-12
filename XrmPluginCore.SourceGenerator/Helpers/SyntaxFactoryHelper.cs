using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace XrmPluginCore.SourceGenerator.Helpers;

/// <summary>
/// Shared utilities for syntax generation.
/// </summary>
internal static class SyntaxFactoryHelper
{
	/// <summary>
	/// Creates a nameof(ServiceType.MethodName) expression. <paramref name="serviceType"/> is parsed
	/// as a name so qualified (<c>My.Namespace.Service</c>) and generic (<c>Service&lt;T&gt;</c>) type
	/// names produce valid syntax rather than a single malformed identifier.
	/// </summary>
	public static InvocationExpressionSyntax CreateNameofExpression(string serviceType, string methodName)
	{
		return SyntaxFactory.InvocationExpression(
			SyntaxFactory.IdentifierName("nameof"),
			SyntaxFactory.ArgumentList(
				SyntaxFactory.SingletonSeparatedList(
					SyntaxFactory.Argument(
						SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							SyntaxFactory.ParseName(serviceType),
							SyntaxFactory.IdentifierName(methodName))))));
	}

	/// <summary>
	/// Creates a parameter list for the generated image wrappers. Each parameter is typed with the
	/// fully-qualified generated wrapper type (e.g. <c>MyNs.AccountPluginAccountUpdatePostOperationPreImage</c>),
	/// so no using directive is required at the handler's declaration site. A null type name omits that
	/// parameter.
	/// </summary>
	public static ParameterListSyntax CreateImageParameterList(string preImageTypeName, string postImageTypeName)
	{
		var parameters = new List<ParameterSyntax>();

		if (preImageTypeName != null)
		{
			parameters.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier("preImage"))
				.WithType(SyntaxFactory.ParseTypeName(preImageTypeName)));
		}

		if (postImageTypeName != null)
		{
			parameters.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier("postImage"))
				.WithType(SyntaxFactory.ParseTypeName(postImageTypeName)));
		}

		return SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters));
	}

	/// <summary>
	/// Builds a signature description string for PreImage/PostImage parameters.
	/// </summary>
	/// <param name="hasPreImage">Whether PreImage is included.</param>
	/// <param name="hasPostImage">Whether PostImage is included.</param>
	/// <param name="includeParameterNames">If true, includes parameter names (e.g., "PreImage preImage"); if false, just type names.</param>
	public static string BuildSignatureDescription(bool hasPreImage, bool hasPostImage, bool includeParameterNames = false)
	{
		if (!hasPreImage && !hasPostImage)
		{
			return "";
		}

		var parts = new List<string>();
		if (hasPreImage)
		{
			parts.Add(includeParameterNames ? "PreImage preImage" : "PreImage");
		}

		if (hasPostImage)
		{
			parts.Add(includeParameterNames ? "PostImage postImage" : "PostImage");
		}

		return string.Join(", ", parts);
	}
}
