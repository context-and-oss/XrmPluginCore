using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using XrmPluginCore.SourceGenerator.Analyzers;
using XrmPluginCore.SourceGenerator.CodeFixes;
using XrmPluginCore.SourceGenerator.Tests.Helpers;
using Xunit;

namespace XrmPluginCore.SourceGenerator.Tests.DiagnosticTests;

/// <summary>
/// Tests for the type-safe Custom API analyzers (XPC4004/XPC4005/XPC4006, XPC3001) and their code fixers.
/// </summary>
public class CustomApiHandlerDiagnosticsTests : CodeFixTestBase
{
	private const string RegistrationWithParams = """
		            RegisterAPI<CallbackService>(nameof(SomeApi), nameof(CallbackService.Handle))
		                .AddRequestParameter("EntityId", CustomApiParameterType.Guid)
		                .AddResponseProperty("StatusCode", CustomApiParameterType.Integer);
		""";

	[Fact]
	public async Task Should_Report_XPC4004_When_Handler_Method_Missing()
	{
		var source = WrapPlugin(RegistrationWithParams, serviceBody: "// no methods");

		var diagnostics = await GetDiagnosticsAsync(source, new CustomApiHandlerMethodNotFoundAnalyzer());

		diagnostics.Should().ContainSingle(d => d.Id == "XPC4004");
		diagnostics.Single(d => d.Id == "XPC4004").Severity.Should().Be(DiagnosticSeverity.Error);
	}

	[Fact]
	public async Task Should_Report_XPC4005_When_Signature_Wrong_And_Types_Missing()
	{
		// Handler exists but has the wrong signature; the generated request/response types do not exist.
		var source = WrapPlugin(RegistrationWithParams, serviceBody: "public void Handle() { }");

		var diagnostics = await GetDiagnosticsAsync(source, new CustomApiHandlerSignatureMismatchAnalyzer());

		var diagnostic = diagnostics.Should().ContainSingle(d => d.Id == "XPC4005").Subject;
		diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
		diagnostic.GetMessage().Should().Contain("SomeApiResponse Handle(SomeApiRequest request)");
	}

	[Fact]
	public async Task Should_Report_XPC4006_When_Signature_Wrong_And_Types_Exist()
	{
		// The generated types exist in the compilation, so the mismatch escalates to an error.
		var source = WrapPlugin(RegistrationWithParams, serviceBody: "public void Handle() { }")
			+ GeneratedTypes;

		var diagnostics = await GetDiagnosticsAsync(source, new CustomApiHandlerSignatureMismatchAnalyzer());

		diagnostics.Should().ContainSingle(d => d.Id == "XPC4006")
			.Which.Severity.Should().Be(DiagnosticSeverity.Error);
	}

	[Fact]
	public async Task Should_Not_Report_When_Signature_Matches()
	{
		var source = WrapPlugin(RegistrationWithParams, serviceBody: "public SomeApiResponse Handle(SomeApiRequest request) => new SomeApiResponse(0);")
			+ GeneratedTypes;

		var diagnostics = await GetDiagnosticsAsync(source, new CustomApiHandlerSignatureMismatchAnalyzer());

		diagnostics.Should().NotContain(d => d.Id == "XPC4005" || d.Id == "XPC4006");
	}

	[Fact]
	public async Task Should_Report_Mismatch_When_Handler_Uses_Same_Named_Type_From_Different_Namespace()
	{
		// The handler's request/response types share the generated types' short names but live in a
		// different namespace, so they are NOT the generated types and must be reported as a mismatch.
		const string source = """
			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;

			namespace Other
			{
			    public sealed class SomeApiRequest { }
			    public sealed class SomeApiResponse { }
			}

			namespace TestNamespace
			{
			    public class SomeApi : Plugin
			    {
			        public SomeApi()
			        {
			            RegisterAPI<CallbackService>(nameof(SomeApi), nameof(CallbackService.Handle))
			                .AddRequestParameter("EntityId", CustomApiParameterType.Guid)
			                .AddResponseProperty("StatusCode", CustomApiParameterType.Integer);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			            => services.AddScoped<CallbackService>();
			    }

			    public class CallbackService
			    {
			        public Other.SomeApiResponse Handle(Other.SomeApiRequest request) => new Other.SomeApiResponse();
			    }
			}
			""";

		var diagnostics = await GetDiagnosticsAsync(source, new CustomApiHandlerSignatureMismatchAnalyzer());

		diagnostics.Should().Contain(d => d.Id == "XPC4005" || d.Id == "XPC4006");
	}

	[Fact]
	public async Task Should_Report_XPC3006_When_Api_Name_Is_Not_Constant()
	{
		const string source = """
			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;

			namespace TestNamespace
			{
			    public class SomeApi : Plugin
			    {
			        public SomeApi()
			        {
			            var name = System.Guid.NewGuid().ToString();
			            RegisterAPI<CallbackService>(name, nameof(CallbackService.Handle))
			                .AddResponseProperty("StatusCode", CustomApiParameterType.Integer);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			            => services.AddScoped<CallbackService>();
			    }

			    public class CallbackService
			    {
			        public object Handle() => null;
			    }
			}
			""";

		var diagnostics = await GetDiagnosticsAsync(source, new CustomApiNameNotConstantAnalyzer());

		diagnostics.Should().ContainSingle(d => d.Id == "XPC3006")
			.Which.Severity.Should().Be(DiagnosticSeverity.Warning);
	}

	[Fact]
	public async Task Should_Not_Report_XPC3006_When_Api_Name_Is_Nameof()
	{
		var source = WrapPlugin(RegistrationWithParams, serviceBody: "public SomeApiResponse Handle(SomeApiRequest request) => new SomeApiResponse(0);")
			+ GeneratedTypes;

		var diagnostics = await GetDiagnosticsAsync(source, new CustomApiNameNotConstantAnalyzer());

		diagnostics.Should().NotContain(d => d.Id == "XPC3006");
	}

	[Fact]
	public async Task Should_Report_XPC3001_For_String_Literal_Handler()
	{
		const string registration = """
			            RegisterAPI<CallbackService>(nameof(SomeApi), "Handle")
			                .AddResponseProperty("StatusCode", CustomApiParameterType.Integer);
			""";
		var source = WrapPlugin(registration, serviceBody: "public SomeApiResponse Handle() => new SomeApiResponse(0);")
			+ GeneratedTypes;

		var diagnostics = await GetDiagnosticsAsync(source, new PreferNameofAnalyzer());

		var diagnostic = diagnostics.Should().ContainSingle(d => d.Id == "XPC3001").Subject;
		diagnostic.Properties["ServiceType"].Should().Be("CallbackService");
		diagnostic.Properties["MethodName"].Should().Be("Handle");
	}

	[Fact]
	public async Task Should_Report_XPC3007_For_Action_Overload_With_Parameters()
	{
		const string registration = """
			            RegisterAPI<CallbackService>(nameof(SomeApi), s => s.Handle())
			                .AddRequestParameter("EntityId", CustomApiParameterType.Guid)
			                .AddResponseProperty("StatusCode", CustomApiParameterType.Integer);
			""";
		var source = WrapPlugin(registration, serviceBody: "public void Handle() { }");

		var diagnostics = await GetDiagnosticsAsync(source, new PreferTypedCustomApiHandlerAnalyzer());

		var diagnostic = diagnostics.Should().ContainSingle(d => d.Id == "XPC3007").Subject;
		diagnostic.Severity.Should().Be(DiagnosticSeverity.Info);
		diagnostic.Properties["ServiceType"].Should().Be("CallbackService");
		diagnostic.Properties["MethodName"].Should().Be("Handle");
	}

	[Fact]
	public async Task Should_Report_XPC3007_When_Only_Response_Declared()
	{
		const string registration = """
			            RegisterAPI<CallbackService>(nameof(SomeApi), s => s.Handle())
			                .AddResponseProperty("StatusCode", CustomApiParameterType.Integer);
			""";
		var source = WrapPlugin(registration, serviceBody: "public void Handle() { }");

		var diagnostics = await GetDiagnosticsAsync(source, new PreferTypedCustomApiHandlerAnalyzer());

		diagnostics.Should().ContainSingle(d => d.Id == "XPC3007");
	}

	[Fact]
	public async Task Should_Not_Report_XPC3007_When_No_Parameters_Declared()
	{
		const string registration = """
			            RegisterAPI<CallbackService>(nameof(SomeApi), s => s.Handle());
			""";
		var source = WrapPlugin(registration, serviceBody: "public void Handle() { }");

		var diagnostics = await GetDiagnosticsAsync(source, new PreferTypedCustomApiHandlerAnalyzer());

		diagnostics.Should().NotContain(d => d.Id == "XPC3007");
	}

	[Fact]
	public async Task Should_Not_Report_XPC3007_For_Typed_Handler_Overload()
	{
		var source = WrapPlugin(RegistrationWithParams, serviceBody: "public SomeApiResponse Handle(SomeApiRequest request) => new SomeApiResponse(0);")
			+ GeneratedTypes;

		var diagnostics = await GetDiagnosticsAsync(source, new PreferTypedCustomApiHandlerAnalyzer());

		diagnostics.Should().NotContain(d => d.Id == "XPC3007");
	}

	[Fact]
	public async Task Should_Report_XPC3007_Without_MethodName_When_Call_Is_Not_On_Lambda_Parameter()
	{
		// The lambda calls a method on a different receiver, not the service parameter, so no
		// nameof(CallbackService.X) can be safely suggested: report the diagnostic but offer no fix.
		const string source = """
			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;

			namespace TestNamespace
			{
			    public static class Other { public static void Handle() { } }

			    public class SomeApi : Plugin
			    {
			        public SomeApi()
			        {
			            RegisterAPI<CallbackService>(nameof(SomeApi), s => Other.Handle())
			                .AddResponseProperty("StatusCode", CustomApiParameterType.Integer);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			            => services.AddScoped<CallbackService>();
			    }

			    public class CallbackService
			    {
			        public void Handle() { }
			    }
			}
			""";

		var diagnostics = await GetDiagnosticsAsync(source, new PreferTypedCustomApiHandlerAnalyzer());

		var diagnostic = diagnostics.Should().ContainSingle(d => d.Id == "XPC3007").Subject;
		diagnostic.Properties.ContainsKey("MethodName").Should().BeFalse();
	}

	[Fact]
	public async Task Should_Fix_Action_Overload_With_Qualified_Service_Type()
	{
		// A namespace-qualified generic argument must produce valid nameof syntax (not a single
		// malformed identifier).
		const string registration = """
			            RegisterAPI<TestNamespace.CallbackService>(nameof(SomeApi), s => s.Handle())
			                .AddResponseProperty("StatusCode", CustomApiParameterType.Integer);
			""";
		var source = WrapPlugin(registration, serviceBody: "public void Handle() { }");

		var fixedSource = await ApplyCodeFixAsync(
			source,
			new PreferTypedCustomApiHandlerAnalyzer(),
			new PreferTypedCustomApiHandlerCodeFixProvider(),
			DiagnosticDescriptors.PreferTypedCustomApiHandler.Id);

		fixedSource.Should().Contain("nameof(TestNamespace.CallbackService.Handle)");
	}

	[Fact]
	public async Task Should_Fix_Action_Overload_To_Nameof()
	{
		const string registration = """
			            RegisterAPI<CallbackService>(nameof(SomeApi), s => s.Handle())
			                .AddRequestParameter("EntityId", CustomApiParameterType.Guid)
			                .AddResponseProperty("StatusCode", CustomApiParameterType.Integer);
			""";
		var source = WrapPlugin(registration, serviceBody: "public void Handle() { }");

		var fixedSource = await ApplyCodeFixAsync(
			source,
			new PreferTypedCustomApiHandlerAnalyzer(),
			new PreferTypedCustomApiHandlerCodeFixProvider(),
			DiagnosticDescriptors.PreferTypedCustomApiHandler.Id);

		fixedSource.Should().Contain("nameof(CallbackService.Handle)");
		fixedSource.Should().NotContain("s => s.Handle()");
	}

	[Fact]
	public async Task Should_Fix_Missing_Handler_Method()
	{
		var source = WrapPlugin(RegistrationWithParams, serviceBody: "// no methods");

		var fixedSource = await ApplyCodeFixAsync(
			source,
			new CustomApiHandlerMethodNotFoundAnalyzer(),
			new CreateCustomApiHandlerMethodCodeFixProvider(),
			DiagnosticDescriptors.CustomApiHandlerMethodNotFound.Id);

		fixedSource.Should().Contain("TestNamespace.SomeApiResponse Handle(TestNamespace.SomeApiRequest request)");
	}

	[Fact]
	public async Task Should_Fix_Wrong_Handler_Signature()
	{
		var source = WrapPlugin(RegistrationWithParams, serviceBody: "public void Handle() { }")
			+ GeneratedTypes;

		var fixedSource = await ApplyCodeFixAsync(
			source,
			new CustomApiHandlerSignatureMismatchAnalyzer(),
			new FixCustomApiHandlerSignatureCodeFixProvider(),
			DiagnosticDescriptors.CustomApiHandlerSignatureMismatch.Id,
			DiagnosticDescriptors.CustomApiHandlerSignatureMismatchError.Id);

		fixedSource.Should().Contain("Handle(TestNamespace.SomeApiRequest request)");
		fixedSource.Should().Contain("TestNamespace.SomeApiResponse Handle");
	}

	private const string GeneratedTypes = """


		namespace TestNamespace
		{
		    public sealed class SomeApiRequest { public System.Guid EntityId { get; set; } }
		    public sealed class SomeApiResponse
		    {
		        public int StatusCode { get; set; }
		        public SomeApiResponse(int statusCode) { StatusCode = statusCode; }
		    }
		}
		""";

	private static string WrapPlugin(string registration, string serviceBody) =>
		$$"""
			using System;
			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;

			namespace TestNamespace
			{
			    public class SomeApi : Plugin
			    {
			        public SomeApi()
			        {
			{{registration}}
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<CallbackService>();
			        }
			    }

			    public class CallbackService
			    {
			        {{serviceBody}}
			    }
			}
			""";

	private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source, DiagnosticAnalyzer analyzer)
	{
		var compilation = CompilationHelper.CreateCompilation(source);
		var compilationWithAnalyzers = compilation.WithAnalyzers([analyzer]);
		return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
	}
}
