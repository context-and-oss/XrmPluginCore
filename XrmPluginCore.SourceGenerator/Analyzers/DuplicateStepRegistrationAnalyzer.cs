using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using XrmPluginCore.SourceGenerator.Helpers;

namespace XrmPluginCore.SourceGenerator.Analyzers;

/// <summary>
/// Reports when a plugin class registers the same entity/operation/stage combination more than once
/// (via <c>RegisterStep</c> or the legacy <c>RegisterPluginStep</c>). Such a duplicate is rejected at
/// registration time and would make the source-generated image/wrapper type names — which are derived
/// from that tuple — ambiguous.
///
/// Registrations are collected per plugin type across every syntax tree (so <c>partial</c> plugins are
/// aggregated) and duplicates are evaluated at compilation end, where inherited base-plugin
/// registrations are also considered. Only types deriving from <c>XrmPluginCore.Plugin</c> are analyzed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class DuplicateStepRegistrationAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(DiagnosticDescriptors.DuplicateStepRegistration);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(OnCompilationStart);
	}

	private static void OnCompilationStart(CompilationStartAnalysisContext context)
	{
		// Accumulate registrations per plugin type across all trees (handles partial declarations).
		var registrationsByType = new ConcurrentDictionary<INamedTypeSymbol, ConcurrentBag<Registration>>(SymbolEqualityComparer.Default);

		context.RegisterSyntaxNodeAction(
			nodeContext => CollectRegistration(nodeContext, registrationsByType),
			SyntaxKind.InvocationExpression);

		context.RegisterCompilationEndAction(endContext => ReportDuplicates(endContext, registrationsByType));
	}

	private static void CollectRegistration(
		SyntaxNodeAnalysisContext context,
		ConcurrentDictionary<INamedTypeSymbol, ConcurrentBag<Registration>> registrationsByType)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;

		if (!RegisterStepHelper.IsStepRegistrationCall(invocation, out var genericName) ||
			genericName.TypeArgumentList.Arguments.Count < 1)
		{
			return;
		}

		var classDeclaration = invocation.FirstAncestorOrSelf<ClassDeclarationSyntax>();
		if (classDeclaration == null ||
			context.SemanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol typeSymbol ||
			!IsPluginClass(typeSymbol))
		{
			return;
		}

		var entity = context.SemanticModel.GetTypeInfo(genericName.TypeArgumentList.Arguments[0]).Type?.Name;

		var (operationExpr, stageExpr) = RegisterStepHelper.GetOperationAndStageArguments(invocation, context.SemanticModel);
		if (entity == null || operationExpr == null || stageExpr == null)
		{
			return;
		}

		var operation = ResolveTupleValue(operationExpr, context.SemanticModel);
		var stage = ResolveTupleValue(stageExpr, context.SemanticModel);

		// Skip registrations whose tuple can't be resolved to concrete values: the generator can't
		// derive a stable name from them either, so keying them would risk false positives.
		if (operation == null || stage == null)
		{
			return;
		}

		var location = (RegisterStepHelper.GetGenericName(invocation) ?? (SyntaxNode)invocation).GetLocation();
		registrationsByType
			.GetOrAdd(typeSymbol, _ => new ConcurrentBag<Registration>())
			.Add(new Registration(entity, operation, stage, location));
	}

	private static void ReportDuplicates(
		CompilationAnalysisContext context,
		ConcurrentDictionary<INamedTypeSymbol, ConcurrentBag<Registration>> registrationsByType)
	{
		foreach (var entry in registrationsByType)
		{
			var type = entry.Key;

			// Seed with registrations inherited from source-defined base plugins so a derived class that
			// re-registers an inherited tuple is caught. Base-internal duplicates are reported when the
			// base itself is evaluated, so they are not re-reported here.
			var seen = new HashSet<string>();
			for (var baseType = type.BaseType; baseType != null && !IsFrameworkPluginBase(baseType); baseType = baseType.BaseType)
			{
				if (registrationsByType.TryGetValue(baseType, out var baseRegistrations))
				{
					foreach (var registration in baseRegistrations)
					{
						seen.Add(registration.Key);
					}
				}
			}

			// Order for deterministic reporting (ConcurrentBag is unordered; declarations may span trees).
			var ordered = entry.Value
				.OrderBy(r => r.Location.SourceTree?.FilePath, System.StringComparer.Ordinal)
				.ThenBy(r => r.Location.SourceSpan.Start);

			foreach (var registration in ordered)
			{
				if (!seen.Add(registration.Key))
				{
					context.ReportDiagnostic(Diagnostic.Create(
						DiagnosticDescriptors.DuplicateStepRegistration,
						registration.Location,
						type.Name,
						registration.Entity,
						registration.Operation,
						registration.Stage));
				}
			}
		}
	}

	/// <summary>
	/// Resolves an operation/stage argument to its comparison value, matching what the runtime uses
	/// (<c>value.ToString()</c> for enums, the raw string for custom messages):
	/// <list type="bullet">
	/// <item>an enum member access (<c>EventOperation.Update</c>) yields the member name;</item>
	/// <item>a string literal or <c>const string</c> yields the constant string;</item>
	/// <item>a <c>const</c> enum reference (<c>const EventOperation Op = EventOperation.Update</c>) is
	/// mapped from its underlying constant value back to the enum member name.</item>
	/// </list>
	/// Returns null when the value can't be resolved to a concrete string.
	/// </summary>
	private static string ResolveTupleValue(ExpressionSyntax expression, SemanticModel semanticModel)
	{
		if (expression is MemberAccessExpressionSyntax memberAccess)
		{
			return memberAccess.Name.Identifier.Text;
		}

		var constant = semanticModel.GetConstantValue(expression);
		if (!constant.HasValue)
		{
			return null;
		}

		if (constant.Value is string stringValue)
		{
			return stringValue;
		}

		// A const enum reference: GetConstantValue gives the underlying integral value, so map it back to
		// the member name (matching EventOperation.Update.ToString()).
		if (semanticModel.GetTypeInfo(expression).Type is INamedTypeSymbol enumType &&
			enumType.TypeKind == TypeKind.Enum)
		{
			foreach (var field in enumType.GetMembers().OfType<IFieldSymbol>())
			{
				if (field.HasConstantValue && Equals(field.ConstantValue, constant.Value))
				{
					return field.Name;
				}
			}
		}

		return null;
	}

	private static bool IsPluginClass(INamedTypeSymbol type)
	{
		for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
		{
			if (IsFrameworkPluginBase(baseType))
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsFrameworkPluginBase(INamedTypeSymbol type) =>
		type.Name == Constants.PluginBaseClassName &&
		type.ContainingNamespace?.ToDisplayString() == Constants.PluginNamespace;

	private readonly struct Registration
	{
		public Registration(string entity, string operation, string stage, Location location)
		{
			Entity = entity;
			Operation = operation;
			Stage = stage;
			Location = location;
		}

		public string Entity { get; }
		public string Operation { get; }
		public string Stage { get; }
		public Location Location { get; }
		public string Key => $"{Entity}|{Operation}|{Stage}";
	}
}
