using XrmPluginCore.Tests.Context.BusinessDomain;

namespace XrmPluginCore.SourceGenerator.Tests.Helpers;

/// <summary>
/// Provides reusable test fixtures for source generator testing.
/// </summary>
public static class TestFixtures
{
	/// <summary>
	/// Plugin with PreImage only.
	/// </summary>
	public static string GetPluginWithPreImage(string entityClass = "Account")
	{
		List<string> imageProperties = [];
		if (entityClass == "Account")
		{
			imageProperties.AddRange([
				nameof(Account.Name),
				nameof(Account.Revenue),
				nameof(Account.IndustryCode)
			]);
		}
		else
		{
			imageProperties.AddRange([
				nameof(Contact.FirstName),
				nameof(Contact.EMailAddress1),
				nameof(Contact.AccountId)
			]);
		}

		var preImageProps = $"{string.Join(", ", imageProperties.Select(i => $"x => x.{i}"))}";
		var filtered = entityClass == "Account" ? nameof(Account.Name) : nameof(Contact.FirstName);

		return $@"
using XrmPluginCore;
using XrmPluginCore.Abstractions;
using XrmPluginCore.Enums;
using Microsoft.Extensions.DependencyInjection;
using TestNamespace;

namespace TestNamespace
{{
    public class TestPlugin : Plugin
    {{
        public TestPlugin()
        {{
            RegisterStep<{entityClass}, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
                nameof(ITestService.HandleAccountUpdate))
                .AddFilteredAttributes(x => x.{filtered})
                .WithPreImage({preImageProps});
        }}

        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
        {{
            return services.AddScoped<ITestService, TestService>();
        }}
    }}

    public interface ITestService
    {{
        void HandleAccountUpdate(TestPlugin{entityClass}UpdatePostOperationPreImage preImage);
    }}

    public class TestService : ITestService
    {{
        public void HandleAccountUpdate(TestPlugin{entityClass}UpdatePostOperationPreImage preImage) {{ }}
    }}
}}";
	}

	/// <summary>
	/// Plugin with PostImage.
	/// </summary>
	public const string PluginWithPostImage = """

		using XrmPluginCore;
		using XrmPluginCore.Abstractions;
		using XrmPluginCore.Enums;
		using Microsoft.Extensions.DependencyInjection;
		using TestNamespace;

		namespace TestNamespace
		{
		    public class TestPlugin : Plugin
		    {
		        public TestPlugin()
		        {
		            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
		                nameof(ITestService.HandleAccountUpdate))
		                .AddFilteredAttributes(x => x.Name)
		                .WithPostImage(x => x.Name, x => x.AccountNumber);
		        }

		        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
		        {
		            return services.AddScoped<ITestService, TestService>();
		        }
		    }

		    public interface ITestService
		    {
		        void HandleAccountUpdate(TestPluginAccountUpdatePostOperationPostImage postImage);
		    }

		    public class TestService : ITestService
		    {
		        public void HandleAccountUpdate(TestPluginAccountUpdatePostOperationPostImage postImage) { }
		    }
		}
		""";

	/// <summary>
	/// Plugin with both PreImage and PostImage using WithPreImage and WithPostImage.
	/// </summary>
	public const string PluginWithBothImages = """

		using XrmPluginCore;
		using XrmPluginCore.Abstractions;
		using XrmPluginCore.Enums;
		using Microsoft.Extensions.DependencyInjection;
		using TestNamespace;

		namespace TestNamespace
		{
		    public class TestPlugin : Plugin
		    {
		        public TestPlugin()
		        {
		            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
		                nameof(ITestService.HandleAccountUpdate))
		                .AddFilteredAttributes(x => x.Name)
		                .WithPreImage(x => x.Name, x => x.Revenue)
		                .WithPostImage(x => x.Name, x => x.AccountNumber);
		        }

		        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
		        {
		            return services.AddScoped<ITestService, TestService>();
		        }
		    }

		    public interface ITestService
		    {
		        void HandleAccountUpdate(TestPluginAccountUpdatePostOperationPreImage preImage, TestPluginAccountUpdatePostOperationPostImage postImage);
		    }

		    public class TestService : ITestService
		    {
		        public void HandleAccountUpdate(TestPluginAccountUpdatePostOperationPreImage preImage, TestPluginAccountUpdatePostOperationPostImage postImage) { }
		    }
		}
		""";

	/// <summary>
	/// Plugin with handler method reference but without any images.
	/// Tests that ActionWrapper is generated even when no images are registered.
	/// </summary>
	public static string GetPluginWithoutImages(string action = "nameof(ITestService.HandleUpdate)") =>
		$$"""
				using XrmPluginCore;
				using XrmPluginCore.Abstractions;
				using XrmPluginCore.Enums;
				using Microsoft.Extensions.DependencyInjection;
				using TestNamespace;

				namespace TestNamespace
				{
				    public class TestPlugin : Plugin
				    {
				        public TestPlugin()
				        {
				            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
				                {{action}})
				                .AddFilteredAttributes(x => x.Name);
				        }

				        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
				        {
				            return services.AddScoped<ITestService, TestService>();
				        }
				    }

				    public interface ITestService
				    {
				        void HandleUpdate();
				    }

				    public class TestService : ITestService
				    {
				        public void HandleUpdate() { }
				    }
				}
		""";

	/// <summary>
	/// Plugin with full entity PreImage (no attributes specified — captures all entity attributes).
	/// </summary>
	public const string PluginWithFullEntityPreImage = """

		using XrmPluginCore;
		using XrmPluginCore.Abstractions;
		using XrmPluginCore.Enums;
		using Microsoft.Extensions.DependencyInjection;
		using TestNamespace;

		namespace TestNamespace
		{
		    public class TestPlugin : Plugin
		    {
		        public TestPlugin()
		        {
		            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
		                nameof(ITestService.HandleAccountUpdate))
		                .AddFilteredAttributes(x => x.Name)
		                .WithPreImage();
		        }

		        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
		        {
		            return services.AddScoped<ITestService, TestService>();
		        }
		    }

		    public interface ITestService
		    {
		        void HandleAccountUpdate(TestPluginAccountUpdatePostOperationPreImage preImage);
		    }

		    public class TestService : ITestService
		    {
		        public void HandleAccountUpdate(TestPluginAccountUpdatePostOperationPreImage preImage) { }
		    }
		}
		""";

	/// <summary>
	/// Plugin with full entity PostImage (no attributes specified — captures all entity attributes).
	/// </summary>
	public const string PluginWithFullEntityPostImage = """

		using XrmPluginCore;
		using XrmPluginCore.Abstractions;
		using XrmPluginCore.Enums;
		using Microsoft.Extensions.DependencyInjection;
		using TestNamespace;

		namespace TestNamespace
		{
		    public class TestPlugin : Plugin
		    {
		        public TestPlugin()
		        {
		            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
		                nameof(ITestService.HandleAccountUpdate))
		                .AddFilteredAttributes(x => x.Name)
		                .WithPostImage();
		        }

		        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
		        {
		            return services.AddScoped<ITestService, TestService>();
		        }
		    }

		    public interface ITestService
		    {
		        void HandleAccountUpdate(TestPluginAccountUpdatePostOperationPostImage postImage);
		    }

		    public class TestService : ITestService
		    {
		        public void HandleAccountUpdate(TestPluginAccountUpdatePostOperationPostImage postImage) { }
		    }
		}
		""";

	/// <summary>
	/// Plugin whose PreImage registers a mix of normal and deprecated ([Obsolete]) attributes.
	/// The handler does NOT access the deprecated attribute, so any deprecation warning must
	/// originate from calling code — never from the generated image class.
	/// </summary>
	public const string PluginWithObsoleteImageAttributes = """

		using XrmPluginCore;
		using XrmPluginCore.Abstractions;
		using XrmPluginCore.Enums;
		using Microsoft.Extensions.DependencyInjection;
		using TestNamespace;

		namespace TestNamespace
		{
		    public class TestPlugin : Plugin
		    {
		        public TestPlugin()
		        {
		            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
		                nameof(ITestService.HandleAccountUpdate))
		                .AddFilteredAttributes(x => x.Name)
		                .WithPreImage(x => x.Name, x => x.ctx_DeprecatedField);
		        }

		        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
		        {
		            return services.AddScoped<ITestService, TestService>();
		        }
		    }

		    public interface ITestService
		    {
		        void HandleAccountUpdate(TestPluginAccountUpdatePostOperationPreImage preImage);
		    }

		    public class TestService : ITestService
		    {
		        public void HandleAccountUpdate(TestPluginAccountUpdatePostOperationPreImage preImage)
		        {
		            // Only touches a non-deprecated member.
		            var name = preImage.Name;
		        }
		    }
		}
		""";

	/// <summary>
	/// Plugin whose handler reads a deprecated ([Obsolete]) image attribute, so the deprecation
	/// warning is expected to surface here, in the calling code.
	/// </summary>
	public const string PluginAccessingObsoleteImageAttribute = """

		using XrmPluginCore;
		using XrmPluginCore.Abstractions;
		using XrmPluginCore.Enums;
		using Microsoft.Extensions.DependencyInjection;
		using TestNamespace;

		namespace TestNamespace
		{
		    public class TestPlugin : Plugin
		    {
		        public TestPlugin()
		        {
		            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
		                nameof(ITestService.HandleAccountUpdate))
		                .AddFilteredAttributes(x => x.Name)
		                .WithPreImage(x => x.Name, x => x.ctx_DeprecatedField);
		        }

		        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
		        {
		            return services.AddScoped<ITestService, TestService>();
		        }
		    }

		    public interface ITestService
		    {
		        void HandleAccountUpdate(TestPluginAccountUpdatePostOperationPreImage preImage);
		    }

		    public class TestService : ITestService
		    {
		        public void HandleAccountUpdate(TestPluginAccountUpdatePostOperationPreImage preImage)
		        {
		            // Accessing the deprecated member here SHOULD raise CS0612 in this calling code.
		            var legacy = preImage.ctx_DeprecatedField;
		        }
		    }
		}
		""";

	/// <summary>
	/// Plugin using old AddImage API for backward compatibility testing.
	/// </summary>
	public const string PluginWithLegacyAddImage =
		"""
		using XrmPluginCore;
		using XrmPluginCore.Abstractions;
		using Microsoft.Extensions.DependencyInjection;
		using TestNamespace;

		namespace TestNamespace
		{
		    public class TestPlugin : Plugin
		    {
		        public TestPlugin()
		        {
		            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation, service => service.HandleAccountUpdate())
		                .AddFilteredAttributes(x => x.Name)
		                .AddImage(ImageType.PreImage, x => x.Name, x => x.Revenue);
		        }

		        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
		        {
		            return services.AddScoped<ITestService, TestService>();
		        }
		    }

		    public interface ITestService
		    {
		        void HandleAccountUpdate();
		    }

		    public class TestService : ITestService
		    {
		        public void HandleAccountUpdate() { }
		    }
		}
		""";

	/// <summary>
	/// A type-safe Custom API plugin and service. The Request/Response types referenced by the handler
	/// are emitted by the CustomApiGenerator; this fixture only needs to declare the registration and a
	/// matching handler. Use <paramref name="withRequest"/>/<paramref name="withResponse"/> to exercise
	/// the signature-adaptation paths.
	/// </summary>
	public static string GetCustomApiPlugin(bool withRequest = true, bool withResponse = true)
	{
		var requestChain = withRequest
			? """
			            .AddRequestParameter("EntityLogicalName", CustomApiParameterType.String)
			            .AddRequestParameter("EntityId", CustomApiParameterType.Guid)
			            .AddRequestParameter("Count", CustomApiParameterType.Integer, isOptional: true)
			"""
			: string.Empty;

		var responseChain = withResponse
			? """
			            .AddResponseProperty("StatusCode", CustomApiParameterType.Integer)
			            .AddResponseProperty("ErrorMessage", CustomApiParameterType.String)
			"""
			: string.Empty;

		var returnType = withResponse ? "SomeApiResponse" : "void";
		var parameter = withRequest ? "SomeApiRequest request" : string.Empty;
		var body = withResponse ? "=> new SomeApiResponse(0, string.Empty);" : "{ }";

		return $$"""
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
			            RegisterAPI<CallbackService>(nameof(SomeApi), nameof(CallbackService.Handle))
			{{requestChain}}{{responseChain}}
			                ;
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<CallbackService>();
			        }
			    }

			    public class CallbackService
			    {
			        public {{returnType}} Handle({{parameter}}) {{body}}
			    }
			}
			""";
	}

	/// <summary>
	/// Gets a complete compilable source with entity and plugin.
	/// </summary>
	public static string GetCompleteSource(string pluginSource)
	{
		return $$"""
			using System;
			using System.ComponentModel;
			using Microsoft.Xrm.Sdk;
			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using XrmPluginCore.Tests.Context.BusinessDomain;

			namespace TestNamespace
			{
				{{StripNamespaceAndUsings(pluginSource)}}
			}
			""";
	}

	/// <summary>
	/// Removes namespace declaration and using statements from source code.
	/// </summary>
	private static string StripNamespaceAndUsings(string source)
	{
		var lines = source.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
		var result = new System.Text.StringBuilder();
		bool inNamespace = false;
		int braceCount = 0;

		foreach (var line in lines)
		{
			var trimmed = line.Trim();

			// Skip using statements
			if (trimmed.StartsWith("using "))
				continue;

			// Skip namespace declaration
			if (trimmed.StartsWith("namespace "))
			{
				inNamespace = true;
				continue;
			}

			// Skip opening brace of namespace
			if (inNamespace && trimmed == "{")
			{
				inNamespace = false;
				braceCount++;
				continue;
			}

			// Track braces
			braceCount += line.Count(c => c == '{');
			braceCount -= line.Count(c => c == '}');

			// Skip closing brace if it would close the namespace
			if (braceCount == 0 && trimmed == "}")
				continue;

			result.AppendLine(line);
		}

		return result.ToString();
	}
}
