using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
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
		context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
	}

	private void AnalyzeClass(SyntaxNodeAnalysisContext context)
	{
		var classDeclaration = (ClassDeclarationSyntax)context.Node;
		var semanticModel = context.SemanticModel;

		// Key: "{entity}|{operation}|{stage}" -> already seen. Report on the 2nd+ occurrence.
		var seen = new HashSet<string>();

		foreach (var invocation in classDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			// Scope strictly to this class so registrations in a nested class aren't attributed here
			// (nested classes are analyzed by their own ClassDeclaration action).
			if (invocation.FirstAncestorOrSelf<ClassDeclarationSyntax>() != classDeclaration)
			{
				continue;
			}

			if (!RegisterStepHelper.IsStepRegistrationCall(invocation, out var genericName) ||
				genericName.TypeArgumentList.Arguments.Count < 1)
			{
				continue;
			}

			var entity = semanticModel.GetTypeInfo(genericName.TypeArgumentList.Arguments[0]).Type?.Name;

			var (operationExpr, stageExpr) = RegisterStepHelper.GetOperationAndStageArguments(invocation, semanticModel);
			if (entity == null || operationExpr == null || stageExpr == null)
			{
				continue;
			}

			var operation = RegisterStepHelper.ExtractEnumValue(operationExpr);
			var stage = RegisterStepHelper.ExtractEnumValue(stageExpr);

			// Skip registrations whose tuple can't be resolved to concrete values: the generator can't
			// derive a stable name from them either, so keying them would risk false positives.
			if (operation == "Unknown" || stage == "Unknown")
			{
				continue;
			}

			var key = $"{entity}|{operation}|{stage}";
			if (!seen.Add(key))
			{
				context.ReportDiagnostic(Diagnostic.Create(
					DiagnosticDescriptors.DuplicateStepRegistration,
					(RegisterStepHelper.GetGenericName(invocation) ?? (SyntaxNode)invocation).GetLocation(),
					classDeclaration.Identifier.Text,
					entity,
					operation,
					stage));
			}
		}
	}
}
