using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using XrmPluginCore.CustomApis;
using XrmPluginCore.Enums;
using XrmPluginCore.Extensions;
using XrmPluginCore.Helpers;
using XrmPluginCore.Interfaces.CustomApi;
using XrmPluginCore.Interfaces.Plugin;
using XrmPluginCore.Plugins;

namespace XrmPluginCore;

/// <summary>
/// Base class for all Plugins.
/// </summary>
public abstract class Plugin : IPlugin, IPluginDefinition, ICustomApiDefinition
{
	private string ChildClassName { get; }
	private string ChildClassShortName { get; }
	private string ChildClassNamespace { get; }
	private List<PluginStepRegistration> RegisteredPluginSteps { get; } = [];
	private CustomApiRegistration RegisteredCustomApi { get; set; }

	// The source generator places generated types under "GlobalNamespace" when the plugin class has no
	// namespace (see SyntaxExtensions.GetNamespace in the generator). Type.Namespace is null in that case,
	// so mirror the same fallback here for wrapper-type discovery to match. Keep the literal in sync.
	private const string GlobalNamespaceFallback = "GlobalNamespace";

	protected Plugin()
	{
		var type = GetType();
		ChildClassName = type.ToString();
		ChildClassShortName = type.Name;
		ChildClassNamespace = string.IsNullOrEmpty(type.Namespace) ? GlobalNamespaceFallback : type.Namespace;
	}

	/// <summary>
	/// Called to allow derived classes to modify the service collection before it is used
	/// </summary>
	protected virtual IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
	{
		return services;
	}

	/// <summary>
	/// Executes the plug-in.
	/// </summary>
	/// <param name="serviceProvider">The service provider.</param>
	/// <remarks>
	/// For improved performance, Microsoft Dynamics CRM caches plug-in instances.
	/// The plug-in's Execute method should be written to be stateless as the constructor
	/// is not called for every invocation of the plug-in. Also, multiple system threads
	/// could execute the plug-in at the same time. All per invocation state information
	/// is stored in the context. This means that you should not use global variables in plug-ins.
	/// </remarks>
	public void Execute(IServiceProvider serviceProvider)
	{
		if (serviceProvider is null)
		{
			throw new ArgumentNullException(nameof(serviceProvider));
		}

		// Build a local service provider
		using var localServiceProvider = serviceProvider.BuildServiceProvider(OnBeforeBuildServiceProvider);

		try
		{
			localServiceProvider.Trace(string.Format(CultureInfo.InvariantCulture, "Entered {0}.Execute()", ChildClassName));
			var context = localServiceProvider.GetRequiredService<IPluginExecutionContext>();

			// Find the matching registration to determine if we need to register IPluginContext
			var matchingRegistration = GetMatchingRegistration(context);
			var pluginAction = matchingRegistration?.Action;

			// If action is null but we have a handler method name, try to discover generated action wrapper
			if (pluginAction == null && matchingRegistration?.HandlerMethodName != null)
			{
				pluginAction = DiscoverGeneratedAction(matchingRegistration);
			}

			if (pluginAction == null)
			{
				// If we have a handler method but no generated wrapper, provide a clear error
				if (matchingRegistration?.HandlerMethodName != null)
				{
					throw new InvalidPluginExecutionException(
						OperationStatus.Failed,
						string.Format(
							CultureInfo.InvariantCulture,
							"Plugin step registration for Entity: {0}, Message: {1} in {2} uses method reference " +
							"'{3}' but no generated ActionWrapper was found. Ensure the source generator is running.",
							context.PrimaryEntityName,
							context.MessageName,
							ChildClassName,
							matchingRegistration.HandlerMethodName
						));
				}

				localServiceProvider.Trace(string.Format(
					CultureInfo.InvariantCulture,
					"No registered event found for Entity: {0}, Message: {1} in {2}",
					context.PrimaryEntityName,
					context.MessageName,
					ChildClassName
				));

				return;
			}

			localServiceProvider.Trace(string.Format(
				CultureInfo.InvariantCulture,
				"{0} is firing for Entity: {1}, Message: {2}",
				ChildClassName,
				context.PrimaryEntityName,
				context.MessageName
				));

			pluginAction.Invoke(localServiceProvider);
		}
		catch (InvalidPluginExecutionException e)
		{
			localServiceProvider.Trace(string.Format(CultureInfo.InvariantCulture, "Exception: {0}", e.ToString()));

			// Rethrow InvalidPluginExecutionException without wrapping
			throw;
		}
		catch (FaultException<OrganizationServiceFault> e)
		{
			localServiceProvider.Trace(string.Format(CultureInfo.InvariantCulture, "Exception: {0}", e.ToString()));
			throw new InvalidPluginExecutionException(OperationStatus.Failed, e.Message);
		}
		catch (NotImplementedException e)
		{
			localServiceProvider.Trace(string.Format(CultureInfo.InvariantCulture, "Exception: {0}", e.ToString()));
			throw new InvalidPluginExecutionException(OperationStatus.Failed, e.Message);
		}
		catch (Exception e)
		{
			localServiceProvider.Trace(string.Format(CultureInfo.InvariantCulture, "Unexpected exception: {0}", e.ToString()));
			throw new InvalidPluginExecutionException(OperationStatus.Failed, $"Unexpected error in {ChildClassName}: {e.Message}");
		}
		finally
		{
			localServiceProvider.Trace(string.Format(CultureInfo.InvariantCulture, "Exiting {0}.Execute()", ChildClassName));
		}
	}

	/// <summary>
	/// Get the plugin step configurations.
	/// </summary>
	/// <returns>List of steps</returns>
	public IEnumerable<IPluginStepConfig> GetRegistrations()
		=> RegisteredPluginSteps.Select(registration => registration.ConfigBuilder.Build());

	/// <summary>
	/// Get the CustomAPI configuration, if any.
	/// </summary>
	/// <returns>The registered CustomAPI, or null</returns>
	public ICustomApiConfig GetRegistration()
		=> RegisteredCustomApi?.ConfigBuilder?.Build();

	/// <summary>
	/// Register a plugin step for the given entity type, event operation, and execution stage with the given action.<br/>
	/// The action will get passed a <see cref="LocalPluginContext"/>.
	/// </summary>
	/// <typeparam name="T">The entity type to register the plugin for</typeparam>
	/// <param name="eventOperation">The event operation to register the plugin for</param>
	/// <param name="executionStage">The execution stage of the plugin registration</param>
	/// <param name="action">The action to execute</param>
	/// <returns>The <see cref="PluginStepConfigBuilder{T}"/> to register filters and images</returns>
	[Obsolete("Use RegisterStep instead")]
	protected PluginStepConfigBuilder<T> RegisterPluginStep<T>(
		EventOperation eventOperation, ExecutionStage executionStage, Action<LocalPluginContext> action)
		where T : Entity, new()
	{
		return RegisterStep<T>(eventOperation.ToString(), executionStage, sp => action(new LocalPluginContext(sp)));
	}

	/// <summary>
	/// Register a plugin step for the given entity type, event operation, and execution stage with the given action.<br/>
	/// The action will get passed a <see cref="LocalPluginContext"/>.<br/>
	/// <br/>
	/// <b>
	/// NOTE: It is strongly adviced to use the <see cref="RegisterPluginStep{T}(EventOperation, ExecutionStage, Action{LocalPluginContext})"/> method instead if possible.<br/>
	/// Only use this method if you are registering for a non-standard message.
	/// </b>
	/// </summary>
	/// <typeparam name="T">The entity type to register the plugin for</typeparam>
	/// <param name="eventOperation">The event operation to register the plugin for</param>
	/// <param name="executionStage">The execution stage of the plugin registration</param>
	/// <param name="action">The action to execute</param>
	/// <returns>The <see cref="PluginStepConfigBuilder{T}"/> to register filters and images</returns>
	[Obsolete("Use RegisterStep instead")]
	protected PluginStepConfigBuilder<T> RegisterPluginStep<T>(
		string eventOperation, ExecutionStage executionStage, Action<LocalPluginContext> action)
		where T : Entity, new()
	{
		return RegisterStep<T>(eventOperation, executionStage, sp => action(new LocalPluginContext(sp)));
	}

	/// <summary>
	/// Register a plugin step for the given entity type, event operation, and execution stage with the given action.<br/>
	/// The action will get passed an instance of <typeparamref name="TService"/>.
	/// </summary>
	/// <typeparam name="TEntity">The entity type to register the plugin for</typeparam>
	/// <typeparam name="TService">The service type to pass to the action</typeparam>
	/// <param name="eventOperation">The event operation to register the plugin for</param>
	/// <param name="executionStage">The execution stage of the plugin registration</param>
	/// <param name="action">The action to execute</param>
	/// <returns>The <see cref="PluginStepConfigBuilder{T}"/> to register filters and images</returns>
	protected PluginStepConfigBuilder<TEntity> RegisterStep<TEntity, TService>(
		EventOperation eventOperation, ExecutionStage executionStage, Action<TService> action)
		where TEntity : Entity, new()
	{
		return RegisterStep<TEntity>(eventOperation.ToString(), executionStage, sp => action(sp.GetRequiredService<TService>()));
	}

	/// <summary>
	/// Register a plugin step for the given entity type, event operation, and execution stage with the given action.<br/>
	/// The action will get passed an instance of <typeparamref name="TService"/>.
	/// <br/>
	/// <b>
	/// NOTE: It is strongly adviced to use the <see cref="RegisterStep{TEntity, TService}(EventOperation, ExecutionStage, Action{TService})"/> method instead if possible.<br/>
	/// Only use this method if you are registering for a non-standard message.
	/// </b>
	/// </summary>
	/// <typeparam name="TEntity">The entity type to register the plugin for</typeparam>
	/// <typeparam name="TService">The service type to pass to the action</typeparam>
	/// <param name="eventOperation">The event operation to register the plugin for</param>
	/// <param name="executionStage">The execution stage of the plugin registration</param>
	/// <param name="action">The action to execute</param>
	/// <returns>The <see cref="PluginStepConfigBuilder{T}"/> to register filters and images</returns>
	protected PluginStepConfigBuilder<TEntity> RegisterStep<TEntity, TService>(
		string eventOperation, ExecutionStage executionStage, Action<TService> action)
		where TEntity : Entity, new()
	{
		return RegisterStep<TEntity>(eventOperation, executionStage, sp => action(sp.GetRequiredService<TService>()));
	}

	/// <summary>
	/// Register a plugin step for the given entity type, event operation, and execution stage with the given action.<br/>
	/// The action will get passed a <see cref="IServiceProvider"/>.
	/// </summary>
	/// <typeparam name="T">The entity type to register the plugin for</typeparam>
	/// <param name="eventOperation">The event operation to register the plugin for</param>
	/// <param name="executionStage">The execution stage of the plugin registration</param>
	/// <param name="action">The action to execute</param>
	/// <returns>The <see cref="PluginStepConfigBuilder{T}"/> to register filters and images</returns>
	protected PluginStepConfigBuilder<T> RegisterStep<T>(
		EventOperation eventOperation, ExecutionStage executionStage, Action<IExtendedServiceProvider> action)
		where T : Entity, new()
	{
		return RegisterStep<T>(eventOperation.ToString(), executionStage, action);
	}

	/// <summary>
	/// Register a plugin step for the given entity type, event operation, and execution stage with the given action.<br/>
	/// The action will get passed a <see cref="IServiceProvider"/>.
	/// <br/>
	/// <b>
	/// NOTE: It is strongly adviced to use the <see cref="RegisterStep{T}(EventOperation, ExecutionStage, Action{IExtendedServiceProvider})"/> method instead if possible.<br/>
	/// Only use this method if you are registering for a non-standard message.
	/// </b>
	/// </summary>
	/// <typeparam name="T">The entity type to register the plugin for</typeparam>
	/// <param name="eventOperation">The event operation to register the plugin for</param>
	/// <param name="executionStage">The execution stage of the plugin registration</param>
	/// <param name="action">The action to execute</param>
	/// <returns>The <see cref="PluginStepConfigBuilder{T}"/> to register filters and images</returns>
	protected PluginStepConfigBuilder<T> RegisterStep<T>(
		string eventOperation, ExecutionStage executionStage, Action<IExtendedServiceProvider> action)
		where T : Entity, new()
	{
		var builder = new PluginStepConfigBuilder<T>(eventOperation, executionStage);
		var registration = new PluginStepRegistration(
			pluginStepConfig: builder,
			action: action);
		RegisteredPluginSteps.Add(registration);
		return builder;
	}

	/// <summary>
	/// Register a plugin step for the given entity type with a handler method name.
	/// The source generator will emit an ActionWrapper that calls the specified method.
	/// Use WithPreImage/WithPostImage to add images - the method signature must match.
	/// <br/>
	/// Use <c>nameof(IService.MethodName)</c> for compile-time safety.
	/// </summary>
	/// <typeparam name="TEntity">The entity type to register the plugin for</typeparam>
	/// <typeparam name="TService">The service type that contains the handler method</typeparam>
	/// <param name="eventOperation">The event operation to register the plugin for</param>
	/// <param name="executionStage">The execution stage of the plugin registration</param>
	/// <param name="handlerMethodName">The name of the handler method (use nameof(IService.MethodName))</param>
	/// <returns>A <see cref="PluginStepConfigBuilder{TEntity}"/> for configuring images and filtered attributes</returns>
	protected PluginStepConfigBuilder<TEntity> RegisterStep<TEntity, TService>(
		EventOperation eventOperation,
		ExecutionStage executionStage,
		string handlerMethodName)
		where TEntity : Entity, new()
	{
		return RegisterStep<TEntity, TService>(eventOperation.ToString(), executionStage, handlerMethodName);
	}

	/// <summary>
	/// Register a plugin step for the given entity type with a handler method name.
	/// The source generator will emit an ActionWrapper that calls the specified method.
	/// Use WithPreImage/WithPostImage to add images - the method signature must match.
	/// <br/>
	/// Use <c>nameof(IService.MethodName)</c> for compile-time safety.
	/// <br/>
	/// <b>
	/// NOTE: It is strongly advised to use the <see cref="RegisterStep{TEntity, TService}(EventOperation, ExecutionStage, string)"/> method instead if possible.<br/>
	/// Only use this method if you are registering for a non-standard message.
	/// </b>
	/// </summary>
	/// <typeparam name="TEntity">The entity type to register the plugin for</typeparam>
	/// <typeparam name="TService">The service type that contains the handler method</typeparam>
	/// <param name="eventOperation">The event operation to register the plugin for</param>
	/// <param name="executionStage">The execution stage of the plugin registration</param>
	/// <param name="handlerMethodName">The name of the handler method (use nameof(IService.MethodName))</param>
	/// <returns>A <see cref="PluginStepConfigBuilder{TEntity}"/> for configuring images and filtered attributes</returns>
	protected PluginStepConfigBuilder<TEntity> RegisterStep<TEntity, TService>(
		string eventOperation,
		ExecutionStage executionStage,
		string handlerMethodName)
		where TEntity : Entity, new()
	{
		var builder = new PluginStepConfigBuilder<TEntity>(eventOperation, executionStage);

		// Compute the generated ActionWrapper type name up front. Must match what the source generator
		// emits: a registration-derived, sanitized type prefix in the plugin's own namespace, i.e.
		// {Namespace}.{Sanitize(PluginClass + Entity + Operation + Stage)}ActionWrapper.
		var wrapperTypeName =
			$"{ChildClassNamespace}." +
			$"{IdentifierSanitizer.Sanitize($"{ChildClassShortName}{typeof(TEntity).Name}{eventOperation}{executionStage}")}ActionWrapper";

		var registration = new PluginStepRegistration(
			pluginStepConfig: builder,
			action: null,
			handlerMethodName: handlerMethodName,
			wrapperTypeName: wrapperTypeName);

		RegisteredPluginSteps.Add(registration);
		return builder;
	}

	private Action<IExtendedServiceProvider> DiscoverGeneratedAction(PluginStepRegistration registration)
	{
		// The wrapper type name is computed at registration time (the single source of truth), so plugin
		// steps and Custom APIs share this discovery path regardless of their naming convention.
		var wrapperTypeName = registration.WrapperTypeName;
		if (string.IsNullOrEmpty(wrapperTypeName))
			return null;

		var wrapperType = GetType().Assembly.GetType(wrapperTypeName);
		if (wrapperType == null)
			return null;

		// Use interface instead of reflection
		var wrapper = Activator.CreateInstance(wrapperType) as IActionWrapper
			?? throw new InvalidOperationException(
				$"Generated type '{wrapperTypeName}' could not be instantiated or does not implement IActionWrapper.");
		return wrapper.CreateAction();
	}

	/// <summary>
	/// <para>
	/// Register a CustomAPI with the given name and action.<br/>
	/// Returns the config builder for specifying additional settings
	/// </para>
	/// </summary>
	/// <exception cref="InvalidOperationException">If called multiple times in the same class</exception>
	protected CustomApiConfigBuilder RegisterCustomAPI(string name, Action<LocalPluginContext> action)
	{
		return RegisterAPI(name, sp => action(new LocalPluginContext(sp)));
	}

	/// <summary>
	/// <para>
	/// Register a CustomAPI with the given name and action.<br/>
	/// Returns the config builder for specifying additional settings
	/// </para>
	/// </summary>
	/// <exception cref="InvalidOperationException">If called multiple times in the same class</exception>
	protected CustomApiConfigBuilder RegisterAPI<TService>(string name, Action<TService> action)
	{
		return RegisterAPI(name, sp => action(sp.GetRequiredService<TService>()));
	}

	/// <summary>
	/// <para>
	/// Register a CustomAPI with a handler method name.<br/>
	/// The source generator emits type-safe <c>Request</c>/<c>Response</c> classes (named after the API)
	/// from the <c>AddRequestParameter</c>/<c>AddResponseProperty</c> calls, and an ActionWrapper that
	/// marshals the execution context's InputParameters into the request and the returned response into
	/// the OutputParameters. The handler method must accept the generated request (when request parameters
	/// are declared) and return the generated response (when response properties are declared).
	/// </para>
	/// <para>
	/// Use <c>nameof(TService.MethodName)</c> for compile-time safety.
	/// </para>
	/// </summary>
	/// <typeparam name="TService">The service type that contains the handler method</typeparam>
	/// <param name="name">The unique name of the Custom API</param>
	/// <param name="handlerMethodName">The name of the handler method (use nameof(TService.MethodName))</param>
	/// <exception cref="ArgumentException">If <paramref name="name"/> or <paramref name="handlerMethodName"/> is null or whitespace</exception>
	/// <exception cref="InvalidOperationException">If called multiple times in the same class</exception>
	protected CustomApiConfigBuilder RegisterAPI<TService>(string name, string handlerMethodName)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("A Custom API name must be provided.", nameof(name));
		}

		if (string.IsNullOrWhiteSpace(handlerMethodName))
		{
			throw new ArgumentException(
				"A handler method name must be provided. Use nameof(TService.MethodName) for compile-time safety.",
				nameof(handlerMethodName));
		}

		if (RegisteredCustomApi != null)
		{
			throw new InvalidOperationException("You cannot register multiple CustomAPIs in the same class");
		}

		var configBuilder = new CustomApiConfigBuilder(name);
		RegisteredCustomApi = new CustomApiRegistration(configBuilder, handlerMethodName);

		return configBuilder;
	}

	/// <summary>
	/// <para>
	/// Register a CustomAPI with the given name and action.<br/>
	/// Returns the config builder for specifying additional settings
	/// </para>
	/// </summary>
	/// <exception cref="InvalidOperationException">If called multiple times in the same class</exception>
	protected CustomApiConfigBuilder RegisterAPI(string name, Action<IExtendedServiceProvider> action)
	{
		if (RegisteredCustomApi != null)
		{
			throw new InvalidOperationException("You cannot register multiple CustomAPIs in the same class");
		}

		var configBuilder = new CustomApiConfigBuilder(name);
		RegisteredCustomApi = new CustomApiRegistration(configBuilder, action);

		return configBuilder;
	}

	private PluginStepRegistration GetMatchingRegistration(IPluginExecutionContext context)
	{
		// Iterate over all of the expected registered events to ensure that the plugin
		// has been invoked by an expected event
		// For any given plug-in event at an instance in time, we would expect at most 1 result to match.
		var pluginStepRegistration = RegisteredPluginSteps.FirstOrDefault(a => a.ConfigBuilder?.Matches(context) == true);

		// If no plugin step found and we have a CustomAPI, return a registration with that action
		if (pluginStepRegistration == null && RegisteredCustomApi != null)
		{
			// Type-safe Custom APIs have no action; they defer to a source-generated ActionWrapper
			// discovered by naming convention from the (sanitized) API name in the plugin's namespace.
			string handlerMethodName = null;
			string wrapperTypeName = null;
			if (RegisteredCustomApi.Action == null && RegisteredCustomApi.HandlerMethodName != null)
			{
				handlerMethodName = RegisteredCustomApi.HandlerMethodName;
				wrapperTypeName = $"{ChildClassNamespace}.{IdentifierSanitizer.Sanitize(RegisteredCustomApi.ConfigBuilder.Name)}ActionWrapper";
			}

			return new PluginStepRegistration(
				pluginStepConfig: null,
				action: RegisteredCustomApi.Action,
				handlerMethodName: handlerMethodName,
				wrapperTypeName: wrapperTypeName);
		}

		return pluginStepRegistration;
	}
}
