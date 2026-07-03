using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

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
	/// Creates a parameter list for PreImage/PostImage parameters.
	/// </summary>
	public static ParameterListSyntax CreateImageParameterList(bool hasPreImage, bool hasPostImage)
	{
		return CreateImageParameterList(hasPreImage, hasPostImage, qualifier: null);
	}

	/// <summary>
	/// Creates a parameter list for PreImage/PostImage parameters with optional qualifier.
	/// </summary>
	public static ParameterListSyntax CreateImageParameterList(bool hasPreImage, bool hasPostImage, string qualifier)
	{
		var parameters = new List<ParameterSyntax>();

		if (hasPreImage)
		{
			parameters.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier("preImage"))
				.WithType(CreateTypeName(Constants.PreImageTypeName, qualifier)));
		}

		if (hasPostImage)
		{
			parameters.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier("postImage"))
				.WithType(CreateTypeName(Constants.PostImageTypeName, qualifier)));
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

	/// <summary>
	/// Adds a using directive to the compilation unit if it doesn't already exist.
	/// </summary>
	public static SyntaxNode AddUsingDirectiveIfMissing(SyntaxNode root, string namespaceName)
	{
		if (string.IsNullOrEmpty(namespaceName))
		{
			return root;
		}

		if (root is not CompilationUnitSyntax compilationUnit)
		{
			return root;
		}

		var alreadyExists = compilationUnit.Usings.Any(u => u.Name?.ToString() == namespaceName);
		if (alreadyExists)
		{
			return root;
		}

		var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
			.WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

		return compilationUnit.AddUsings(usingDirective);
	}

	/// <summary>
	/// Detects whether adding the given image namespace would create ambiguity with existing usings.
	/// </summary>
	/// <returns>(needsAlias, alias) where alias is the last segment of the namespace.</returns>
	public static (bool needsAlias, string alias) DetectImageAmbiguity(SyntaxNode root, string imageNamespace)
	{
		if (string.IsNullOrEmpty(imageNamespace) || root is not CompilationUnitSyntax compilationUnit)
		{
			return (false, null);
		}

		foreach (var usingDirective in compilationUnit.Usings)
		{
			// Skip aliased usings
			if (usingDirective.Alias != null)
			{
				continue;
			}

			var existingNs = usingDirective.Name?.ToString();
			if (existingNs == null)
			{
				continue;
			}

			if (existingNs != imageNamespace && IsImageRegistrationNamespace(existingNs))
			{
				return (true, GetLastNamespaceSegment(imageNamespace));
			}
		}

		return (false, null);
	}

	/// <summary>
	/// Returns the alias used for an image-registration namespace: its last dot-separated segment.
	/// </summary>
	public static string GetAliasForImageNamespace(string ns) => GetLastNamespaceSegment(ns);

	/// <summary>
	/// Backward-compatible overload without a semantic model. Bare PreImage/PostImage references are
	/// left unqualified (no semantic resolution). Prefer the overload that takes a <see cref="SemanticModel"/>.
	/// </summary>
	public static SyntaxNode ConvertToAliasedUsingsAndQualifyRefs(SyntaxNode root, string newImageNamespace)
		=> ConvertToAliasedUsingsAndQualifyRefs(root, newImageNamespace, semanticModel: null);

	/// <summary>
	/// Always emits the aliased using for <paramref name="imageNamespace"/> and re-qualifies/aliases
	/// every existing image-registration using in the tree. This is the single shared entry point used
	/// by the code-fix providers; alias-qualified parameter lists are produced separately via
	/// <see cref="CreateImageParameterList(bool, bool, string)"/>.
	/// </summary>
	/// <param name="root">The compilation unit (document root) to rewrite.</param>
	/// <param name="imageNamespace">The image-registration namespace to alias and add.</param>
	/// <param name="semanticModel">The semantic model for <paramref name="root"/>'s tree (pre-rewrite).</param>
	public static SyntaxNode ApplyAliasedImageUsings(SyntaxNode root, string imageNamespace, SemanticModel semanticModel)
	{
		if (string.IsNullOrEmpty(imageNamespace))
		{
			// The analyzer only sets the image namespace when an expected one exists; nothing to do.
			return root;
		}

		return ConvertToAliasedUsingsAndQualifyRefs(root, imageNamespace, semanticModel);
	}

	/// <summary>
	/// Multi-namespace variant of <see cref="ApplyAliasedImageUsings(SyntaxNode, string, SemanticModel)"/>.
	/// Adds every distinct aliased image using needed and requalifies every reference in a single pass.
	/// Used by the FixAll path so consolidating N registrations funnels through the same engine as a
	/// single fix.
	/// </summary>
	/// <param name="root">The compilation unit (document root) to rewrite.</param>
	/// <param name="imageNamespaces">The image-registration namespaces to alias and add.</param>
	/// <param name="semanticModel">The semantic model for <paramref name="root"/>'s tree (pre-rewrite).</param>
	public static SyntaxNode ApplyAliasedImageUsings(SyntaxNode root, IEnumerable<string> imageNamespaces, SemanticModel semanticModel)
	{
		if (imageNamespaces == null)
		{
			return root;
		}

		var namespaces = imageNamespaces.Where(ns => !string.IsNullOrEmpty(ns)).Distinct().ToList();
		if (namespaces.Count == 0)
		{
			return root;
		}

		return ConvertToAliasedUsingsAndQualifyRefs(root, namespaces, semanticModel);
	}

	/// <summary>
	/// Converts existing plain image usings to aliased form, qualifies all bare PreImage/PostImage
	/// type references, and adds the new aliased using.
	/// </summary>
	/// <param name="root">The compilation unit (document root) to rewrite.</param>
	/// <param name="newImageNamespace">The image-registration namespace to alias and add.</param>
	/// <param name="semanticModel">
	/// The semantic model for <paramref name="root"/>'s original (pre-rewrite) tree, used to resolve
	/// which alias a bare PreImage/PostImage reference belongs to. May be null, in which case bare
	/// references are left unqualified.
	/// </param>
	public static SyntaxNode ConvertToAliasedUsingsAndQualifyRefs(SyntaxNode root, string newImageNamespace, SemanticModel semanticModel)
		=> ConvertToAliasedUsingsAndQualifyRefs(root, new[] { newImageNamespace }, semanticModel);

	/// <summary>
	/// Converts existing plain image usings to aliased form, qualifies all bare PreImage/PostImage
	/// type references, and adds an aliased using for each of <paramref name="newImageNamespaces"/>.
	/// This is the single engine shared by the single-fix and FixAll code paths.
	/// </summary>
	/// <param name="root">The compilation unit (document root) to rewrite.</param>
	/// <param name="newImageNamespaces">The image-registration namespaces to alias and add.</param>
	/// <param name="semanticModel">
	/// The semantic model for <paramref name="root"/>'s original (pre-rewrite) tree, used to resolve
	/// which alias a bare PreImage/PostImage reference belongs to. May be null, in which case bare
	/// references are left unqualified.
	/// </param>
	private static SyntaxNode ConvertToAliasedUsingsAndQualifyRefs(SyntaxNode root, IReadOnlyCollection<string> newImageNamespaces, SemanticModel semanticModel)
	{
		if (root is not CompilationUnitSyntax compilationUnit)
		{
			return root;
		}

		// Build map of existing plain image namespace usings → alias
		var plainNamespaceToAlias = new Dictionary<string, string>();
		foreach (var usingDirective in compilationUnit.Usings)
		{
			if (usingDirective.Alias != null)
			{
				continue;
			}

			var ns = usingDirective.Name?.ToString();
			if (ns != null && IsImageRegistrationNamespace(ns))
			{
				plainNamespaceToAlias[ns] = GetLastNamespaceSegment(ns);
			}
		}

		// Also include each new namespace
		foreach (var newImageNamespace in newImageNamespaces)
		{
			if (!string.IsNullOrEmpty(newImageNamespace))
			{
				plainNamespaceToAlias[newImageNamespace] = GetLastNamespaceSegment(newImageNamespace);
			}
		}

		// Rewrite the tree
		var rewriter = new ImageAmbiguityRewriter(plainNamespaceToAlias, semanticModel);
		var newRoot = rewriter.Visit(root);

		// Add each new aliased using if not already present
		if (newRoot is CompilationUnitSyntax newCompUnit)
		{
			var usingsToAdd = new List<UsingDirectiveSyntax>();
			foreach (var newImageNamespace in newImageNamespaces)
			{
				if (string.IsNullOrEmpty(newImageNamespace))
				{
					continue;
				}

				var newAlias = GetLastNamespaceSegment(newImageNamespace);

				// The standard alias is "already present" only if some using binds exactly that alias
				// name, or a plain (non-aliased) using imports the namespace (which the rewriter above
				// would already have converted to the standard alias). A using that imports the same
				// namespace under a DIFFERENT alias does NOT count: the emitted parameter types are
				// qualified with the standard alias, so we must still add it or the code won't compile.
				var alreadyExists = newCompUnit.Usings.Any(u =>
						u.Alias?.Name.ToString() == newAlias ||
						(u.Alias == null && u.Name?.ToString() == newImageNamespace)) ||
					usingsToAdd.Any(u => u.Alias?.Name.ToString() == newAlias);

				if (!alreadyExists)
				{
					usingsToAdd.Add(SyntaxFactory.UsingDirective(
							SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName(newAlias)),
							SyntaxFactory.ParseName(newImageNamespace))
						.WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed));
				}
			}

			if (usingsToAdd.Count > 0)
			{
				newRoot = newCompUnit.AddUsings(usingsToAdd.ToArray());
			}
		}

		return newRoot;
	}

	private static bool IsImageRegistrationNamespace(string ns)
	{
		return ns.Contains(".PluginRegistrations.");
	}

	private static string GetLastNamespaceSegment(string ns)
	{
		var lastDot = ns.LastIndexOf('.');
		return lastDot >= 0 ? ns.Substring(lastDot + 1) : ns;
	}

	private static TypeSyntax CreateTypeName(string typeName, string qualifier)
	{
		if (qualifier == null)
		{
			return SyntaxFactory.IdentifierName(typeName);
		}

		return SyntaxFactory.QualifiedName(
			SyntaxFactory.IdentifierName(qualifier),
			SyntaxFactory.IdentifierName(typeName));
	}

	/// <summary>
	/// Rewrites a syntax tree to convert plain image usings to aliased form
	/// and qualify bare PreImage/PostImage references.
	/// </summary>
	private sealed class ImageAmbiguityRewriter : CSharpSyntaxRewriter
	{
		private readonly Dictionary<string, string> _plainNamespaceToAlias;

		// Semantic model for the original (pre-rewrite) tree, used to resolve which image
		// namespace a bare PreImage/PostImage reference actually binds to. May be null.
		private readonly SemanticModel _semanticModel;

		public ImageAmbiguityRewriter(Dictionary<string, string> plainNamespaceToAlias, SemanticModel semanticModel)
		{
			_plainNamespaceToAlias = plainNamespaceToAlias;
			_semanticModel = semanticModel;
		}

		public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node)
		{
			if (node.Alias != null)
			{
				return base.VisitUsingDirective(node);
			}

			var ns = node.Name?.ToString();
			if (ns != null && _plainNamespaceToAlias.TryGetValue(ns, out var alias))
			{
				// Convert to aliased using
				return SyntaxFactory.UsingDirective(
						SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName(alias)),
						node.Name!)
					.WithLeadingTrivia(node.GetLeadingTrivia())
					.WithTrailingTrivia(node.GetTrailingTrivia());
			}

			return base.VisitUsingDirective(node);
		}

		public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
		{
			var name = node.Identifier.Text;

			// Only qualify PreImage and PostImage
			if (name != Constants.PreImageTypeName && name != Constants.PostImageTypeName)
			{
				return base.VisitIdentifierName(node);
			}

			// Exclusion: already qualified (parent is QualifiedNameSyntax and we are the right side)
			if (node.Parent is QualifiedNameSyntax qualifiedParent && qualifiedParent.Right == node)
			{
				return base.VisitIdentifierName(node);
			}

			// Exclusion: member access name (e.g., obj.PreImage) — not a type reference
			if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node)
			{
				return base.VisitIdentifierName(node);
			}

			// Exclusion: alias name in using directive
			if (node.Parent is NameEqualsSyntax)
			{
				return base.VisitIdentifierName(node);
			}

			// Resolve which image namespace this bare reference binds to via the semantic model
			// (built from the original, pre-rewrite tree). Only qualify when it resolves uniquely
			// to a single image-registration namespace whose alias we know. If the reference is
			// ambiguous or unresolved, leave it unqualified so it surfaces as a normal compile error
			// rather than guessing — this keeps requalification correct with any number of plain
			// image usings in the file.
			var matchingAlias = ResolveImageAlias(node);
			if (matchingAlias == null)
			{
				return base.VisitIdentifierName(node);
			}

			// Replace bare identifier with qualified name
			var qualifiedName = SyntaxFactory.QualifiedName(
				SyntaxFactory.IdentifierName(matchingAlias),
				SyntaxFactory.IdentifierName(name));

			return qualifiedName
				.WithLeadingTrivia(node.GetLeadingTrivia())
				.WithTrailingTrivia(node.GetTrailingTrivia());
		}

		private string ResolveImageAlias(IdentifierNameSyntax node)
		{
			if (_semanticModel == null)
			{
				return null;
			}

			var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
			if (symbol == null)
			{
				// Ambiguous (e.g. CS0104) or otherwise unresolved — do not guess.
				return null;
			}

			var containingNamespace = symbol.ContainingNamespace?.ToDisplayString();
			if (containingNamespace != null && _plainNamespaceToAlias.TryGetValue(containingNamespace, out var alias))
			{
				return alias;
			}

			return null;
		}
	}
}
