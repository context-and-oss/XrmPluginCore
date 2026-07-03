using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace XrmPluginCore.SourceGenerator.Helpers;

/// <summary>
/// Shared utilities for analyzing type-safe Custom API registrations
/// (<c>RegisterAPI&lt;TService&gt;(name, handlerMethodName)</c>) and their fluent
/// <c>AddRequestParameter</c>/<c>AddResponseProperty</c> chains. Mirrors <see cref="RegisterStepHelper"/>.
/// </summary>
internal static class RegisterApiHelper
{
	/// <summary>
	/// Checks if an invocation is a generic <c>RegisterAPI&lt;TService&gt;(...)</c> call and extracts the generic name.
	/// </summary>
	public static bool IsRegisterApiCall(InvocationExpressionSyntax invocation, out GenericNameSyntax genericName)
	{
		genericName = GetGenericName(invocation);
		return genericName != null && genericName.Identifier.Text == Constants.RegisterApiMethodName;
	}

	/// <summary>
	/// Gets the GenericNameSyntax from a RegisterAPI call (handles <c>this.RegisterAPI</c> and bare forms).
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
	/// Determines whether the invocation binds to the typed handler-name overload
	/// <c>RegisterAPI&lt;TService&gt;(string name, string handlerMethodName)</c> (vs the action overloads).
	/// </summary>
	public static bool IsTypedHandlerOverload(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
	{
		var method = ResolveMethod(invocation, semanticModel);
		return method != null
			&& method.Parameters.Length == 2
			&& method.Parameters[1].Type.SpecialType == SpecialType.System_String;
	}

	/// <summary>
	/// Determines whether the invocation binds to a generic action overload
	/// <c>RegisterAPI&lt;TService&gt;(string name, Action&lt;TService&gt; action)</c> (vs the typed handler-name
	/// overload). This is the overload that does NOT generate request/response DTOs.
	/// </summary>
	public static bool IsActionOverload(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
	{
		var method = ResolveMethod(invocation, semanticModel);
		return method != null
			&& method.Parameters.Length == 2
			&& method.Parameters[1].Type.TypeKind == TypeKind.Delegate;
	}

	/// <summary>
	/// Returns the <c>name</c> argument expression, resolved by parameter name (so named/reordered
	/// arguments are honored). Returns null when absent.
	/// </summary>
	public static ExpressionSyntax GetNameArgument(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
		=> ArgumentBinder.GetArgument(invocation, semanticModel, Constants.ParameterName);

	/// <summary>
	/// Returns the <c>action</c> argument expression (the delegate/lambda), resolved by parameter name.
	/// Returns null when absent.
	/// </summary>
	public static ExpressionSyntax GetActionArgument(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
		=> ArgumentBinder.GetArgument(invocation, semanticModel, Constants.ParameterAction);

	/// <summary>
	/// Extracts the handler method name from an action lambda such as <c>s =&gt; s.Handle()</c> or
	/// <c>s =&gt; s.Handle</c>, but only when the member is accessed directly on the lambda parameter.
	/// Returns null for statement blocks, more complex expressions, or calls whose receiver is not the
	/// lambda parameter (e.g. <c>s =&gt; Other.Handle()</c> or <c>s =&gt; s.Foo.Handle()</c>), so the
	/// code fix never offers a misleading <c>nameof(TService.X)</c> for a method that isn't on TService.
	/// </summary>
	public static string GetActionMethodName(ExpressionSyntax actionArgument)
	{
		string parameterName;
		ExpressionSyntax body;
		switch (actionArgument)
		{
			case SimpleLambdaExpressionSyntax simpleLambda:
				parameterName = simpleLambda.Parameter.Identifier.Text;
				body = simpleLambda.Body as ExpressionSyntax;
				break;
			case ParenthesizedLambdaExpressionSyntax parenLambda when parenLambda.ParameterList.Parameters.Count == 1:
				parameterName = parenLambda.ParameterList.Parameters[0].Identifier.Text;
				body = parenLambda.Body as ExpressionSyntax;
				break;
			default:
				return null;
		}

		// Unwrap an invocation body (s => s.Handle()) to its member access; a bare member access body
		// (s => s.Handle, a method group) is used directly.
		var memberAccess = body switch
		{
			InvocationExpressionSyntax invocation => invocation.Expression as MemberAccessExpressionSyntax,
			MemberAccessExpressionSyntax bareMember => bareMember,
			_ => null,
		};

		// Only accept when the receiver is exactly the lambda parameter (s.Handle), not a different
		// target (Other.Handle) or a nested access (s.Foo.Handle).
		if (memberAccess?.Expression is IdentifierNameSyntax receiver &&
			receiver.Identifier.Text == parameterName)
		{
			return memberAccess.Name.Identifier.Text;
		}

		return null;
	}

	/// <summary>
	/// Returns the <c>handlerMethodName</c> argument expression, resolved by parameter name. Returns null
	/// when absent.
	/// </summary>
	public static ExpressionSyntax GetHandlerArgument(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
		=> ArgumentBinder.GetArgument(invocation, semanticModel, Constants.ParameterHandlerMethodName);

	/// <summary>
	/// Resolves the <c>name</c> argument as a compile-time constant string. Returns null when it is not a
	/// constant.
	/// </summary>
	public static string GetApiName(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
	{
		var nameArgument = GetNameArgument(invocation, semanticModel);
		if (nameArgument == null)
		{
			return null;
		}

		var constant = semanticModel.GetConstantValue(nameArgument);
		return constant.HasValue ? constant.Value as string : null;
	}

	/// <summary>
	/// Extracts the handler method name from the <c>handlerMethodName</c> argument (nameof()/string literal).
	/// </summary>
	public static string GetHandlerMethodName(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
	{
		var handlerArgument = GetHandlerArgument(invocation, semanticModel);
		return handlerArgument == null ? null : RegisterStepHelper.GetMethodName(handlerArgument);
	}

	/// <summary>
	/// Resolves the service type (the single generic argument) of a RegisterAPI call.
	/// </summary>
	public static ITypeSymbol GetServiceType(GenericNameSyntax genericName, SemanticModel semanticModel)
	{
		if (genericName == null || genericName.TypeArgumentList.Arguments.Count < 1)
		{
			return null;
		}

		return semanticModel.GetTypeInfo(genericName.TypeArgumentList.Arguments[0]).Type;
	}

	/// <summary>
	/// Finds all AddRequestParameter/AddResponseProperty invocations chained to a RegisterAPI call,
	/// in source order.
	/// </summary>
	public static IEnumerable<InvocationExpressionSyntax> FindParameterInvocations(InvocationExpressionSyntax registerApiInvocation)
	{
		var parent = registerApiInvocation.Parent;
		while (parent != null)
		{
			if (parent is InvocationExpressionSyntax invocation &&
				invocation.Expression is MemberAccessExpressionSyntax memberAccess)
			{
				var methodName = memberAccess.Name.Identifier.Text;
				if (methodName == Constants.AddRequestParameterMethodName ||
					methodName == Constants.AddResponsePropertyMethodName)
				{
					yield return invocation;
				}
			}
			parent = parent.Parent;
		}
	}

	/// <summary>
	/// Counts the request parameters and response properties declared in the fluent chain.
	/// </summary>
	public static (bool hasRequest, bool hasResponse) CheckForParameters(InvocationExpressionSyntax registerApiInvocation)
	{
		var hasRequest = false;
		var hasResponse = false;

		foreach (var invocation in FindParameterInvocations(registerApiInvocation))
		{
			var methodName = ((MemberAccessExpressionSyntax)invocation.Expression).Name.Identifier.Text;
			if (methodName == Constants.AddRequestParameterMethodName)
			{
				hasRequest = true;
			}
			else if (methodName == Constants.AddResponsePropertyMethodName)
			{
				hasResponse = true;
			}
		}

		return (hasRequest, hasResponse);
	}

	private static IMethodSymbol ResolveMethod(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
	{
		var symbolInfo = semanticModel.GetSymbolInfo(invocation);
		return symbolInfo.Symbol as IMethodSymbol
			?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
	}
}
