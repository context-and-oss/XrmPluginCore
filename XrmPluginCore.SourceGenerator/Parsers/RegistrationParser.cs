using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using XrmPluginCore.SourceGenerator.CodeGeneration;
using XrmPluginCore.SourceGenerator.Helpers;
using XrmPluginCore.SourceGenerator.Models;

namespace XrmPluginCore.SourceGenerator.Parsers;

/// <summary>
/// Parses plugin class syntax to extract registration metadata
/// </summary>
internal static class RegistrationParser
{
	/// <summary>
	/// Parses a plugin class and extracts all plugin step metadata
	/// </summary>
	public static IEnumerable<PluginStepMetadata> ParsePluginClass(
		ClassDeclarationSyntax classDeclaration,
		SemanticModel semanticModel,
		bool nullableEnabled = false)
	{
		// Check if plugin class has a parameterless constructor
		var hasParameterlessConstructor = classDeclaration.Members
			.OfType<ConstructorDeclarationSyntax>()
			.Any(c => c.ParameterList.Parameters.Count == 0);

		// Check if class has ANY explicit constructors
		var hasExplicitConstructors = classDeclaration.Members
			.OfType<ConstructorDeclarationSyntax>()
			.Any();

		// If class has explicit constructors but no parameterless one, abort generation
		// Note: XPC2001 (NoParameterlessConstructor) is handled by a separate analyzer
		if (hasExplicitConstructors && !hasParameterlessConstructor)
		{
			yield break;
		}

		// Find the parameterless constructor (registration pipeline only supports parameterless)
		var constructor = classDeclaration.Members
			.OfType<ConstructorDeclarationSyntax>()
			.FirstOrDefault(c => c.ParameterList.Parameters.Count == 0);

		if (constructor == null)
			yield break;

		// Find all RegisterStep invocations
		foreach (var registerStep in SyntaxHelper.FindRegisterStepInvocations(constructor))
		{
			var metadata = ParseRegisterStepInvocation(registerStep, semanticModel, classDeclaration, nullableEnabled);
			if (metadata != null)
			{
				yield return metadata;
			}
		}
	}

	/// <summary>
	/// Parses a single RegisterStep invocation
	/// </summary>
	private static PluginStepMetadata ParseRegisterStepInvocation(
		InvocationExpressionSyntax registerStepInvocation,
		SemanticModel semanticModel,
		ClassDeclarationSyntax classDeclaration,
		bool nullableEnabled)
	{
		// Get the symbol info to extract type arguments
		var symbolInfo = semanticModel.GetSymbolInfo(registerStepInvocation);

		// Handle both resolved symbols and candidate symbols (when overload resolution is ambiguous)
		IMethodSymbol methodSymbol = symbolInfo.Symbol as IMethodSymbol;
		if (methodSymbol == null && symbolInfo.CandidateSymbols.Length > 0)
		{
			methodSymbol = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
		}

		if (methodSymbol == null)
		{
			return null;
		}

		// Extract entity type from generic parameter TEntity
		if (methodSymbol.TypeArguments.Length == 0)
		{
			return null;
		}

		var entityType = methodSymbol.TypeArguments[0];
		var metadata = new PluginStepMetadata
		{
			EntityTypeName = entityType.Name,
			EntityTypeFullName = entityType.ToDisplayString(),
			Namespace = classDeclaration.GetNamespace(),
			PluginClassName = classDeclaration.Identifier.ValueText,
			NullableAnnotationsEnabled = nullableEnabled
		};

		// Extract service type from generic parameter TService (if present)
		if (methodSymbol.TypeArguments.Length >= 2)
		{
			var serviceType = methodSymbol.TypeArguments[1];
			metadata.ServiceTypeName = serviceType.Name;
			metadata.ServiceTypeFullName = serviceType.ToDisplayString();
		}

		// Resolve EventOperation, ExecutionStage and the handler argument by parameter name so named or
		// reordered arguments are honored (positional calls bind to the same parameters).
		var boundArguments = ArgumentBinder.Bind(registerStepInvocation, semanticModel);

		if (boundArguments.TryGetValue(Constants.ParameterEventOperation, out var operationExpr))
		{
			metadata.EventOperation = ExtractEnumValue(operationExpr);
		}

		if (boundArguments.TryGetValue(Constants.ParameterExecutionStage, out var stageExpr))
		{
			metadata.ExecutionStage = ExtractEnumValue(stageExpr);
		}

		// The handler argument is 'handlerMethodName' on the typed overload and 'action' on the
		// lambda/action overloads; GetMethodName handles nameof, string literals and lambda bodies.
		if (boundArguments.TryGetValue(Constants.ParameterHandlerMethodName, out var handlerExpr) ||
			boundArguments.TryGetValue(Constants.ParameterAction, out handlerExpr))
		{
			metadata.HandlerMethodName = RegisterStepHelper.GetMethodName(handlerExpr);
		}

		// Find image calls
		foreach (var imageCall in SyntaxHelper.FindImageInvocations(registerStepInvocation))
		{
			var imageMetadata = ParseImageInvocation(imageCall, entityType, nullableEnabled);
			if (imageMetadata != null)
			{
				metadata.Images.Add(imageMetadata);
			}
		}

		// Return metadata if we have a method reference (for code generation)
		// OR if we have diagnostics to report
		// Note: XPC3003 (ImageWithoutMethodReference) is handled by a separate analyzer
		return !string.IsNullOrEmpty(metadata.HandlerMethodName) || metadata.Diagnostics.Any() ? metadata : null;
	}

	/// <summary>
	/// Parses WithPreImage, WithPostImage, or AddImage call to extract image metadata.
	/// </summary>
	private static ImageMetadata ParseImageInvocation(
		InvocationExpressionSyntax imageInvocation,
		ITypeSymbol entityType,
		bool nullableEnabled)
	{
		if (imageInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
			return null;

		// Get method name - handle both generic (AddPreImage<T>) and non-generic (AddPreImage)
		string methodName;
		bool isGenericMethod = false;
		if (memberAccess.Name is GenericNameSyntax genericName)
		{
			methodName = genericName.Identifier.Text;
			isGenericMethod = true;
		}
		else if (memberAccess.Name is IdentifierNameSyntax identifierName)
		{
			methodName = identifierName.Identifier.Text;
		}
		else
		{
			return null;
		}

		var imageMetadata = new ImageMetadata();
		var arguments = imageInvocation.ArgumentList.Arguments;
		int attributeStartIndex = 0;

		// Determine image type and starting index for attributes
		if (methodName == Constants.AddImageMethodName)
		{
			// Old API: AddImage(ImageType.PreImage, "name", attr1, attr2, ...)
			if (arguments.Count > 0)
			{
				var imageTypeArg = arguments[0].Expression;
				imageMetadata.ImageType = ExtractEnumValue(imageTypeArg);

				// Skip first argument (ImageType), process remaining
				attributeStartIndex = 1;
			}
		}
		else if (methodName == Constants.WithPreImageMethodName)
		{
			// New API: WithPreImage(x => x.Name, ...)
			imageMetadata.ImageType = Constants.PreImageTypeName;
			attributeStartIndex = 0;
		}
		else if (methodName == Constants.WithPostImageMethodName)
		{
			// New API: WithPostImage(x => x.Name, ...)
			imageMetadata.ImageType = Constants.PostImageTypeName;
			attributeStartIndex = 0;
		}

		// For WithPreImage/WithPostImage, all arguments are attributes
		// For AddImage, first string after ImageType might be image name
		bool allArgumentsAreAttributes = isGenericMethod ||
			methodName == Constants.WithPreImageMethodName || methodName == Constants.WithPostImageMethodName;

		// Process arguments starting from attributeStartIndex
		for (int i = attributeStartIndex; i < arguments.Count; i++)
		{
			var argument = arguments[i];

			// Try to extract from nameof expression
			string value = SyntaxHelper.GetPropertyNameFromNameof(argument.Expression);

			// Try to extract from string literal
			if (value is null && argument.Expression is LiteralExpressionSyntax literal)
			{
				value = literal.Token.ValueText;
			}

			// Try to extract from lambda
			if (value is null && argument.Expression is LambdaExpressionSyntax lambda)
			{
				value = SyntaxHelper.GetPropertyNameFromLambda(lambda);
			}

			if (value is not null)
			{
				// Lambdas are always attributes, never image names
				// String literals in old AddImage API: first one might be image name
				bool isLambda = argument.Expression is LambdaExpressionSyntax;
				bool treatAsAttribute = allArgumentsAreAttributes || isLambda || !string.IsNullOrEmpty(imageMetadata.ImageName);

				if (treatAsAttribute)
				{
					// This is an attribute
					var attrMetadata = GetAttributeMetadata(value, entityType, nullableEnabled);
					if (attrMetadata != null)
					{
						imageMetadata.Attributes.Add(attrMetadata);
					}
				}
				else
				{
					// Old AddImage API: first string literal is image name
					imageMetadata.ImageName = value;
				}
			}
		}

		// Default image name if not provided
		if (string.IsNullOrEmpty(imageMetadata.ImageName))
		{
			imageMetadata.ImageName = imageMetadata.ImageType;
		}

		// For WithPreImage/WithPostImage with no attributes specified, capture all entity attributes
		if (!imageMetadata.Attributes.Any() &&
			(methodName == Constants.WithPreImageMethodName || methodName == Constants.WithPostImageMethodName))
		{
			imageMetadata.Attributes.AddRange(GetAllEntityAttributes(entityType, nullableEnabled));
		}

		return imageMetadata.Attributes.Any() ? imageMetadata : null;
	}

	/// <summary>
	/// Gets all attribute metadata for all entity properties that have an AttributeLogicalName attribute.
	/// Used for full entity images where no specific attributes are specified.
	/// </summary>
	private static IEnumerable<AttributeMetadata> GetAllEntityAttributes(ITypeSymbol entityType, bool nullableEnabled)
	{
		return entityType.GetMembers()
			.OfType<IPropertySymbol>()
			.Where(p => p.GetAttributes().Any(a => a.AttributeClass?.Name == Constants.LogicalNameAttributeName))
			.Select(p => GetAttributeMetadata(p.Name, entityType, nullableEnabled))
			.Where(a => a != null);
	}

	/// <summary>
	/// Gets attribute metadata (property name, logical name, type) for a property
	/// </summary>
	private static AttributeMetadata GetAttributeMetadata(
		string propertyName,
		ITypeSymbol entityType,
		bool nullableEnabled)
	{
		// Find the property in the entity type
		var property = entityType.GetMembers(propertyName)
			.OfType<IPropertySymbol>()
			.FirstOrDefault();

		if (property == null)
			return null;

		// Get the logical name from AttributeLogicalName attribute if present
		var logicalName = GetLogicalNameFromAttribute(property) ?? propertyName.ToLowerInvariant();

		// Get XML documentation from the property
		var xmlDoc = GetFormattedXmlDocumentation(property);

		var attrMetadata = new AttributeMetadata
		{
			PropertyName = propertyName,
			LogicalName = logicalName,
			// Strip nullable-reference '?' when the consumer hasn't enabled NRT, so generated code stays
			// valid on NRT-off / C# 7.3 consumers. Nullable value-type '?' is preserved.
			TypeName = NullableHelper.DisplayType(property.Type, nullableEnabled),
			XmlDocumentation = xmlDoc
		};

		PopulateObsoleteInfo(attrMetadata, property);

		return attrMetadata;
	}

	/// <summary>
	/// Detects whether the property is decorated with <see cref="System.ObsoleteAttribute"/> and, if so,
	/// captures its message and error flag so the generated wrapper property can mirror it. This pushes
	/// the deprecation warning (CS0612/CS0618) to the calling code instead of emitting it from the
	/// auto-generated image class.
	/// </summary>
	private static void PopulateObsoleteInfo(AttributeMetadata attrMetadata, IPropertySymbol property)
	{
		var obsoleteAttribute = property.GetAttributes()
			.FirstOrDefault(a => a.AttributeClass?.Name == "ObsoleteAttribute"
				&& a.AttributeClass.ContainingNamespace?.ToDisplayString() == "System");

		if (obsoleteAttribute == null)
			return;

		attrMetadata.IsObsolete = true;

		var ctorArgs = obsoleteAttribute.ConstructorArguments;
		if (ctorArgs.Length > 0 && ctorArgs[0].Value is string message)
		{
			attrMetadata.ObsoleteMessage = message;
		}
		if (ctorArgs.Length > 1 && ctorArgs[1].Value is bool isError)
		{
			attrMetadata.ObsoleteIsError = isError;
		}
	}

	/// <summary>
	/// Extracts and formats XML documentation from a property symbol into /// comment format.
	/// </summary>
	private static string GetFormattedXmlDocumentation(IPropertySymbol property)
	{
		var xmlComment = property.GetDocumentationCommentXml();
		if (string.IsNullOrWhiteSpace(xmlComment))
			return null;

		try
		{
			var doc = System.Xml.Linq.XDocument.Parse(xmlComment);
			var member = doc.Root; // <member> element
			if (member == null)
				return null;

			// Get inner XML (summary, remarks, etc.) and format as /// comments
			var innerXml = string.Concat(member.Nodes());
			if (string.IsNullOrWhiteSpace(innerXml))
				return null;

			// Split into lines, trim, and add /// prefix with member-level indentation
			var lines = innerXml
				.Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries)
				.Select(line => line.Trim())
				.Where(line => !string.IsNullOrEmpty(line))
				.Select(line => $"{Indent.L2}/// {line}");

			var result = string.Join("\n", lines);
			return string.IsNullOrWhiteSpace(result) ? null : result;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Extracts the logical name from [AttributeLogicalName("name")] attribute
	/// </summary>
	private static string GetLogicalNameFromAttribute(IPropertySymbol property)
	{
		var attribute = property.GetAttributes()
			.FirstOrDefault(a => a.AttributeClass?.Name == Constants.LogicalNameAttributeName);

		if (attribute?.ConstructorArguments.Length > 0)
		{
			return attribute.ConstructorArguments[0].Value?.ToString();
		}

		return null;
	}

	/// <summary>
	/// Extracts enum value name from expression
	/// </summary>
	private static string ExtractEnumValue(ExpressionSyntax expression)
	{
		// Handle direct enum access like EventOperation.Update
		if (expression is MemberAccessExpressionSyntax memberAccess)
		{
			return memberAccess.Name.Identifier.Text;
		}

		// Handle string literal for custom messages
		if (expression is LiteralExpressionSyntax literal)
		{
			return literal.Token.ValueText;
		}

		return "Unknown";
	}
}

/// <summary>
/// Extension methods for syntax nodes
/// </summary>
internal static class SyntaxExtensions
{
	public static string GetNamespace(this SyntaxNode node)
	{
		var namespaces = new List<string>();

		while (node != null)
		{
			if (node is NamespaceDeclarationSyntax namespaceDecl)
			{
				namespaces.Add(namespaceDecl.Name.ToString());
			}
			else if (node is FileScopedNamespaceDeclarationSyntax fileScopedNs)
			{
				namespaces.Add(fileScopedNs.Name.ToString());
			}
			node = node.Parent;
		}

		// No namespace declaration: emit generated types under a literal "GlobalNamespace". The runtime
		// mirrors this fallback for wrapper discovery (Plugin.GlobalNamespaceFallback) - keep them in sync.
		if (namespaces.Count == 0)
			return "GlobalNamespace";

		// Reverse to get outer-to-inner order, then join
		namespaces.Reverse();
		return string.Join(".", namespaces);
	}
}
